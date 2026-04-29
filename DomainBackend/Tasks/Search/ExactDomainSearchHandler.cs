using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Domain.Backend.Api.Models.Requests;
using Domain.Backend.Tasks.Models.Enums;
using Domain.Backend.Tasks.Search.Models;
using Domain.Backend.Utilities.DomainNames;

namespace Domain.Backend.Tasks.Search;

public sealed class ExactDomainSearchHandler(IDomainNameNormalizer normalizer) : IDomainSearchHandler
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public SearchMode Mode => SearchMode.Exact;

    public IReadOnlyList<DomainCandidate> GenerateCandidates(CreateDomainSearchTaskRequest request)
    {
        var payload = request.Payload.Deserialize<ExactPayload>(JsonOptions)
            ?? throw new DomainSearchValidationException("Exact payload is required.", "payload");

        if (payload.Domains is not { Length: > 0 })
        {
            throw new DomainSearchValidationException("At least one domain is required.", "payload.domains");
        }

        var candidates = new Dictionary<string, DomainCandidate>(StringComparer.OrdinalIgnoreCase);
        foreach (var input in payload.Domains)
        {
            var normalized = normalizer.NormalizeExactInput(input);
            candidates.TryAdd(normalized.Domain, new DomainCandidate(normalized.Domain, input, normalized.Tld));
        }

        return candidates.Values.ToArray();
    }
}
