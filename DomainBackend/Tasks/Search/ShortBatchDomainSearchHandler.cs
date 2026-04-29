using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using Domain.Backend.Api.Models.Requests;
using Domain.Backend.Tasks.Models.Enums;
using Domain.Backend.Tasks.Search.Models;
using Domain.Backend.Utilities.DomainNames;

namespace Domain.Backend.Tasks.Search;

public sealed partial class ShortBatchDomainSearchHandler(IDomainNameNormalizer normalizer) : IDomainSearchHandler
{
    private const int MinLength = 1;
    private const int MaxLength = 5;
    private const int DefaultMaxCandidates = 10_000;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public SearchMode Mode => SearchMode.ShortBatch;

    public IReadOnlyList<DomainCandidate> GenerateCandidates(CreateDomainSearchTaskRequest request)
    {
        var payload = request.Payload.Deserialize<ShortBatchPayload>(JsonOptions)
            ?? throw new DomainSearchValidationException("ShortBatch payload is required.", "payload");

        if (payload.Length is < MinLength or > MaxLength)
        {
            throw new DomainSearchValidationException("ShortBatch length must be between 1 and 5.", "payload.length");
        }

        if (string.IsNullOrWhiteSpace(payload.Charset) || !CharsetRegex().IsMatch(payload.Charset))
        {
            throw new DomainSearchValidationException("Charset can only contain a-z, A-Z, or 0-9.", "payload.charset");
        }

        if (request.Options?.Tlds is not { Length: > 0 })
        {
            throw new DomainSearchValidationException("At least one TLD is required.", "options.tlds");
        }

        var chars = payload.Charset
            .ToLowerInvariant()
            .Distinct()
            .Order()
            .ToArray();
        var tlds = request.Options.Tlds
            .Select(normalizer.NormalizeTld)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var candidateCount = checked((int)Math.Pow(chars.Length, payload.Length) * tlds.Length);
        var maxCandidates = request.Options.MaxCandidates ?? DefaultMaxCandidates;
        if (candidateCount > maxCandidates)
        {
            throw new DomainSearchValidationException($"Candidate count {candidateCount} exceeds maxCandidates {maxCandidates}.", "options.maxCandidates");
        }

        var labels = new List<string>(candidateCount / Math.Max(1, tlds.Length));
        GenerateLabels(chars, payload.Length, string.Empty, labels);

        return labels
            .SelectMany(label => tlds.Select(tld => new DomainCandidate($"{label}.{tld}", null, tld)))
            .ToArray();
    }

    private static void GenerateLabels(char[] chars, int length, string prefix, List<string> labels)
    {
        if (prefix.Length == length)
        {
            labels.Add(prefix);
            return;
        }

        foreach (var c in chars)
        {
            GenerateLabels(chars, length, prefix + c, labels);
        }
    }

    [GeneratedRegex("^[a-zA-Z0-9]+$")]
    private static partial Regex CharsetRegex();
}
