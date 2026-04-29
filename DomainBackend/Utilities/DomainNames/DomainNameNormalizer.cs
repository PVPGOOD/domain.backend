using System;
using System.Globalization;
using System.Linq;

namespace Domain.Backend.Utilities.DomainNames;

public sealed class DomainNameNormalizer : IDomainNameNormalizer
{
    private static readonly string[] CompoundSuffixes =
    [
        "com.cn",
        "net.cn",
        "org.cn",
        "gov.cn",
        "co.uk",
        "org.uk",
        "com.au",
        "net.au"
    ];

    private readonly IdnMapping _idnMapping = new();

    public NormalizedDomain NormalizeExactInput(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            throw new DomainSearchValidationException("Domain cannot be empty.", "payload.domains");
        }

        var value = input.Trim().TrimEnd('.').ToLowerInvariant();
        if (value.Contains("://", StringComparison.Ordinal))
        {
            if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || string.IsNullOrWhiteSpace(uri.Host))
            {
                throw new DomainSearchValidationException("Domain input is invalid.", "payload.domains");
            }

            value = uri.Host.TrimEnd('.').ToLowerInvariant();
        }

        var ascii = _idnMapping.GetAscii(value);
        var labels = ascii.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (labels.Length < 2)
        {
            throw new DomainSearchValidationException("Domain must include a registrable label and TLD.", "payload.domains");
        }

        var suffix = ResolveSuffix(labels);
        var suffixLabelCount = suffix.Count(c => c == '.') + 1;
        if (labels.Length <= suffixLabelCount)
        {
            throw new DomainSearchValidationException("Domain must include a registrable label before the TLD.", "payload.domains");
        }

        var registrableLabel = labels[^ (suffixLabelCount + 1)];
        var domain = string.Join('.', [registrableLabel, suffix]);
        return new NormalizedDomain(domain, suffix);
    }

    public string NormalizeTld(string tld)
    {
        if (string.IsNullOrWhiteSpace(tld))
        {
            throw new DomainSearchValidationException("TLD cannot be empty.", "options.tlds");
        }

        return _idnMapping.GetAscii(tld.Trim().TrimStart('.').TrimEnd('.').ToLowerInvariant());
    }

    private static string ResolveSuffix(string[] labels)
    {
        var domain = string.Join('.', labels);
        foreach (var suffix in CompoundSuffixes.OrderByDescending(s => s.Length))
        {
            if (domain.EndsWith($".{suffix}", StringComparison.Ordinal) || domain == suffix)
            {
                return suffix;
            }
        }

        return labels[^1];
    }
}
