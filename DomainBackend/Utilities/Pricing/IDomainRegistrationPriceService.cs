using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Domain.Backend.Utilities.Pricing;

public interface IDomainRegistrationPriceService
{
    Task<IReadOnlyList<TldRegistrationPrices>> QueryRegistrationPricesAsync(
        IReadOnlyList<string> tlds,
        CancellationToken cancellationToken);
}

public sealed class TldRegistrationPrices
{
    public string Tld { get; init; } = string.Empty;
    public decimal? LowestRegistrationPrice { get; init; }
    public decimal? LowestRenewalPrice { get; init; }
    public string? Currency { get; init; }
    public string? RegistrarName { get; init; }
    public string? RegistrarWeb { get; init; }
    public string? ErrorMessage { get; init; }
    public IReadOnlyList<RegistrarRegistrationPrice> Prices { get; init; } = [];
}

public sealed class RegistrarRegistrationPrice
{
    public string Registrar { get; init; } = string.Empty;
    public string RegistrarName { get; init; } = string.Empty;
    public string? RegistrarWeb { get; init; }
    public decimal? RegistrationPrice { get; init; }
    public decimal? RenewalPrice { get; init; }
    public decimal? TransferPrice { get; init; }
    public string? Currency { get; init; }
    public bool HasRegistrationPromoCode { get; init; }
    public string? UpdatedTime { get; init; }
}
