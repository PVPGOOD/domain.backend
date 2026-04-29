using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Domain.Backend.Tasks.Models.Enums;
using Domain.Backend.Tasks.Search.Models;
using Domain.Backend.Tasks.Whois.Models;

namespace Domain.Backend.Utilities.Rdap;

public sealed class IanaRdapLookupService(HttpClient httpClient) : IDomainRdapLookupService
{
    private static readonly Uri BootstrapUri = new("https://data.iana.org/rdap/dns.json");
    private static readonly TimeSpan BootstrapCacheDuration = TimeSpan.FromHours(12);
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private readonly SemaphoreSlim bootstrapLock = new(1, 1);
    private IReadOnlyDictionary<string, string[]> rdapServers = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
    private DateTimeOffset rdapServersExpiresAt;

    public async Task<DomainAvailabilityResult?> TryLookupAsync(DomainCandidate candidate, CancellationToken cancellationToken)
    {
        var servers = await GetServersAsync(candidate.Tld, cancellationToken);
        if (servers.Length == 0)
        {
            return null;
        }

        foreach (var server in servers)
        {
            var result = await TryLookupServerAsync(candidate, server, cancellationToken);
            if (result is not null)
            {
                return result;
            }
        }

        return null;
    }

    private async Task<string[]> GetServersAsync(string tld, CancellationToken cancellationToken)
    {
        await EnsureBootstrapAsync(cancellationToken);

        return rdapServers.TryGetValue(tld.Trim().TrimStart('.'), out var servers)
            ? servers
            : [];
    }

    private async Task EnsureBootstrapAsync(CancellationToken cancellationToken)
    {
        if (rdapServersExpiresAt > DateTimeOffset.UtcNow && rdapServers.Count > 0)
        {
            return;
        }

        await bootstrapLock.WaitAsync(cancellationToken);
        try
        {
            if (rdapServersExpiresAt > DateTimeOffset.UtcNow && rdapServers.Count > 0)
            {
                return;
            }

            using var response = await httpClient.GetAsync(BootstrapUri, cancellationToken);
            response.EnsureSuccessStatusCode();
            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);

            var map = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
            foreach (var service in document.RootElement.GetProperty("services").EnumerateArray())
            {
                if (service.GetArrayLength() < 2) continue;

                var tlds = service[0].EnumerateArray()
                    .Select(static item => item.GetString())
                    .Where(static item => !string.IsNullOrWhiteSpace(item))
                    .Select(static item => item!)
                    .ToArray();
                var urls = service[1].EnumerateArray()
                    .Select(static item => item.GetString())
                    .Where(static item => !string.IsNullOrWhiteSpace(item))
                    .Select(static item => item!)
                    .ToArray();

                foreach (var tld in tlds)
                {
                    map[tld] = urls;
                }
            }

            rdapServers = map;
            rdapServersExpiresAt = DateTimeOffset.UtcNow.Add(BootstrapCacheDuration);
        }
        catch
        {
            rdapServersExpiresAt = DateTimeOffset.UtcNow.AddMinutes(2);
        }
        finally
        {
            bootstrapLock.Release();
        }
    }

    private async Task<DomainAvailabilityResult?> TryLookupServerAsync(
        DomainCandidate candidate,
        string server,
        CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();

        try
        {
            using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(8));
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);
            using var response = await httpClient.GetAsync(BuildDomainUri(server, candidate.Domain), linkedCts.Token);
            var raw = await response.Content.ReadAsStringAsync(linkedCts.Token);
            stopwatch.Stop();

            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                return new DomainAvailabilityResult(
                    candidate.Domain,
                    candidate.Tld,
                    DomainAvailability.Unknown,
                    server,
                    "RDAP record was not found; availability is not confirmed.",
                    RawWhois: raw,
                    ProxyId: "rdap",
                    ProxyElapsedMs: stopwatch.ElapsedMilliseconds,
                    DispatchInfo: $"server {server}, protocol RDAP, {stopwatch.ElapsedMilliseconds}ms");
            }

            if (response.StatusCode == HttpStatusCode.TooManyRequests)
            {
                return new DomainAvailabilityResult(
                    candidate.Domain,
                    candidate.Tld,
                    DomainAvailability.RateLimited,
                    server,
                    "RDAP request was rate limited.",
                    RawWhois: raw,
                    ProxyId: "rdap",
                    ProxyElapsedMs: stopwatch.ElapsedMilliseconds,
                    DispatchInfo: $"server {server}, protocol RDAP, {stopwatch.ElapsedMilliseconds}ms");
            }

            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            using var document = JsonDocument.Parse(raw);
            return new DomainAvailabilityResult(
                candidate.Domain,
                candidate.Tld,
                DomainAvailability.Registered,
                server,
                null,
                RawWhois: JsonSerializer.Serialize(document.RootElement, JsonOptions),
                ExpirationDate: ExtractExpirationDate(document.RootElement),
                ProxyId: "rdap",
                ProxyElapsedMs: stopwatch.ElapsedMilliseconds,
                DispatchInfo: $"server {server}, protocol RDAP, {stopwatch.ElapsedMilliseconds}ms");
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or OperationCanceledException)
        {
            return null;
        }
    }

    private static Uri BuildDomainUri(string server, string domain)
    {
        var baseUri = server.EndsWith('/') ? new Uri(server) : new Uri(server + "/");
        return new Uri(baseUri, "domain/" + Uri.EscapeDataString(domain));
    }

    private static DateTimeOffset? ExtractExpirationDate(JsonElement root)
    {
        if (!root.TryGetProperty("events", out var events) || events.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        foreach (var item in events.EnumerateArray())
        {
            if (!item.TryGetProperty("eventAction", out var action) ||
                !string.Equals(action.GetString(), "expiration", StringComparison.OrdinalIgnoreCase) ||
                !item.TryGetProperty("eventDate", out var eventDate) ||
                !DateTimeOffset.TryParse(eventDate.GetString(), out var parsed))
            {
                continue;
            }

            return parsed.ToUniversalTime();
        }

        return null;
    }
}
