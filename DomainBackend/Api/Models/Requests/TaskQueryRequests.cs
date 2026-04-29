using System;
using Domain.Backend.Tasks.Models.Enums;

namespace Domain.Backend.Api.Models.Requests;

public sealed class TaskIdRequest
{
    public Guid TaskId { get; set; }
}

public sealed class QueryTasksRequest
{
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 20;
    public DomainSearchTaskStatus? Status { get; set; }
}

public sealed class QueryTaskResultsRequest
{
    public Guid TaskId { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 100;
    public DomainAvailability? Availability { get; set; }
}

public sealed class StreamTaskProgressRequest
{
    public Guid TaskId { get; set; }
    public int IntervalMs { get; set; } = 1000;
}
