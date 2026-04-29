using System;
using Domain.Backend.Tasks.Models.Enums;

namespace Domain.Backend.Tasks.Whois.Models;

public sealed record DomainAvailabilityResult(
    string Domain,
    string Tld,
    DomainAvailability Availability,
    string? WhoisServer,
    string? ErrorMessage,
    string? RawWhois = null,
    DateTimeOffset? ExpirationDate = null,
    string? ProxyId = null,
    long? ProxyElapsedMs = null,
    int? ProxyWorkerId = null,
    string? DispatchInfo = null);
