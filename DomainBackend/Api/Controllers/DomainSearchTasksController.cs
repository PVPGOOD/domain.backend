using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Domain.Backend.Api.Dispatching;
using Domain.Backend.Api.Models;
using Domain.Backend.Api.Models.Requests;
using Domain.Backend.Api.Models.Responses;
using Domain.Backend.Tasks;
using Domain.Backend.Utilities.DomainNames;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Domain.Backend.Api.Controllers;

[ApiController]
[Route("api/v1/domain-search/tasks")]
public sealed class DomainSearchTasksController(
    IDomainSearchTaskService taskService,
    IApiDispatcher apiDispatcher) : ControllerBase
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly HashSet<string> TerminalStatuses =
    [
        "Completed",
        "Failed",
        "Cancelled",
        "Deleted"
    ];

    [HttpPost]
    public async Task<ActionResult<CreateTaskResponse>> Create(
        [FromBody] CreateDomainSearchTaskRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var response = await apiDispatcher.DispatchAsync(
                ApiOperation.CreateTask,
                token => taskService.CreateAsync(request, token),
                cancellationToken);

            return Ok(response);
        }
        catch (DomainSearchValidationException ex)
        {
            return BadRequest(Error("InvalidRequest", ex.Message, ex.Field is null ? null : new { field = ex.Field }));
        }
        catch (ApiDispatchRejectedException ex)
        {
            return StatusCode(StatusCodes.Status429TooManyRequests, Error("ApiBusy", ex.Message));
        }
    }

    [HttpPost("query")]
    public async Task<ActionResult<PagedResponse<DomainSearchTaskListItemResponse>>> Query(
        [FromBody] QueryTasksRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var response = await apiDispatcher.DispatchAsync(
                ApiOperation.QueryTasks,
                token => taskService.QueryAsync(request, token),
                cancellationToken);

            return Ok(response);
        }
        catch (ApiDispatchRejectedException ex)
        {
            return StatusCode(StatusCodes.Status429TooManyRequests, Error("ApiBusy", ex.Message));
        }
    }

    [HttpPost("get")]
    public async Task<ActionResult<DomainSearchTaskResponse>> Get(
        [FromBody] TaskIdRequest request,
        CancellationToken cancellationToken)
    {
        DomainSearchTaskResponse? task;
        try
        {
            task = await apiDispatcher.DispatchAsync(
                ApiOperation.GetTask,
                token => taskService.GetAsync(request.TaskId, token),
                cancellationToken);
        }
        catch (ApiDispatchRejectedException ex)
        {
            return StatusCode(StatusCodes.Status429TooManyRequests, Error("ApiBusy", ex.Message));
        }

        return task is null ? NotFound(Error("TaskNotFound", "Task was not found.")) : Ok(task);
    }

    [HttpPost("results/query")]
    public async Task<ActionResult<PagedResponse<DomainSearchResultResponse>>> QueryResults(
        [FromBody] QueryTaskResultsRequest request,
        CancellationToken cancellationToken)
    {
        PagedResponse<DomainSearchResultResponse>? results;
        try
        {
            results = await apiDispatcher.DispatchAsync(
                ApiOperation.QueryResults,
                token => taskService.QueryResultsAsync(request, token),
                cancellationToken);
        }
        catch (ApiDispatchRejectedException ex)
        {
            return StatusCode(StatusCodes.Status429TooManyRequests, Error("ApiBusy", ex.Message));
        }

        return results is null ? NotFound(Error("TaskNotFound", "Task was not found.")) : Ok(results);
    }

    [HttpPost("stream")]
    public async Task StreamProgress(
        [FromBody] StreamTaskProgressRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            await apiDispatcher.DispatchAsync(
                ApiOperation.StreamTaskProgress,
                async token =>
                {
                    await StreamProgressCoreAsync(request, token);
                    return true;
                },
                cancellationToken);
        }
        catch (ApiDispatchRejectedException ex)
        {
            if (!Response.HasStarted)
            {
                Response.StatusCode = StatusCodes.Status429TooManyRequests;
                await Response.WriteAsJsonAsync(Error("ApiBusy", ex.Message), cancellationToken);
            }
        }
    }

    [HttpPost("cancel")]
    public async Task<ActionResult<TaskActionResponse>> Cancel(
        [FromBody] TaskIdRequest request,
        CancellationToken cancellationToken)
    {
        TaskActionResponse? response;
        try
        {
            response = await apiDispatcher.DispatchAsync(
                ApiOperation.CancelTask,
                token => taskService.CancelAsync(request.TaskId, token),
                cancellationToken);
        }
        catch (ApiDispatchRejectedException ex)
        {
            return StatusCode(StatusCodes.Status429TooManyRequests, Error("ApiBusy", ex.Message));
        }

        return response is null ? NotFound(Error("TaskNotFound", "Task was not found.")) : Ok(response);
    }

    [HttpPost("delete")]
    public async Task<ActionResult<TaskActionResponse>> Delete(
        [FromBody] TaskIdRequest request,
        CancellationToken cancellationToken)
    {
        TaskActionResponse? response;
        try
        {
            response = await apiDispatcher.DispatchAsync(
                ApiOperation.DeleteTask,
                token => taskService.DeleteAsync(request.TaskId, token),
                cancellationToken);
        }
        catch (ApiDispatchRejectedException ex)
        {
            return StatusCode(StatusCodes.Status429TooManyRequests, Error("ApiBusy", ex.Message));
        }

        return response is null ? NotFound(Error("TaskNotFound", "Task was not found.")) : Ok(response);
    }

    private static ErrorEnvelope Error(string code, string message, object? details = null)
    {
        return new ErrorEnvelope(new ErrorBody(code, message, details));
    }

    private async Task StreamProgressCoreAsync(StreamTaskProgressRequest request, CancellationToken cancellationToken)
    {
        var interval = Math.Clamp(request.IntervalMs, 250, 5000);
        var task = await taskService.GetAsync(request.TaskId, cancellationToken);
        if (task is null)
        {
            Response.StatusCode = StatusCodes.Status404NotFound;
            await Response.WriteAsJsonAsync(Error("TaskNotFound", "Task was not found."), cancellationToken);
            return;
        }

        Response.StatusCode = StatusCodes.Status200OK;
        Response.ContentType = "application/x-ndjson; charset=utf-8";
        Response.Headers.CacheControl = "no-cache";
        Response.Headers.Append("X-Accel-Buffering", "no");

        while (!cancellationToken.IsCancellationRequested)
        {
            task = await taskService.GetAsync(request.TaskId, cancellationToken);
            if (task is null)
            {
                await WriteNdjsonAsync(new
                {
                    error = new
                    {
                        code = "TaskNotFound",
                        message = "Task was not found."
                    }
                }, cancellationToken);
                return;
            }

            await WriteNdjsonAsync(ToStreamResponse(task), cancellationToken);
            if (TerminalStatuses.Contains(task.Status))
            {
                return;
            }

            await Task.Delay(interval, cancellationToken);
        }
    }

    private async Task WriteNdjsonAsync<T>(T value, CancellationToken cancellationToken)
    {
        await Response.WriteAsync(JsonSerializer.Serialize(value, JsonOptions), cancellationToken);
        await Response.WriteAsync("\n", cancellationToken);
        await Response.Body.FlushAsync(cancellationToken);
    }

    private static TaskProgressStreamResponse ToStreamResponse(DomainSearchTaskResponse task)
    {
        return new TaskProgressStreamResponse
        {
            TaskId = task.TaskId,
            Name = task.Name,
            Mode = task.Mode,
            Status = task.Status,
            TotalCount = task.Progress.TotalCount,
            CompletedCount = task.Progress.CompletedCount,
            RemainingCount = task.Progress.RemainingCount,
            Percent = task.Progress.Percent,
            AvailableCount = task.Summary.AvailableCount,
            RegisteredCount = task.Summary.RegisteredCount,
            UnknownCount = task.Summary.UnknownCount,
            ErrorCount = task.Summary.ErrorCount,
            CreatedAt = task.CreatedAt,
            StartedAt = task.StartedAt,
            FinishedAt = task.FinishedAt,
            ErrorMessage = task.ErrorMessage,
            EmittedAt = DateTimeOffset.UtcNow
        };
    }
}
