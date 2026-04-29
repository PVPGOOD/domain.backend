using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Domain.Backend.Utilities.Pricing;

public sealed class FrankfurterCurrencyExchangeRateService(HttpClient httpClient) : ICurrencyExchangeRateService
{
    private const string BaseCurrency = "EUR";
    private const string TargetCurrency = "CNY";
    private static readonly TimeSpan CacheDuration = TimeSpan.FromHours(24);
    private static readonly TimeSpan ErrorCacheDuration = TimeSpan.FromMinutes(5);
    private readonly SemaphoreSlim refreshLock = new(1, 1);
    private ExchangeRateTable? cachedRates;
    private DateTimeOffset errorExpiresAt;

    public async Task<decimal?> GetCnyRateAsync(string currency, CancellationToken cancellationToken)
    {
        var normalizedCurrency = NormalizeCurrency(currency);
        if (string.IsNullOrWhiteSpace(normalizedCurrency)) return null;
        if (normalizedCurrency is TargetCurrency or "RMB") return 1m;

        var rates = await GetRatesAsync(cancellationToken);
        if (rates is null) return null;

        if (normalizedCurrency == BaseCurrency)
        {
            return rates.CnyPerBase;
        }

        if (!rates.BaseToQuote.TryGetValue(normalizedCurrency, out var quotePerBase) || quotePerBase <= 0)
        {
            return null;
        }

        return rates.CnyPerBase / quotePerBase;
    }

    private async Task<ExchangeRateTable?> GetRatesAsync(CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        if (cachedRates is not null && cachedRates.ExpiresAt > now)
        {
            return cachedRates;
        }

        if (errorExpiresAt > now)
        {
            return cachedRates;
        }

        await refreshLock.WaitAsync(cancellationToken);
        try
        {
            now = DateTimeOffset.UtcNow;
            if (cachedRates is not null && cachedRates.ExpiresAt > now)
            {
                return cachedRates;
            }

            var table = await FetchRatesAsync(cancellationToken);
            if (table is null)
            {
                errorExpiresAt = now.Add(ErrorCacheDuration);
                return cachedRates;
            }

            cachedRates = table;
            errorExpiresAt = default;
            return cachedRates;
        }
        finally
        {
            refreshLock.Release();
        }
    }

    private async Task<ExchangeRateTable?> FetchRatesAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var response = await httpClient.GetAsync(
                $"v2/rates?base={BaseCurrency}",
                cancellationToken);
            response.EnsureSuccessStatusCode();

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
            var root = document.RootElement;

            if (root.ValueKind != JsonValueKind.Array)
            {
                return null;
            }

            var rates = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase)
            {
                [BaseCurrency] = 1m
            };

            foreach (var item in root.EnumerateArray())
            {
                if (!TryGetString(item, "quote", out var quote) ||
                    !item.TryGetProperty("rate", out var rateElement) ||
                    !rateElement.TryGetDecimal(out var rate) ||
                    rate <= 0)
                {
                    continue;
                }

                rates[NormalizeCurrency(quote)] = rate;
            }

            if (!rates.TryGetValue(TargetCurrency, out var cnyPerBase))
            {
                return null;
            }

            return new ExchangeRateTable(
                cnyPerBase,
                rates,
                DateTimeOffset.UtcNow.Add(CacheDuration));
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or TaskCanceledException)
        {
            return null;
        }
    }

    private static string NormalizeCurrency(string currency)
    {
        return currency.Trim().ToUpperInvariant();
    }

    private static bool TryGetString(JsonElement item, string propertyName, out string value)
    {
        if (item.TryGetProperty(propertyName, out var property) && property.ValueKind == JsonValueKind.String)
        {
            value = property.GetString() ?? string.Empty;
            return !string.IsNullOrWhiteSpace(value);
        }

        value = string.Empty;
        return false;
    }

    private sealed record ExchangeRateTable(
        decimal CnyPerBase,
        IReadOnlyDictionary<string, decimal> BaseToQuote,
        DateTimeOffset ExpiresAt);
}
