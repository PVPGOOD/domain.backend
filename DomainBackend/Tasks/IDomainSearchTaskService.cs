using System;
using System.Threading;
using System.Threading.Tasks;
using Domain.Backend.Api.Models.Requests;
using Domain.Backend.Api.Models.Responses;

namespace Domain.Backend.Tasks;

public interface IDomainSearchTaskService
{
    Task<CreateTaskResponse> CreateAsync(CreateDomainSearchTaskRequest request, CancellationToken cancellationToken);
    Task<DomainSearchTaskResponse?> GetAsync(Guid taskId, CancellationToken cancellationToken);
    Task<PagedResponse<DomainSearchTaskListItemResponse>> QueryAsync(QueryTasksRequest request, CancellationToken cancellationToken);
    Task<PagedResponse<DomainSearchResultResponse>?> QueryResultsAsync(QueryTaskResultsRequest request, CancellationToken cancellationToken);
    Task<TaskActionResponse?> CancelAsync(Guid taskId, CancellationToken cancellationToken);
    Task<TaskActionResponse?> DeleteAsync(Guid taskId, CancellationToken cancellationToken);
}
