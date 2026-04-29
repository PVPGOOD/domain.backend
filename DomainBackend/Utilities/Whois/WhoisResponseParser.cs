using System;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using Domain.Backend.Tasks.Models.Enums;
using Domain.Backend.Tasks.Whois;

namespace Domain.Backend.Utilities.Whois;

public sealed class WhoisResponseParser : IWhoisResponseParser
{
    private static readonly string[] AvailableSignals =
    [
        "no match for",
        "not found",
        "no data found",
        "no entries found",
        "no match found",
        "no match",
        "no information was found matching that query",
        "the queried object does not exist",
        "object does not exist",
        "no object found",
        "domain is not registered",
        "domain has not been registered",
        "el dominio no se encuentra registrado",
        "no_se_encontro_el_objeto/object_not_found",
        "no such domain",
        "no found",
        "no information available about domain name",
        "available for registration",
        "available for registration.",
        "domain is available",
        "status: available",
        "is available for purchase",
        "available for purchase",
        "status: free",
        "is free"
    ];

    private static readonly string[] UnsupportedSignals =
    [
        "access will only be enabled for  ip addresses  authorised  by red.es",
        "the ip address used to perform the query  is not authorised",
        "to request access to the service,complete the form located at https://sede.red.gob.es/sede/whois",
        "is not valid!"
    ];

    private static readonly string[] ReservedSignals =
    [
        "reserved domain name"
    ];

    private static readonly string[] RegisteredSignals =
    [
        "domain:",
        "domainname:",
        "domain name:",
        "domain name                 :",
        "domain  name:",
        "domain name.........:",
        "domain state:   ok",
        "domaintype:     active",
        "domain status.......: active",
        "this domain is currently not available for registration",
        "[domain name]",
        "registrar:",
        "registrant:",
        "created:",
        "creation date:",
        "created on:",
        "registry domain id:",
        "registration status: busy",
        "status...............: registered",
        "status.............: registered",
        "[状態]                          active",
        "status: active",
        "status: connect"
    ];

    private static readonly string[] RateLimitSignals =
    [
        "limit exceeded",
        "rate limit exceeded",
        "rate-limit exceeded",
        "too many requests",
        "quota exceeded",
        "query rate limit",
        "access denied"
    ];

    public DomainAvailability Parse(string responseText)
    {
        if (string.IsNullOrWhiteSpace(responseText))
        {
            return DomainAvailability.Unknown;
        }

        var text = responseText.ToLowerInvariant();
        if (RateLimitSignals.Any(text.Contains))
        {
            return DomainAvailability.RateLimited;
        }

        if (UnsupportedSignals.Any(text.Contains))
        {
            return DomainAvailability.UnsupportedTld;
        }

        if (ReservedSignals.Any(text.Contains))
        {
            return DomainAvailability.Reserved;
        }

        if (AvailableSignals.Any(text.Contains))
        {
            return DomainAvailability.Available;
        }

        if (RegisteredSignals.Any(text.Contains))
        {
            return DomainAvailability.Registered;
        }

        return DomainAvailability.Unknown;
    }

    public DateTimeOffset? ExtractExpirationDate(string responseText)
    {
        if (string.IsNullOrWhiteSpace(responseText))
        {
            return null;
        }

        var patterns = new[]
        {
            @"(?im)^\s*(?:Registry Expiry Date|Registrar Registration Expiration Date|Expiration Date|Expiry Date|paid-till|expire)\s*:\s*(?<value>.+?)\s*$",
            @"(?im)^\s*(?:Expiry|Expire Date|Expiration Time|Renewal Date|Valid Until)\s*:\s*(?<value>.+?)\s*$",
            @"(?im)^\s*Expires on\s*:\s*(?<value>.+?)\s*$",
            @"(?im)^\s*expires\.+\s*:\s*(?<value>.+?)\s*$",
            @"(?im)^\s*\[有効期限\]\s*(?<value>.+?)\s*$",
            @"(?im)^\s*expires\s*:\s*(?<value>.+?)\s*$"
        };

        foreach (var pattern in patterns)
        {
            var match = Regex.Match(responseText, pattern);
            if (!match.Success)
            {
                continue;
            }

            var value = match.Groups["value"].Value.Trim();
            if (DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed))
            {
                return parsed.ToUniversalTime();
            }

            if (DateTimeOffset.TryParseExact(
                    value,
                    ["dd.MM.yyyy", "yyyy. MM. dd.", "yyyy. M. d.", "yyyy. MM. d.", "yyyy. M. dd.", "d.M.yyyy HH:mm:ss", "dd.M.yyyy HH:mm:ss", "d.MM.yyyy HH:mm:ss", "dd.MM.yyyy HH:mm:ss", "yyyy/MM/dd"],
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal,
                    out parsed))
            {
                return parsed.ToUniversalTime();
            }

            var normalized = value.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
            if (normalized is not null && DateTimeOffset.TryParse(normalized, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out parsed))
            {
                return parsed.ToUniversalTime();
            }

            if (normalized is not null && DateTimeOffset.TryParseExact(
                    normalized,
                    ["dd.MM.yyyy", "yyyy. MM. dd.", "yyyy. M. d.", "yyyy. MM. d.", "yyyy. M. dd.", "d.M.yyyy", "dd.M.yyyy", "d.MM.yyyy", "dd.MM.yyyy", "yyyy/MM/dd"],
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal,
                    out parsed))
            {
                return parsed.ToUniversalTime();
            }
        }

        return null;
    }
}
