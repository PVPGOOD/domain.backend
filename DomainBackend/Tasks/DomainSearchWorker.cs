using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Domain.Backend.Api.Models.Requests;
using Domain.Backend.Sql;
using Domain.Backend.Tasks.Models.Entities;
using Domain.Backend.Tasks.Models.Enums;
using Domain.Backend.Tasks.Search;
using Domain.Backend.Tasks.Whois;
using Domain.Backend.Tasks.Whois.Models;
using Domain.Backend.Utilities.Pricing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Domain.Backend.Tasks;

public sealed class DomainSearchWorker(
    IServiceScopeFactory scopeFactory,
    ILogger<DomainSearchWorker> logger) : BackgroundService
{
    private const int MaxTaskConcurrency = 100;
    private const int MaxFailureRetryRounds = 3;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var handled = await TryHandleOneTaskAsync(stoppingToken);
                if (!handled)
                {
                    await Task.Delay(TimeSpan.FromSeconds(1), stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Domain search worker failed while polling tasks.");
                await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken);
            }
        }
    }

    private async Task<bool> TryHandleOneTaskAsync(CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DomainBackendDbContext>();

        var task = await db.DomainSearchTasks
            .Where(item => item.Status == DomainSearchTaskStatus.Pending)
            .OrderBy(item => item.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);

        if (task is null)
        {
            return false;
        }

        task.Status = DomainSearchTaskStatus.Running;
        task.StartedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(cancellationToken);

        await ExecuteTaskAsync(scope.ServiceProvider, db, task, cancellationToken);
        return true;
    }

    private async Task ExecuteTaskAsync(
        IServiceProvider serviceProvider,
        DomainBackendDbContext db,
        DomainSearchTask task,
        CancellationToken cancellationToken)
    {
        try
        {
            var request = JsonSerializer.Deserialize<CreateDomainSearchTaskRequest>(task.RequestJson, JsonOptions)
                ?? throw new InvalidOperationException("Task request_json is invalid.");
            var handler = serviceProvider.GetServices<IDomainSearchHandler>().First(item => item.Mode == task.Mode);
            var availabilityProvider = serviceProvider.GetRequiredService<IDomainAvailabilityProvider>();
            var candidates = handler.GenerateCandidates(request);

            task.TotalCount = candidates.Count;
            await db.SaveChangesAsync(cancellationToken);

            using var taskCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            var dbLock = new SemaphoreSlim(1, 1);
            var retryCandidates = new ConcurrentBag<Search.Models.DomainCandidate>();
            await Parallel.ForEachAsync(candidates, new ParallelOptions
            {
                MaxDegreeOfParallelism = MaxTaskConcurrency,
                CancellationToken = taskCancellation.Token
            }, async (candidate, token) =>
            {
                if (await TryApplyStopRequestAsync(db, task, dbLock, taskCancellation, token))
                {
                    return;
                }

                DomainAvailabilityResult check;
                try
                {
                    check = await availabilityProvider.CheckAsync(candidate, token);
                }
                catch (OperationCanceledException) when (taskCancellation.IsCancellationRequested)
                {
                    return;
                }
                catch (Exception ex)
                {
                    check = new DomainAvailabilityResult(
                        candidate.Domain,
                        candidate.Tld,
                        DomainAvailability.Error,
                        null,
                        ex.Message);
                }

                if (ShouldRetry(check.Availability))
                {
                    retryCandidates.Add(candidate);
                    return;
                }

                await PersistResultAsync(db, task, dbLock, candidate, check, isRetry: false, token);
            });

            for (var retryRound = 1; retryRound <= MaxFailureRetryRounds && !retryCandidates.IsEmpty; retryRound++)
            {
                var currentRetryCandidates = retryCandidates.ToArray();
                retryCandidates.Clear();

                await Task.Delay(GetRetryDelay(retryRound), taskCancellation.Token);

                await Parallel.ForEachAsync(currentRetryCandidates, new ParallelOptions
                {
                    MaxDegreeOfParallelism = MaxTaskConcurrency,
                    CancellationToken = taskCancellation.Token
                }, async (candidate, token) =>
                {
                    if (await TryApplyStopRequestAsync(db, task, dbLock, taskCancellation, token))
                    {
                        return;
                    }

                    var check = await CheckCandidateAsync(availabilityProvider, candidate, taskCancellation, token);
                    if (ShouldRetry(check.Availability) && retryRound < MaxFailureRetryRounds)
                    {
                        retryCandidates.Add(candidate);
                        return;
                    }

                    await PersistResultAsync(db, task, dbLock, candidate, check, isRetry: false, token, retryRound);
                });
            }

            await dbLock.WaitAsync(cancellationToken);
            try
            {
                await db.Entry(task).ReloadAsync(cancellationToken);
                if (task.Status == DomainSearchTaskStatus.Running)
                {
                    task.Status = DomainSearchTaskStatus.Completed;
                    task.FinishedAt = DateTimeOffset.UtcNow;
                    await db.SaveChangesAsync(cancellationToken);
                }
            }
            finally
            {
                dbLock.Release();
                dbLock.Dispose();
            }

            await EnrichAvailableResultPricesAsync(serviceProvider, db, task.Id, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            await db.Entry(task).ReloadAsync(CancellationToken.None);
            if (task.Status == DomainSearchTaskStatus.Running)
            {
                task.Status = DomainSearchTaskStatus.Cancelled;
                task.FinishedAt = DateTimeOffset.UtcNow;
                await db.SaveChangesAsync(CancellationToken.None);
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Domain search task {TaskId} failed.", task.Id);
            task.Status = DomainSearchTaskStatus.Failed;
            task.ErrorMessage = ex.Message;
            task.FinishedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(CancellationToken.None);
        }
    }

    private static async Task<bool> TryApplyStopRequestAsync(
        DomainBackendDbContext db,
        DomainSearchTask task,
        SemaphoreSlim dbLock,
        CancellationTokenSource taskCancellation,
        CancellationToken cancellationToken)
    {
        await dbLock.WaitAsync(cancellationToken);
        try
        {
            await db.Entry(task).ReloadAsync(cancellationToken);
            if (!task.CancelRequested && task.Status != DomainSearchTaskStatus.Deleting)
            {
                return false;
            }

            task.Status = task.Status == DomainSearchTaskStatus.Deleting
                ? DomainSearchTaskStatus.Deleted
                : DomainSearchTaskStatus.Cancelled;
            task.DeletedAt ??= task.Status == DomainSearchTaskStatus.Deleted ? DateTimeOffset.UtcNow : null;
            task.FinishedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(cancellationToken);
            await taskCancellation.CancelAsync();
            return true;
        }
        finally
        {
            dbLock.Release();
        }
    }

    private static async Task PersistResultAsync(
        DomainBackendDbContext db,
        DomainSearchTask task,
        SemaphoreSlim dbLock,
        Search.Models.DomainCandidate candidate,
        Whois.Models.DomainAvailabilityResult check,
        bool isRetry,
        CancellationToken cancellationToken,
        int retryRound = 0)
    {
        await dbLock.WaitAsync(cancellationToken);
        try
        {
            await db.Entry(task).ReloadAsync(cancellationToken);
            if (task.Status != DomainSearchTaskStatus.Running)
            {
                return;
            }

            var existingResult = isRetry
                ? await db.DomainSearchResults.FirstOrDefaultAsync(
                    result => result.TaskId == task.Id && result.Domain == candidate.Domain,
                    cancellationToken)
                : null;

            if (existingResult is not null)
            {
                DecrementSummary(task, existingResult.Availability);
                existingResult.Availability = check.Availability;
                existingResult.WhoisServer = check.WhoisServer;
                existingResult.ErrorMessage = check.ErrorMessage;
                existingResult.RawWhois = check.RawWhois;
                existingResult.ExpirationDate = check.ExpirationDate;
                existingResult.ProxyId = check.ProxyId;
                existingResult.ProxyElapsedMs = check.ProxyElapsedMs;
                existingResult.ProxyWorkerId = check.ProxyWorkerId;
                existingResult.DispatchInfo = AppendRetryInfo(check.DispatchInfo, check.Availability, retryRound);
                existingResult.CheckedAt = DateTimeOffset.UtcNow;
                IncrementSummary(task, check.Availability);
                await db.SaveChangesAsync(cancellationToken);
                return;
            }

            db.DomainSearchResults.Add(new DomainSearchResult
            {
                Id = Guid.NewGuid(),
                TaskId = task.Id,
                Domain = candidate.Domain,
                InputDomain = candidate.InputDomain,
                Tld = candidate.Tld,
                Availability = check.Availability,
                WhoisServer = check.WhoisServer,
                ErrorMessage = check.ErrorMessage,
                RawWhois = check.RawWhois,
                ExpirationDate = check.ExpirationDate,
                ProxyId = check.ProxyId,
                ProxyElapsedMs = check.ProxyElapsedMs,
                ProxyWorkerId = check.ProxyWorkerId,
                DispatchInfo = retryRound > 0 ? AppendRetryInfo(check.DispatchInfo, check.Availability, retryRound) : check.DispatchInfo,
                CheckedAt = DateTimeOffset.UtcNow
            });

            if (!isRetry)
            {
                task.CompletedCount++;
            }

            IncrementSummary(task, check.Availability);
            await db.SaveChangesAsync(cancellationToken);
        }
        finally
        {
            dbLock.Release();
        }
    }

    private static async Task<DomainAvailabilityResult> CheckCandidateAsync(
        IDomainAvailabilityProvider availabilityProvider,
        Search.Models.DomainCandidate candidate,
        CancellationTokenSource taskCancellation,
        CancellationToken cancellationToken)
    {
        try
        {
            return await availabilityProvider.CheckAsync(candidate, cancellationToken);
        }
        catch (OperationCanceledException) when (taskCancellation.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            return new DomainAvailabilityResult(
                candidate.Domain,
                candidate.Tld,
                DomainAvailability.Error,
                null,
                ex.Message,
                DispatchInfo: "Unhandled backend availability check failure; added to retry list.");
        }
    }

    private static bool ShouldRetry(DomainAvailability availability)
    {
        return availability is DomainAvailability.Error or DomainAvailability.RateLimited;
    }

    private static TimeSpan GetRetryDelay(int retryRound)
    {
        return TimeSpan.FromSeconds(retryRound * 2);
    }

    private async Task EnrichAvailableResultPricesAsync(
        IServiceProvider serviceProvider,
        DomainBackendDbContext db,
        Guid taskId,
        CancellationToken cancellationToken)
    {
        try
        {
            var results = await db.DomainSearchResults
                .Where(result => result.TaskId == taskId &&
                    result.Availability == DomainAvailability.Available &&
                    result.RegistrationPriceSnapshotJson == null)
                .ToArrayAsync(cancellationToken);

            var tlds = results
                .Select(static result => NormalizeTld(result.Tld))
                .Where(static tld => !string.IsNullOrWhiteSpace(tld))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();

            if (tlds.Length == 0)
            {
                return;
            }

            var priceService = serviceProvider.GetRequiredService<IDomainRegistrationPriceService>();
            var prices = await priceService.QueryRegistrationPricesAsync(tlds, cancellationToken);
            var pricesByTld = prices
                .Where(ShouldPersistRegistrationPriceSnapshot)
                .ToDictionary(static price => NormalizeTld(price.Tld), StringComparer.OrdinalIgnoreCase);

            if (pricesByTld.Count == 0)
            {
                return;
            }

            var snapshotAt = DateTimeOffset.UtcNow;
            foreach (var result in results)
            {
                if (!pricesByTld.TryGetValue(NormalizeTld(result.Tld), out var price))
                {
                    continue;
                }

                result.RegistrationPriceSnapshotJson = JsonSerializer.Serialize(price, JsonOptions);
                result.RegistrationPriceSnapshotAt = snapshotAt;
            }

            await db.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Failed to enrich registration prices for task {TaskId}.", taskId);
        }
    }

    private static bool ShouldPersistRegistrationPriceSnapshot(TldRegistrationPrices price)
    {
        return price.LowestRegistrationPrice is not null || price.Prices.Count > 0;
    }

    private static string NormalizeTld(string value)
    {
        return value.Trim().TrimStart('.').ToLowerInvariant();
    }

    private static string? AppendRetryInfo(string? dispatchInfo, DomainAvailability availability, int retryRound)
    {
        var retryInfo = ShouldRetry(availability)
            ? $"retry round {retryRound}/{MaxFailureRetryRounds} attempted; still failed"
            : $"retry round {retryRound}/{MaxFailureRetryRounds} attempted; recovered";

        return string.IsNullOrWhiteSpace(dispatchInfo)
            ? retryInfo
            : dispatchInfo + "; " + retryInfo;
    }

    private static void IncrementSummary(DomainSearchTask task, DomainAvailability availability)
    {
        switch (availability)
        {
            case DomainAvailability.Available:
                task.AvailableCount++;
                break;
            case DomainAvailability.Registered:
                task.RegisteredCount++;
                break;
            case DomainAvailability.Unknown:
            case DomainAvailability.Reserved:
                task.UnknownCount++;
                break;
            case DomainAvailability.Error:
            case DomainAvailability.RateLimited:
            case DomainAvailability.UnsupportedTld:
                task.ErrorCount++;
                break;
        }
    }

    private static void DecrementSummary(DomainSearchTask task, DomainAvailability availability)
    {
        switch (availability)
        {
            case DomainAvailability.Available:
                task.AvailableCount = Math.Max(0, task.AvailableCount - 1);
                break;
            case DomainAvailability.Registered:
                task.RegisteredCount = Math.Max(0, task.RegisteredCount - 1);
                break;
            case DomainAvailability.Unknown:
            case DomainAvailability.Reserved:
                task.UnknownCount = Math.Max(0, task.UnknownCount - 1);
                break;
            case DomainAvailability.Error:
            case DomainAvailability.RateLimited:
            case DomainAvailability.UnsupportedTld:
                task.ErrorCount = Math.Max(0, task.ErrorCount - 1);
                break;
        }
    }
}
