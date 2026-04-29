namespace Domain.Backend.Tasks.Models.Enums;

public enum DomainSearchTaskStatus
{
    Pending = 1,
    Running = 2,
    Completed = 3,
    Failed = 4,
    Cancelled = 5,
    Deleting = 6,
    Deleted = 7
}
