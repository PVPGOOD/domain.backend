using System;
using System.Threading;
using System.Threading.Tasks;
using Domain.Backend.Tasks.Models.Enums;
using Domain.Backend.Tasks.Search.Models;
using Domain.Backend.Tasks.Whois;
using Domain.Backend.Tasks.Whois.Models;
using Domain.Backend.Utilities.Proxy;
using Domain.Backend.Utilities.Proxy.Models;
using Domain.Backend.Utilities.Rdap;

namespace Domain.Backend.Utilities.Whois;

public sealed class WhoisDomainAvailabilityProvider(
    IWhoisServerResolver serverResolver,
    IOutboundTcpRequestDispatcher dispatcher,
    IWhoisResponseParser parser,
    IDomainRdapLookupService rdapLookup) : IDomainAvailabilityProvider
{
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(10);

    public async Task<DomainAvailabilityResult> CheckAsync(DomainCandidate candidate, CancellationToken cancellationToken)
    {
        var server = serverResolver.ResolveServer(candidate.Tld);
        if (server is null)
        {
            var rdapResult = await rdapLookup.TryLookupAsync(candidate, cancellationToken);
            if (rdapResult is not null)
            {
                return rdapResult;
            }

            return new DomainAvailabilityResult(
                candidate.Domain,
                candidate.Tld,
                DomainAvailability.UnsupportedTld,
                null,
                $"Unsupported TLD: {candidate.Tld}",
                DispatchInfo: $"No WHOIS server mapping for TLD {candidate.Tld}.");
        }

        var response = await dispatcher.SendAsync(new OutboundTcpRequest
        {
            Host = server,
            Port = 43,
            Payload = candidate.Domain + "\r\n",
            Timeout = DefaultTimeout,
            Purpose = "Whois43"
        }, cancellationToken);

        if (!response.Success)
        {
            var rdapFallbackResult = await rdapLookup.TryLookupAsync(candidate, cancellationToken);
            if (rdapFallbackResult is not null && rdapFallbackResult.Availability != DomainAvailability.Unknown)
            {
                return rdapFallbackResult;
            }

            return new DomainAvailabilityResult(
                candidate.Domain,
                candidate.Tld,
                DomainAvailability.Error,
                server,
                response.ErrorMessage ?? "WHOIS request failed.",
                RawWhois: response.ResponseText,
                ProxyId: response.ProxyId,
                ProxyElapsedMs: response.ElapsedMs,
                ProxyWorkerId: response.WorkerId,
                DispatchInfo: BuildDispatchInfo(server, response));
        }

        var rawWhois = response.ResponseText ?? string.Empty;
        var availability = parser.Parse(rawWhois);
        if (availability is DomainAvailability.Unknown or DomainAvailability.UnsupportedTld)
        {
            var rdapFallbackResult = await rdapLookup.TryLookupAsync(candidate, cancellationToken);
            if (rdapFallbackResult is not null && rdapFallbackResult.Availability != DomainAvailability.Unknown)
            {
                return rdapFallbackResult;
            }
        }

        return new DomainAvailabilityResult(
            candidate.Domain,
            candidate.Tld,
            availability,
            server,
            null,
            RawWhois: rawWhois,
            ExpirationDate: availability == DomainAvailability.Registered ? parser.ExtractExpirationDate(rawWhois) : null,
            ProxyId: response.ProxyId,
            ProxyElapsedMs: response.ElapsedMs,
            ProxyWorkerId: response.WorkerId,
            DispatchInfo: BuildDispatchInfo(server, response));
    }

    private static string BuildDispatchInfo(string server, OutboundTcpResponse response)
    {
        var worker = response.WorkerId.HasValue ? $", worker #{response.WorkerId}" : string.Empty;
        var elapsed = response.ElapsedMs.HasValue ? $", {response.ElapsedMs}ms" : string.Empty;
        var proxy = string.IsNullOrWhiteSpace(response.ProxyId) ? "unknown proxy" : response.ProxyId;

        return $"server {server}, proxy {proxy}{worker}{elapsed}";
    }
}
