using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Domain.Backend.Api.Dispatching;
using Domain.Backend.Api.Models;
using Domain.Backend.Api.Models.Requests;
using Domain.Backend.Api.Models.Responses;
using Domain.Backend.Utilities.Pricing;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Domain.Backend.Api.Controllers;

[ApiController]
[Route("api/v1/domain-search/prices")]
public sealed class DomainSearchPricesController(
    IDomainRegistrationPriceService priceService,
    IApiDispatcher apiDispatcher) : ControllerBase
{
    [HttpPost("registration/query")]
    public async Task<ActionResult<RegistrationPriceLookupResponse>> QueryRegistrationPrices(
        [FromBody] QueryRegistrationPricesRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var prices = await apiDispatcher.DispatchAsync(
                ApiOperation.QueryRegistrationPrices,
                token => priceService.QueryRegistrationPricesAsync(request.Tlds, token),
                cancellationToken);

            return Ok(new RegistrationPriceLookupResponse
            {
                Items = prices.Select(ToResponse).ToArray()
            });
        }
        catch (ApiDispatchRejectedException ex)
        {
            return StatusCode(StatusCodes.Status429TooManyRequests, new ErrorEnvelope(new ErrorBody("ApiBusy", ex.Message)));
        }
    }

    private static TldRegistrationPriceResponse ToResponse(TldRegistrationPrices price)
    {
        return new TldRegistrationPriceResponse
        {
            Tld = price.Tld,
            LowestRegistrationPrice = price.LowestRegistrationPrice,
            LowestRenewalPrice = price.LowestRenewalPrice,
            Currency = price.Currency,
            RegistrarName = price.RegistrarName,
            RegistrarWeb = price.RegistrarWeb,
            ErrorMessage = price.ErrorMessage,
            Prices = price.Prices.Select(item => new RegistrarRegistrationPriceResponse
            {
                Registrar = item.Registrar,
                RegistrarName = item.RegistrarName,
                RegistrarWeb = item.RegistrarWeb,
                RegistrationPrice = item.RegistrationPrice,
                RenewalPrice = item.RenewalPrice,
                TransferPrice = item.TransferPrice,
                Currency = item.Currency,
                HasRegistrationPromoCode = item.HasRegistrationPromoCode,
                UpdatedTime = item.UpdatedTime
            }).ToArray()
        };
    }
}
