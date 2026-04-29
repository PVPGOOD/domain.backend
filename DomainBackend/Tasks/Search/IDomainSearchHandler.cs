using System.Collections.Generic;
using Domain.Backend.Api.Models.Requests;
using Domain.Backend.Tasks.Models.Enums;
using Domain.Backend.Tasks.Search.Models;

namespace Domain.Backend.Tasks.Search;

public interface IDomainSearchHandler
{
    SearchMode Mode { get; }

    IReadOnlyList<DomainCandidate> GenerateCandidates(CreateDomainSearchTaskRequest request);
}
