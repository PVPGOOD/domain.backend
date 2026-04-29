using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Domain.Backend.Api.Models.Requests;
using Domain.Backend.Api.Models.Responses;
using Domain.Backend.Sql;
using Domain.Backend.Tasks.Models.Entities;
using Domain.Backend.Tasks.Models.Enums;
using Domain.Backend.Tasks.Search;
using Domain.Backend.Utilities.DomainNames;
using Domain.Backend.Utilities.Pricing;
using Microsoft.EntityFrameworkCore;

namespace Domain.Backend.Tasks;

public sealed class DomainSearchTaskService(
    DomainBackendDbContext db,
    IEnumerable<IDomainSearchHandler> handlers) : IDomainSearchTaskService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly Dictionary<SearchMode, IDomainSearchHandler> _handlers = handlers.ToDictionary(handler => handler.Mode);

    public async Task<CreateTaskResponse> CreateAsync(CreateDomainSearchTaskRequest request, CancellationToken cancellationToken)
    {
        if (!_handlers.TryGetValue(request.Mode, out var handler))
        {
            throw new DomainSearchValidationException("Unsupported search mode.", "mode");
        }

        var candidates = handler.GenerateCandidates(request);
        if (candidates.Count == 0)
        {
            throw new DomainSearchValidationException("Task must contain at least one candidate domain.", "payload");
        }

        var now = DateTimeOffset.UtcNow;
        var task = new DomainSearchTask
        {
            Id = Guid.NewGuid(),
            Name = string.IsNullOrWhiteSpace(request.Name) ? GenerateName(request, candidates.Count) : request.Name.Trim(),
            Mode = request.Mode,
            Status = DomainSearchTaskStatus.Pending,
            RequestJson = JsonSerializer.Serialize(request, JsonOptions),
            TotalCount = candidates.Count,
            CreatedAt = now
        };

        db.DomainSearchTasks.Add(task);
        await db.SaveChangesAsync(cancellationToken);

        return new CreateTaskResponse(task.Id, task.Status.ToString(), task.CreatedAt);
    }

    public async Task<DomainSearchTaskResponse?> GetAsync(Guid taskId, CancellationToken cancellationToken)
    {
        var task = await db.DomainSearchTasks
            .AsNoTracking()
            .FirstOrDefaultAsync(item => item.Id == taskId && item.Status != DomainSearchTaskStatus.Deleted, cancellationToken);

        return task is null ? null : ToTaskResponse(task);
    }

    public async Task<PagedResponse<DomainSearchTaskListItemResponse>> QueryAsync(QueryTasksRequest request, CancellationToken cancellationToken)
    {
        var page = Math.Max(1, request.Page);
        var pageSize = Math.Clamp(request.PageSize, 1, 100);
        IQueryable<DomainSearchTask> query = db.DomainSearchTasks
            .AsNoTracking()
            .Where(task => task.Status != DomainSearchTaskStatus.Deleted);

        if (request.Status.HasValue)
        {
            query = query.Where(task => task.Status == request.Status.Value);
        }

        var total = await query.CountAsync(cancellationToken);
        var items = await query
            .OrderByDescending(task => task.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(task => new DomainSearchTaskListItemResponse
            {
                TaskId = task.Id,
                Name = task.Name,
                Mode = task.Mode.ToString(),
                Status = task.Status.ToString(),
                TotalCount = task.TotalCount,
                CompletedCount = task.CompletedCount,
                RemainingCount = Math.Max(0, task.TotalCount - task.CompletedCount),
                AvailableCount = task.AvailableCount,
                RegisteredCount = task.RegisteredCount,
                UnknownCount = task.UnknownCount,
                ErrorCount = task.ErrorCount,
                CreatedAt = task.CreatedAt,
                StartedAt = task.StartedAt,
                FinishedAt = task.FinishedAt
            })
            .ToArrayAsync(cancellationToken);

        return new PagedResponse<DomainSearchTaskListItemResponse>
        {
            Items = items,
            Page = page,
            PageSize = pageSize,
            Total = total
        };
    }

    public async Task<PagedResponse<DomainSearchResultResponse>?> QueryResultsAsync(QueryTaskResultsRequest request, CancellationToken cancellationToken)
    {
        var taskExists = await db.DomainSearchTasks
            .AnyAsync(task => task.Id == request.TaskId && task.Status != DomainSearchTaskStatus.Deleted, cancellationToken);
        if (!taskExists)
        {
            return null;
        }

        var page = Math.Max(1, request.Page);
        var pageSize = Math.Clamp(request.PageSize, 1, 500);
        IQueryable<DomainSearchResult> query = db.DomainSearchResults
            .Where(result => result.TaskId == request.TaskId);

        if (request.Availability.HasValue)
        {
            query = query.Where(result => result.Availability == request.Availability.Value);
        }

        var total = await query.CountAsync(cancellationToken);
        var results = await query
            .OrderByDescending(result => result.CheckedAt)
            .ThenBy(result => result.Domain)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToArrayAsync(cancellationToken);
        var items = results.Select(ToResultResponse).ToArray();
        RestoreRegistrationPriceSnapshots(items, results);

        return new PagedResponse<DomainSearchResultResponse>
        {
            Items = items,
            Page = page,
            PageSize = pageSize,
            Total = total
        };
    }

    public async Task<TaskActionResponse?> CancelAsync(Guid taskId, CancellationToken cancellationToken)
    {
        var task = await db.DomainSearchTasks.FirstOrDefaultAsync(item => item.Id == taskId, cancellationToken);
        if (task is null || task.Status == DomainSearchTaskStatus.Deleted)
        {
            return null;
        }

        if (task.Status == DomainSearchTaskStatus.Pending)
        {
            task.Status = DomainSearchTaskStatus.Cancelled;
            task.FinishedAt = DateTimeOffset.UtcNow;
        }
        else if (task.Status == DomainSearchTaskStatus.Running)
        {
            task.CancelRequested = true;
        }

        await db.SaveChangesAsync(cancellationToken);
        return new TaskActionResponse(task.Id, task.Status.ToString());
    }

    public async Task<TaskActionResponse?> DeleteAsync(Guid taskId, CancellationToken cancellationToken)
    {
        var task = await db.DomainSearchTasks.FirstOrDefaultAsync(item => item.Id == taskId, cancellationToken);
        if (task is null || task.Status == DomainSearchTaskStatus.Deleted)
        {
            return null;
        }

        if (task.Status == DomainSearchTaskStatus.Running)
        {
            task.Status = DomainSearchTaskStatus.Deleting;
            task.CancelRequested = true;
        }
        else
        {
            task.Status = DomainSearchTaskStatus.Deleted;
            task.DeletedAt = DateTimeOffset.UtcNow;
            task.FinishedAt ??= task.DeletedAt;
        }

        await db.SaveChangesAsync(cancellationToken);
        return new TaskActionResponse(task.Id, task.Status.ToString());
    }

    private static DomainSearchTaskResponse ToTaskResponse(DomainSearchTask task)
    {
        var percent = task.TotalCount == 0
            ? 0
            : decimal.Round(task.CompletedCount * 100m / task.TotalCount, 2);

        return new DomainSearchTaskResponse
        {
            TaskId = task.Id,
            Name = task.Name,
            Mode = task.Mode.ToString(),
            Status = task.Status.ToString(),
            Progress = new TaskProgressResponse
            {
                TotalCount = task.TotalCount,
                CompletedCount = task.CompletedCount,
                RemainingCount = Math.Max(0, task.TotalCount - task.CompletedCount),
                Percent = percent
            },
            Summary = new TaskSummaryResponse
            {
                AvailableCount = task.AvailableCount,
                RegisteredCount = task.RegisteredCount,
                UnknownCount = task.UnknownCount,
                ErrorCount = task.ErrorCount
            },
            CreatedAt = task.CreatedAt,
            StartedAt = task.StartedAt,
            FinishedAt = task.FinishedAt,
            ErrorMessage = task.ErrorMessage
        };
    }

    private static DomainSearchResultResponse ToResultResponse(DomainSearchResult result)
    {
        return new DomainSearchResultResponse
        {
            Id = result.Id,
            Domain = result.Domain,
            InputDomain = result.InputDomain,
            Tld = result.Tld,
            Availability = result.Availability.ToString(),
            WhoisServer = result.WhoisServer,
            CheckedAt = result.CheckedAt,
            ErrorMessage = result.ErrorMessage,
            RawWhois = result.RawWhois,
            ExpirationDate = result.ExpirationDate,
            ProxyId = result.ProxyId,
            ProxyElapsedMs = result.ProxyElapsedMs,
            ProxyWorkerId = result.ProxyWorkerId,
            DispatchInfo = result.DispatchInfo
        };
    }

    private static string GenerateName(CreateDomainSearchTaskRequest request, int candidateCount)
    {
        return request.Mode switch
        {
            SearchMode.Exact => $"Exact: {candidateCount} domains",
            SearchMode.Keyword => $"Keyword: {candidateCount} domains",
            SearchMode.ShortBatch => $"ShortBatch: {candidateCount} domains",
            _ => $"Task: {candidateCount} domains"
        };
    }

    private static void RestoreRegistrationPriceSnapshots(
        IReadOnlyList<DomainSearchResultResponse> responses,
        IReadOnlyList<DomainSearchResult> results)
    {
        var entitiesById = results.ToDictionary(static result => result.Id);

        foreach (var response in responses)
        {
            if (!entitiesById.TryGetValue(response.Id, out var result) ||
                string.IsNullOrWhiteSpace(result.RegistrationPriceSnapshotJson) ||
                !TryRestoreRegistrationPriceSnapshot(result.RegistrationPriceSnapshotJson, out var snapshot))
            {
                continue;
            }

            ApplyRegistrationPrice(response, snapshot);
        }
    }

    private static void ApplyRegistrationPrice(DomainSearchResultResponse result, TldRegistrationPrices price)
    {
        result.LowestRegistrationPrice = price.LowestRegistrationPrice;
        result.LowestRenewalPrice = price.LowestRenewalPrice;
        result.RegistrationPriceCurrency = price.Currency;
        result.RegistrationPriceRegistrarName = price.RegistrarName;
        result.RegistrationPriceRegistrarWeb = price.RegistrarWeb;
        result.RegistrationPriceErrorMessage = price.ErrorMessage;
        result.RegistrationPrices = price.Prices
            .Take(3)
            .Select(static item => new RegistrarRegistrationPriceResponse
            {
                Registrar = item.Registrar,
                RegistrarName = item.RegistrarName,
                RegistrarWeb = item.RegistrarWeb,
                RegistrationPrice = item.RegistrationPrice,
                RenewalPrice = item.RenewalPrice,
                TransferPrice = item.TransferPrice,
                Currency = item.Currency,
                HasRegistrationPromoCode = item.HasRegistrationPromoCode,
                UpdatedTime = item.UpdatedTime
            })
            .ToArray();
    }

    private static bool TryRestoreRegistrationPriceSnapshot(string json, out TldRegistrationPrices price)
    {
        try
        {
            price = JsonSerializer.Deserialize<TldRegistrationPrices>(json, JsonOptions) ?? new TldRegistrationPrices();
            return true;
        }
        catch (JsonException)
        {
            price = new TldRegistrationPrices();
            return false;
        }
    }

}
