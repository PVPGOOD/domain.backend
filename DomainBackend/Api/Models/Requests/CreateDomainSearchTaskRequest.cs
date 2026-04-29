using System.Text.Json;
using Domain.Backend.Tasks.Models.Enums;

namespace Domain.Backend.Api.Models.Requests;

public sealed class CreateDomainSearchTaskRequest
{
    public string? Name { get; set; }
    public SearchMode Mode { get; set; }
    public JsonElement Payload { get; set; }
    public DomainSearchOptionsRequest? Options { get; set; }
}

public sealed class DomainSearchOptionsRequest
{
    public string[]? Tlds { get; set; }
    public int? MaxCandidates { get; set; }
    public int? TimeoutSeconds { get; set; }
}
