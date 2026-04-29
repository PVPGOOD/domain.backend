using System;
using System.Threading;
using System.Threading.Tasks;
using Domain.Backend.Api.Models;

namespace Domain.Backend.Api.Dispatching;

public interface IApiDispatcher
{
    Task<T> DispatchAsync<T>(
        ApiOperation operation,
        Func<CancellationToken, Task<T>> handler,
        CancellationToken cancellationToken);
}
