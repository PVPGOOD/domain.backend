namespace Domain.Backend.Api.Models;

public enum ApiOperation
{
    CreateTask = 1,
    QueryTasks = 2,
    GetTask = 3,
    QueryResults = 4,
    CancelTask = 5,
    DeleteTask = 6,
    StreamTaskProgress = 7,
    QueryRegistrationPrices = 8
}
