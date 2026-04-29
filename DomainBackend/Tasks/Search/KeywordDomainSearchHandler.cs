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

public sealed partial class KeywordDomainSearchHandler(IDomainNameNormalizer normalizer) : IDomainSearchHandler
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public SearchMode Mode => SearchMode.Keyword;

    public IReadOnlyList<DomainCandidate> GenerateCandidates(CreateDomainSearchTaskRequest request)
    {
        var payload = request.Payload.Deserialize<KeywordPayload>(JsonOptions)
            ?? throw new DomainSearchValidationException("Keyword payload is required.", "payload");

        if (payload.Keywords is not { Length: > 0 })
        {
            throw new DomainSearchValidationException("At least one keyword is required.", "payload.keywords");
        }

        var tlds = NormalizeTlds(request);
        var keywords = payload.Keywords
            .Select(keyword => keyword.Trim())
            .Select(ValidateKeyword)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        return keywords
            .SelectMany(keyword => tlds.Select(tld => new DomainCandidate($"{keyword}.{tld}", null, tld)))
            .ToArray();
    }

    private string[] NormalizeTlds(CreateDomainSearchTaskRequest request)
    {
        if (request.Options?.Tlds is not { Length: > 0 })
        {
            throw new DomainSearchValidationException("At least one TLD is required.", "options.tlds");
        }

        return request.Options.Tlds
            .Select(normalizer.NormalizeTld)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
    }

    private static string ValidateKeyword(string keyword)
    {
        if (!KeywordRegex().IsMatch(keyword))
        {
            throw new DomainSearchValidationException("Keyword can only contain a-z or A-Z.", "payload.keywords");
        }

        return keyword.ToLowerInvariant();
    }

    [GeneratedRegex("^[a-zA-Z]+$")]
    private static partial Regex KeywordRegex();
}
