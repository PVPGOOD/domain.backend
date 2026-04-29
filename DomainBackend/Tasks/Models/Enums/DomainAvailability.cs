namespace Domain.Backend.Tasks.Models.Enums;

public enum DomainAvailability
{
    Available = 1,
    Registered = 2,
    Unknown = 3,
    Error = 4,
    RateLimited = 5,
    UnsupportedTld = 6,
    Reserved = 7
}
