using System;
using System.Collections.Generic;
using Domain.Backend.Api.Models;

namespace Domain.Backend.Api.Dispatching;

public sealed class DefaultApiDispatchConstraintProvider : IApiDispatchConstraintProvider
{
    private static readonly IReadOnlyDictionary<ApiOperation, ApiDispatchConstraint> Constraints =
        new Dictionary<ApiOperation, ApiDispatchConstraint>
        {
            [ApiOperation.CreateTask] = new()
            {
                Area = ApiArea.TaskCreation,
                MaxConcurrency = 4,
                QueueTimeout = TimeSpan.FromSeconds(2)
            },
            [ApiOperation.QueryTasks] = new()
            {
                Area = ApiArea.TaskRead,
                MaxConcurrency = 32,
                QueueTimeout = TimeSpan.FromSeconds(1)
            },
            [ApiOperation.GetTask] = new()
            {
                Area = ApiArea.TaskRead,
                MaxConcurrency = 32,
                QueueTimeout = TimeSpan.FromSeconds(1)
            },
            [ApiOperation.QueryResults] = new()
            {
                Area = ApiArea.ResultRead,
                MaxConcurrency = 16,
                QueueTimeout = TimeSpan.FromSeconds(1)
            },
            [ApiOperation.CancelTask] = new()
            {
                Area = ApiArea.TaskMutation,
                MaxConcurrency = 8,
                QueueTimeout = TimeSpan.FromSeconds(1)
            },
            [ApiOperation.DeleteTask] = new()
            {
                Area = ApiArea.TaskMutation,
                MaxConcurrency = 8,
                QueueTimeout = TimeSpan.FromSeconds(1)
            },
            [ApiOperation.StreamTaskProgress] = new()
            {
                Area = ApiArea.TaskStream,
                MaxConcurrency = 16,
                QueueTimeout = TimeSpan.FromSeconds(1)
            },
            [ApiOperation.QueryRegistrationPrices] = new()
            {
                Area = ApiArea.PriceRead,
                MaxConcurrency = 8,
                QueueTimeout = TimeSpan.FromSeconds(2)
            }
        };

    public ApiDispatchConstraint GetConstraint(ApiOperation operation)
    {
        return Constraints.TryGetValue(operation, out var constraint)
            ? constraint
            : new ApiDispatchConstraint
            {
                Area = ApiArea.TaskRead,
                MaxConcurrency = 8,
                QueueTimeout = TimeSpan.FromSeconds(1)
            };
    }
}
