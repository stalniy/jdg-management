using System.Net;
using System.Text.Json;

namespace KSeFInvoice;

public sealed class ExchangeRateService(HttpClient httpClient)
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    public async Task EnrichAsync(InvoiceData invoiceData, CancellationToken ct = default)
    {
        string currency = InvoiceMath.GetCurrency(invoiceData);
        invoiceData.Currency = currency;

        if (string.Equals(currency, "PLN", StringComparison.OrdinalIgnoreCase))
        {
            invoiceData.ExchangeRate = null;
            return;
        }

        DateOnly saleDate = InvoiceMath.ParseDate(InvoiceMath.GetSaleDate(invoiceData), "SaleDate");
        DateOnly queryDate = saleDate.AddDays(-1);

        for (int offset = 0; offset < 14; offset++)
        {
            DateOnly candidateDate = queryDate.AddDays(-offset);
            string requestUri = $"https://api.nbp.pl/api/exchangerates/rates/a/{currency}/{candidateDate:yyyy-MM-dd}/?format=json";

            using var response = await httpClient.GetAsync(requestUri, ct);
            if (response.StatusCode == HttpStatusCode.NotFound)
                continue;

            response.EnsureSuccessStatusCode();

            await using var stream = await response.Content.ReadAsStreamAsync(ct);
            var payload = await JsonSerializer.DeserializeAsync<NbpExchangeRateResponse>(stream, SerializerOptions, ct)
                ?? throw new InvalidOperationException($"NBP response for {currency} on {candidateDate:yyyy-MM-dd} was empty.");

            var rate = payload.Rates.FirstOrDefault()
                ?? throw new InvalidOperationException($"NBP response for {currency} on {candidateDate:yyyy-MM-dd} did not include any rates.");

            invoiceData.ExchangeRate = new InvoiceExchangeRate
            {
                Currency = currency,
                Table = rate.No,
                EffectiveDate = rate.EffectiveDate,
                Rate = rate.Mid,
            };

            return;
        }

        throw new InvalidOperationException(
            $"Could not find an NBP exchange rate for {currency} before {saleDate:yyyy-MM-dd}. Tried 14 calendar days.");
    }

    private sealed class NbpExchangeRateResponse
    {
        public required List<NbpExchangeRateItem> Rates { get; init; }
    }

    private sealed class NbpExchangeRateItem
    {
        public required string No { get; init; }
        public required string EffectiveDate { get; init; }
        public required decimal Mid { get; init; }
    }
}
