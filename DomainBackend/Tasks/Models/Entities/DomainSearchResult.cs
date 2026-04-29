using System;
using Domain.Backend.Tasks.Models.Enums;

namespace Domain.Backend.Tasks.Models.Entities;

public sealed class DomainSearchResult
{
    public Guid Id { get; set; }
    public Guid TaskId { get; set; }
    public string Domain { get; set; } = string.Empty;
    public string? InputDomain { get; set; }
    public string Tld { get; set; } = string.Empty;
    public DomainAvailability Availability { get; set; }
    public string? WhoisServer { get; set; }
    public string? ErrorMessage { get; set; }
    public string? RawWhois { get; set; }
    public DateTimeOffset? ExpirationDate { get; set; }
    public string? ProxyId { get; set; }
    public long? ProxyElapsedMs { get; set; }
    public int? ProxyWorkerId { get; set; }
    public string? DispatchInfo { get; set; }
    public string? RegistrationPriceSnapshotJson { get; set; }
    public DateTimeOffset? RegistrationPriceSnapshotAt { get; set; }
    public DateTimeOffset CheckedAt { get; set; }

    public DomainSearchTask? Task { get; set; }
}
