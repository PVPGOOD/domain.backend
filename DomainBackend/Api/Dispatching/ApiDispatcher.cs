using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using Domain.Backend.Api.Models;

namespace Domain.Backend.Api.Dispatching;

public sealed class ApiDispatcher(IApiDispatchConstraintProvider constraintProvider) : IApiDispatcher
{
    private readonly ConcurrentDictionary<ApiArea, SemaphoreSlim> _areaLimiters = new();

    public async Task<T> DispatchAsync<T>(
        ApiOperation operation,
        Func<CancellationToken, Task<T>> handler,
        CancellationToken cancellationToken)
    {
        var constraint = constraintProvider.GetConstraint(operation);
        var limiter = _areaLimiters.GetOrAdd(
            constraint.Area,
            _ => new SemaphoreSlim(constraint.MaxConcurrency, constraint.MaxConcurrency));

        using var timeoutCts = new CancellationTokenSource(constraint.QueueTimeout);
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

        try
        {
            await limiter.WaitAsync(linkedCts.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested && timeoutCts.IsCancellationRequested)
        {
            throw new ApiDispatchRejectedException($"API area {constraint.Area} is busy.");
        }

        try
        {
            return await handler(cancellationToken);
        }
        finally
        {
            limiter.Release();
        }
    }
}
