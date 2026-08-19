using System.Globalization;
using System.Net;
using System.Text;
using KSeFInvoice;

namespace KSeFInvoice.Tests;

public sealed class ExchangeRateServiceTests
{
    [Fact]
    public async Task EnrichAsync_PlnInvoice_NormalizesCurrencyAndClearsExchangeRate()
    {
        var invoice = TestInvoices.Create(currency: " pln ");
        invoice.ExchangeRate = new InvoiceExchangeRate
        {
            Currency = "USD",
            Table = "stale",
            EffectiveDate = "2026-07-09",
            Rate = 4m,
        };
        var handler = new FixtureNbpHandler();
        var service = new ExchangeRateService(new HttpClient(handler));

        await service.EnrichAsync(invoice);

        Assert.Equal("PLN", invoice.Currency);
        Assert.Null(invoice.ExchangeRate);
        Assert.Empty(handler.RequestPaths);
    }

    [Fact]
    public async Task EnrichAsync_ForeignCurrency_UsesMostRecentAvailableRateBeforeSaleDate()
    {
        var invoice = TestInvoices.Create(currency: " usd ", saleDate: "2026-07-27");
        var handler = new FixtureNbpHandler(
            ("2026-07-26", HttpStatusCode.NotFound, 0m),
            ("2026-07-25", HttpStatusCode.NotFound, 0m),
            ("2026-07-24", HttpStatusCode.OK, 3.8123m));
        var service = new ExchangeRateService(new HttpClient(handler));

        await service.EnrichAsync(invoice);

        Assert.NotNull(invoice.ExchangeRate);
        Assert.Equal("USD", invoice.Currency);
        Assert.Equal("001/A/NBP/2026", invoice.ExchangeRate.Table);
        Assert.Equal("2026-07-24", invoice.ExchangeRate.EffectiveDate);
        Assert.Equal(3.8123m, invoice.ExchangeRate.Rate);
        Assert.Equal(
            [
                "/api/exchangerates/rates/a/USD/2026-07-26/",
                "/api/exchangerates/rates/a/USD/2026-07-25/",
                "/api/exchangerates/rates/a/USD/2026-07-24/",
            ],
            handler.RequestPaths);
    }

    [Fact]
    public async Task EnrichAsync_EmptyNbpRates_ThrowsUsefulError()
    {
        var invoice = TestInvoices.Create(currency: "EUR", saleDate: "2026-07-10");
        var service = new ExchangeRateService(new HttpClient(new FixtureNbpHandler(
            ("2026-07-09", HttpStatusCode.OK, null))));

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => service.EnrichAsync(invoice));

        Assert.Contains("did not include any rates", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetAverageRateForDayBeforeAsync_UsesPrecedingWorkingDay()
    {
        var handler = new FixtureNbpHandler(("2026-07-09", HttpStatusCode.OK, 3.7692m));
        var service = new ExchangeRateService(new HttpClient(handler));

        var rate = await service.GetAverageRateForDayBeforeAsync("USD", new DateOnly(2026, 7, 10));

        Assert.Equal("2026-07-09", rate.EffectiveDate);
        Assert.Equal(3.7692m, rate.Rate);
        Assert.Equal("/api/exchangerates/rates/a/USD/2026-07-09/", handler.RequestPaths[0]);
    }

    [Fact]
    public async Task GetAverageRateForDayBeforeAsync_FallsBackOverWeekendOrHoliday()
    {
        var handler = new FixtureNbpHandler(
            ("2026-07-26", HttpStatusCode.NotFound, 0m),
            ("2026-07-25", HttpStatusCode.NotFound, 0m),
            ("2026-07-24", HttpStatusCode.OK, 3.8m));
        var service = new ExchangeRateService(new HttpClient(handler));

        var rate = await service.GetAverageRateForDayBeforeAsync("USD", new DateOnly(2026, 7, 27));

        Assert.Equal("2026-07-24", rate.EffectiveDate);
        Assert.Equal(
            [
                "/api/exchangerates/rates/a/USD/2026-07-26/",
                "/api/exchangerates/rates/a/USD/2026-07-25/",
                "/api/exchangerates/rates/a/USD/2026-07-24/",
            ],
            handler.RequestPaths);
    }

    private sealed class FixtureNbpHandler(params (string Date, HttpStatusCode StatusCode, decimal? Rate)[] responses) : HttpMessageHandler
    {
        private readonly Dictionary<string, (HttpStatusCode StatusCode, decimal? Rate)> _responses =
            responses.ToDictionary(item => item.Date, item => (item.StatusCode, item.Rate), StringComparer.Ordinal);

        public List<string> RequestPaths { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            string path = request.RequestUri!.AbsolutePath;
            RequestPaths.Add(path);
            string date = path.Split('/', StringSplitOptions.RemoveEmptyEntries).Last();

            if (!_responses.TryGetValue(date, out var response) || response.StatusCode == HttpStatusCode.NotFound)
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));

            string ratesJson = response.Rate == null
                ? "[]"
                : $$"""
                  [
                    {
                      "no": "001/A/NBP/2026",
                      "effectiveDate": "{{date}}",
                      "mid": {{response.Rate.Value.ToString(CultureInfo.InvariantCulture)}}
                    }
                  ]
                  """;

            string json = $$"""
              {
                "rates": {{ratesJson}}
              }
              """;

            return Task.FromResult(new HttpResponseMessage(response.StatusCode)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json"),
            });
        }
    }
}
