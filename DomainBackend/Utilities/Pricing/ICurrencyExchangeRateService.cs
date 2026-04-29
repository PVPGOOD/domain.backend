using System.Threading;
using System.Threading.Tasks;

namespace Domain.Backend.Utilities.Pricing;

public interface ICurrencyExchangeRateService
{
    Task<decimal?> GetCnyRateAsync(string currency, CancellationToken cancellationToken);
}
