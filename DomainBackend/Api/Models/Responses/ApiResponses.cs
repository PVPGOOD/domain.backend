using System;
using System.Collections.Generic;

namespace Domain.Backend.Api.Models.Responses;

public sealed record CreateTaskResponse(Guid TaskId, string Status, DateTimeOffset CreatedAt);

public sealed record TaskActionResponse(Guid TaskId, string Status);

public sealed class TaskProgressResponse
{
    public int TotalCount { get; set; }
    public int CompletedCount { get; set; }
    public int RemainingCount { get; set; }
    public decimal Percent { get; set; }
}

public sealed class TaskSummaryResponse
{
    public int AvailableCount { get; set; }
    public int RegisteredCount { get; set; }
    public int UnknownCount { get; set; }
    public int ErrorCount { get; set; }
}

public sealed class DomainSearchTaskResponse
{
    public Guid TaskId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Mode { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public TaskProgressResponse Progress { get; set; } = new();
    public TaskSummaryResponse Summary { get; set; } = new();
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? StartedAt { get; set; }
    public DateTimeOffset? FinishedAt { get; set; }
    public string? ErrorMessage { get; set; }
}

public sealed class DomainSearchTaskListItemResponse
{
    public Guid TaskId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Mode { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public int TotalCount { get; set; }
    public int CompletedCount { get; set; }
    public int RemainingCount { get; set; }
    public int AvailableCount { get; set; }
    public int RegisteredCount { get; set; }
    public int UnknownCount { get; set; }
    public int ErrorCount { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? StartedAt { get; set; }
    public DateTimeOffset? FinishedAt { get; set; }
}

public sealed class TaskProgressStreamResponse
{
    public Guid TaskId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Mode { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public int TotalCount { get; set; }
    public int CompletedCount { get; set; }
    public int RemainingCount { get; set; }
    public decimal Percent { get; set; }
    public int AvailableCount { get; set; }
    public int RegisteredCount { get; set; }
    public int UnknownCount { get; set; }
    public int ErrorCount { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? StartedAt { get; set; }
    public DateTimeOffset? FinishedAt { get; set; }
    public string? ErrorMessage { get; set; }
    public DateTimeOffset EmittedAt { get; set; }
}

public sealed class DomainSearchResultResponse
{
    public Guid Id { get; set; }
    public string Domain { get; set; } = string.Empty;
    public string? InputDomain { get; set; }
    public string Tld { get; set; } = string.Empty;
    public string Availability { get; set; } = string.Empty;
    public string? WhoisServer { get; set; }
    public DateTimeOffset CheckedAt { get; set; }
    public string? ErrorMessage { get; set; }
    public string? RawWhois { get; set; }
    public DateTimeOffset? ExpirationDate { get; set; }
    public string? ProxyId { get; set; }
    public long? ProxyElapsedMs { get; set; }
    public int? ProxyWorkerId { get; set; }
    public string? DispatchInfo { get; set; }
    public decimal? LowestRegistrationPrice { get; set; }
    public decimal? LowestRenewalPrice { get; set; }
    public string? RegistrationPriceCurrency { get; set; }
    public string? RegistrationPriceRegistrarName { get; set; }
    public string? RegistrationPriceRegistrarWeb { get; set; }
    public string? RegistrationPriceErrorMessage { get; set; }
    public IReadOnlyList<RegistrarRegistrationPriceResponse> RegistrationPrices { get; set; } = [];
}

public sealed class PagedResponse<T>
{
    public IReadOnlyList<T> Items { get; set; } = [];
    public int Page { get; set; }
    public int PageSize { get; set; }
    public int Total { get; set; }
}

public sealed class RegistrationPriceLookupResponse
{
    public IReadOnlyList<TldRegistrationPriceResponse> Items { get; set; } = [];
}

public sealed class TldRegistrationPriceResponse
{
    public string Tld { get; set; } = string.Empty;
    public decimal? LowestRegistrationPrice { get; set; }
    public decimal? LowestRenewalPrice { get; set; }
    public string? Currency { get; set; }
    public string? RegistrarName { get; set; }
    public string? RegistrarWeb { get; set; }
    public string? ErrorMessage { get; set; }
    public IReadOnlyList<RegistrarRegistrationPriceResponse> Prices { get; set; } = [];
}

public sealed class RegistrarRegistrationPriceResponse
{
    public string Registrar { get; set; } = string.Empty;
    public string RegistrarName { get; set; } = string.Empty;
    public string? RegistrarWeb { get; set; }
    public decimal? RegistrationPrice { get; set; }
    public decimal? RenewalPrice { get; set; }
    public decimal? TransferPrice { get; set; }
    public string? Currency { get; set; }
    public bool HasRegistrationPromoCode { get; set; }
    public string? UpdatedTime { get; set; }
}

public sealed record ErrorEnvelope(ErrorBody Error);

public sealed record ErrorBody(string Code, string Message, object? Details = null);
