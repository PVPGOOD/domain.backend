using System;
using System.Collections.Generic;
using Domain.Backend.Tasks.Models.Enums;

namespace Domain.Backend.Tasks.Models.Entities;

public sealed class DomainSearchTask
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public SearchMode Mode { get; set; }
    public DomainSearchTaskStatus Status { get; set; }
    public string RequestJson { get; set; } = string.Empty;
    public int TotalCount { get; set; }
    public int CompletedCount { get; set; }
    public int AvailableCount { get; set; }
    public int RegisteredCount { get; set; }
    public int UnknownCount { get; set; }
    public int ErrorCount { get; set; }
    public bool CancelRequested { get; set; }
    public DateTimeOffset? DeletedAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? StartedAt { get; set; }
    public DateTimeOffset? FinishedAt { get; set; }
    public string? ErrorMessage { get; set; }

    public List<DomainSearchResult> Results { get; set; } = [];
}
