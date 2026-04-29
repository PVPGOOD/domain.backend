using Domain.Backend.Api.Models;

namespace Domain.Backend.Api.Dispatching;

public interface IApiDispatchConstraintProvider
{
    ApiDispatchConstraint GetConstraint(ApiOperation operation);
}
