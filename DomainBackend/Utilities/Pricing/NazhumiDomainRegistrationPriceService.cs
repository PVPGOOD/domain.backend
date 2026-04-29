using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Domain.Backend.Utilities.Pricing;

namespace Domain.Backend.Utilities.Pricing;

public sealed class NazhumiDomainRegistrationPriceService(
    HttpClient httpClient,
    ICurrencyExchangeRateService exchangeRateService) : IDomainRegistrationPriceService
{
    private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(30);
    private static readonly TimeSpan ErrorCacheDuration = TimeSpan.FromMinutes(2);
    private readonly ConcurrentDictionary<string, CacheEntry> cache = new(StringComparer.OrdinalIgnoreCase);

    public async Task<IReadOnlyList<TldRegistrationPrices>> QueryRegistrationPricesAsync(
        IReadOnlyList<string> tlds,
        CancellationToken cancellationToken)
    {
        var normalizedTlds = tlds
            .Select(NormalizeTld)
            .Where(static tld => !string.IsNullOrWhiteSpace(tld))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(100)
            .ToArray();

        var tasks = normalizedTlds.Select(tld => GetPriceAsync(tld, cancellationToken));
        return await Task.WhenAll(tasks);
    }

    private async Task<TldRegistrationPrices> GetPriceAsync(string tld, CancellationToken cancellationToken)
    {
        if (cache.TryGetValue(tld, out var cached) && cached.ExpiresAt > DateTimeOffset.UtcNow)
        {
            return cached.Value;
        }

        var value = await FetchPriceAsync(tld, cancellationToken);
        var cacheDuration = HasUsablePrices(value) ? CacheDuration : ErrorCacheDuration;
        cache[tld] = new CacheEntry(value, DateTimeOffset.UtcNow.Add(cacheDuration));
        return value;
    }

    private async Task<TldRegistrationPrices> FetchPriceAsync(string tld, CancellationToken cancellationToken)
    {
        try
        {
            using var response = await httpClient.GetAsync(
                $"api/v1?domain={Uri.EscapeDataString(tld)}&order=new",
                cancellationToken);

            response.EnsureSuccessStatusCode();
            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);

            var root = document.RootElement;
            if (!TryGetPriceArray(root, out var priceElement))
            {
                return new TldRegistrationPrices
                {
                    Tld = tld,
                    ErrorMessage = GetApiMessage(root) ?? "价格 API 返回格式不完整"
                };
            }

            var rawPrices = priceElement.EnumerateArray()
                .Select(ParsePrice)
                .Where(static price => price.RegistrationPrice is not null)
                .ToArray();
            var prices = await ConvertPricesToCnyAsync(rawPrices, cancellationToken);
            prices = prices
                .OrderBy(static price => price.RegistrationPrice)
                .ThenBy(static price => price.RegistrarName, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            var lowest = prices.FirstOrDefault();
            var lowestRenewal = prices
                .Where(static price => price.RenewalPrice is not null)
                .OrderBy(static price => price.RenewalPrice)
                .ThenBy(static price => price.RegistrarName, StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault();

            return new TldRegistrationPrices
            {
                Tld = tld,
                LowestRegistrationPrice = lowest?.RegistrationPrice,
                LowestRenewalPrice = lowestRenewal?.RenewalPrice,
                Currency = lowest?.Currency,
                RegistrarName = lowest?.RegistrarName,
                RegistrarWeb = lowest?.RegistrarWeb,
                Prices = prices
            };
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or TaskCanceledException)
        {
            return new TldRegistrationPrices
            {
                Tld = tld,
                ErrorMessage = ex.Message
            };
        }
    }

    private static RegistrarRegistrationPrice ParsePrice(JsonElement item)
    {
        return new RegistrarRegistrationPrice
        {
            Registrar = GetString(item, "registrar") ?? string.Empty,
            RegistrarName = GetString(item, "registrarname") ?? string.Empty,
            RegistrarWeb = GetString(item, "registrarweb"),
            RegistrationPrice = GetDecimal(item, "new"),
            RenewalPrice = GetDecimal(item, "renew"),
            TransferPrice = GetDecimal(item, "transfer"),
            Currency = GetString(item, "currency")?.ToUpperInvariant(),
            HasRegistrationPromoCode = GetRegistrationPromoCode(item),
            UpdatedTime = GetString(item, "updatedtime")
        };
    }

    private async Task<RegistrarRegistrationPrice[]> ConvertPricesToCnyAsync(
        IReadOnlyList<RegistrarRegistrationPrice> prices,
        CancellationToken cancellationToken)
    {
        var currencies = prices
            .Select(static price => price.Currency)
            .Where(static currency => !string.IsNullOrWhiteSpace(currency))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var rates = new Dictionary<string, decimal?>(StringComparer.OrdinalIgnoreCase);

        foreach (var currency in currencies)
        {
            rates[currency!] = await exchangeRateService.GetCnyRateAsync(currency!, cancellationToken);
        }

        return prices.Select(price => ConvertPriceToCny(price, rates)).ToArray();
    }

    private static RegistrarRegistrationPrice ConvertPriceToCny(
        RegistrarRegistrationPrice price,
        IReadOnlyDictionary<string, decimal?> rates)
    {
        var currency = price.Currency;
        if (string.IsNullOrWhiteSpace(currency)) return price;
        if (!rates.TryGetValue(currency, out var rate) || rate is null) return price;

        return new RegistrarRegistrationPrice
        {
            Registrar = price.Registrar,
            RegistrarName = price.RegistrarName,
            RegistrarWeb = price.RegistrarWeb,
            RegistrationPrice = ConvertAmount(price.RegistrationPrice, rate.Value),
            RenewalPrice = ConvertAmount(price.RenewalPrice, rate.Value),
            TransferPrice = ConvertAmount(price.TransferPrice, rate.Value),
            Currency = "CNY",
            HasRegistrationPromoCode = price.HasRegistrationPromoCode,
            UpdatedTime = price.UpdatedTime
        };
    }

    private static decimal? ConvertAmount(decimal? amount, decimal rate)
    {
        return amount is null ? null : Math.Round(amount.Value * rate, 2, MidpointRounding.AwayFromZero);
    }

    private static string NormalizeTld(string value)
    {
        return value.Trim().TrimStart('.').ToLowerInvariant();
    }

    private static bool TryGetPriceArray(JsonElement root, out JsonElement priceElement)
    {
        if (root.TryGetProperty("price", out priceElement) && priceElement.ValueKind == JsonValueKind.Array)
        {
            return true;
        }

        if (root.TryGetProperty("data", out var data) &&
            data.ValueKind == JsonValueKind.Object &&
            data.TryGetProperty("price", out priceElement) &&
            priceElement.ValueKind == JsonValueKind.Array)
        {
            return true;
        }

        priceElement = default;
        return false;
    }

    private static string? GetApiMessage(JsonElement root)
    {
        if (root.TryGetProperty("message", out var message) && message.ValueKind == JsonValueKind.String)
        {
            return message.GetString();
        }

        if (root.TryGetProperty("msg", out var msg) && msg.ValueKind == JsonValueKind.String)
        {
            return msg.GetString();
        }

        return null;
    }

    private static bool HasUsablePrices(TldRegistrationPrices value)
    {
        return value.LowestRegistrationPrice is not null || value.Prices.Count > 0;
    }

    private static string? GetString(JsonElement item, string propertyName)
    {
        return item.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
    }

    private static decimal? GetDecimal(JsonElement item, string propertyName)
    {
        if (!item.TryGetProperty(propertyName, out var value)) return null;

        return value.ValueKind switch
        {
            JsonValueKind.Number when value.TryGetDecimal(out var number) => number,
            JsonValueKind.String when decimal.TryParse(value.GetString(), NumberStyles.Number, CultureInfo.InvariantCulture, out var number) => number,
            _ => null
        };
    }

    private static bool GetRegistrationPromoCode(JsonElement item)
    {
        if (!item.TryGetProperty("promocode", out var value)) return false;

        if (value.ValueKind == JsonValueKind.True) return true;
        if (value.ValueKind == JsonValueKind.False) return false;

        return value.ValueKind == JsonValueKind.Object &&
            value.TryGetProperty("new", out var newValue) &&
            newValue.ValueKind == JsonValueKind.True;
    }

    private sealed record CacheEntry(TldRegistrationPrices Value, DateTimeOffset ExpiresAt);
}
