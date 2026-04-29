using System;

namespace Domain.Backend.Api.Models;

public sealed class ApiDispatchConstraint
{
    public ApiArea Area { get; init; }
    public int MaxConcurrency { get; init; }
    public TimeSpan QueueTimeout { get; init; }
}
