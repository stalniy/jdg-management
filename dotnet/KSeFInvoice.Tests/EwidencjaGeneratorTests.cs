using System.Globalization;
using System.Text;
using KSeFInvoice;

namespace KSeFInvoice.Tests;

public sealed class EwidencjaGeneratorTests
{
    [Fact]
    public async Task PlnInvoice_PlacesRevenueInDefaultRyczaltColumn()
    {
        using var fixture = new EwidencjaFixture();
        fixture.AddInvoice("f-01-07-2026", currency: "PLN", unitPrice: 1000m, transactionDate: new DateOnly(2026, 7, 10));

        var result = await fixture.BuildAsync("2026-07");
        var entry = Assert.Single(result.Document.Entries);

        Assert.Equal(1000m, entry.RevenuePln);
        Assert.Equal(1000m, entry.AmountForRate(12m));
        Assert.Equal(0m, entry.AmountForRate(8.5m));
        Assert.Equal(1000m, result.Document.TotalRevenue);
        Assert.Empty(fixture.ExchangeRateProvider.Calls);
    }

    [Fact]
    public async Task UsdInvoice_ConvertsRevenueIntoPln()
    {
        using var fixture = new EwidencjaFixture();
        fixture.ExchangeRateProvider.Set("USD", new DateOnly(2026, 7, 10), 4m);
        fixture.AddInvoice("f-01-07-2026", currency: "USD", unitPrice: 100m, transactionDate: new DateOnly(2026, 7, 10));

        var result = await fixture.BuildAsync("2026-07");
        var entry = Assert.Single(result.Document.Entries);

        Assert.Equal(400m, entry.RevenuePln);
        Assert.Equal(400m, entry.AmountForRate(12m));
    }

    [Fact]
    public async Task SaleDate_DrivesProgrammingServicesRow_AndTransactionDateDrivesCurrencyDifferenceRow()
    {
        using var fixture = new EwidencjaFixture();
        fixture.ExchangeRateProvider.Set("USD", new DateOnly(2026, 7, 9), 4m);
        fixture.ExchangeRateProvider.Set("USD", new DateOnly(2026, 7, 10), 4.1m);
        fixture.AddInvoice(
            "f-01-07-2026",
            currency: "USD",
            unitPrice: 100m,
            issueDate: "2026-08-05",
            saleDate: "2026-07-09",
            transactionDate: new DateOnly(2026, 7, 10),
            submissionRate: 4m);

        var result = await fixture.BuildAsync("2026-07");
        var serviceEntry = result.Document.Entries.Single(entry => entry.Notes == "USŁUGI PROGRAMISTYCZNE");
        var differenceEntry = result.Document.Entries.Single(entry => entry.Notes == "Różnica kursowa");

        Assert.Equal(new DateOnly(2026, 7, 9), serviceEntry.RevenueDate);
        Assert.Equal(new DateOnly(2026, 7, 9), serviceEntry.EntryDate);
        Assert.Equal(400m, serviceEntry.RevenuePln);
        Assert.Equal(new DateOnly(2026, 7, 10), differenceEntry.RevenueDate);
        Assert.Equal(new DateOnly(2026, 7, 10), differenceEntry.EntryDate);
        Assert.Equal(10m, differenceEntry.RevenuePln);
    }

    [Fact]
    public async Task PositiveCurrencyDifference_CreatesAdditionalEntry()
    {
        using var fixture = new EwidencjaFixture();
        fixture.ExchangeRateProvider.Set("USD", new DateOnly(2026, 7, 5), 3.7m);
        fixture.ExchangeRateProvider.Set("USD", new DateOnly(2026, 7, 10), 3.9m);
        fixture.AddInvoice(
            "f-01-07-2026",
            currency: "USD",
            unitPrice: 100m,
            saleDate: "2026-07-05",
            transactionDate: new DateOnly(2026, 7, 10),
            submissionRate: 3.7m);

        var result = await fixture.BuildAsync("2026-07");

        Assert.Equal(2, result.Document.Entries.Count);
        Assert.Equal(20m, result.Document.Entries[1].RevenuePln);
        Assert.Equal("Różnica kursowa", result.Document.Entries[1].Notes);
        Assert.Equal("RKUR/01/2026/USD", result.Document.Entries[1].AccountingDocumentNumber);
    }

    [Fact]
    public async Task NegativeCurrencyDifference_CreatesNegativeAdditionalEntry()
    {
        using var fixture = new EwidencjaFixture();
        fixture.ExchangeRateProvider.Set("USD", new DateOnly(2026, 7, 5), 3.7m);
        fixture.ExchangeRateProvider.Set("USD", new DateOnly(2026, 7, 10), 3.6m);
        fixture.AddInvoice(
            "f-01-07-2026",
            currency: "USD",
            unitPrice: 100m,
            saleDate: "2026-07-05",
            transactionDate: new DateOnly(2026, 7, 10),
            submissionRate: 3.7m);

        var result = await fixture.BuildAsync("2026-07");

        Assert.Equal(-10m, result.Document.Entries[1].RevenuePln);
        Assert.Equal(-10m, result.Document.Entries[1].AmountForRate(12m));
    }

    [Fact]
    public async Task CustomCurrencyDifferenceDocumentNumber_IsUsedWhenProvided()
    {
        using var fixture = new EwidencjaFixture();
        fixture.ExchangeRateProvider.Set("USD", new DateOnly(2026, 7, 5), 3.7m);
        fixture.ExchangeRateProvider.Set("USD", new DateOnly(2026, 7, 10), 3.9m);
        fixture.AddInvoice(
            "f-01-07-2026",
            currency: "USD",
            unitPrice: 100m,
            saleDate: "2026-07-05",
            transactionDate: new DateOnly(2026, 7, 10),
            submissionRate: 3.7m,
            exchangeDifferenceDocumentNumber: "RK/OWN/2026");

        var result = await fixture.BuildAsync("2026-07");

        Assert.Equal("RK/OWN/2026", result.Document.Entries[1].AccountingDocumentNumber);
    }

    [Fact]
    public async Task MissingSubmissionExchangeRate_RecordsMissingCurrencyDifferenceData()
    {
        using var fixture = new EwidencjaFixture();
        fixture.ExchangeRateProvider.Set("USD", new DateOnly(2026, 7, 10), 3.9m);
        fixture.AddInvoice(
            "f-01-07-2026",
            currency: "USD",
            unitPrice: 100m,
            transactionDate: new DateOnly(2026, 7, 10));

        var result = await fixture.BuildAsync("2026-07");

        Assert.Single(result.Document.Entries);
        string missing = Assert.Single(result.MissingCurrencyDifferenceData);
        Assert.Contains("f-01-07-2026", missing, StringComparison.Ordinal);
        Assert.Contains("ksef-submission.InvoiceData.ExchangeRate", missing, StringComparison.Ordinal);
    }

    [Fact]
    public async Task MissingKsefId_LeavesFieldEmpty()
    {
        using var fixture = new EwidencjaFixture();
        fixture.AddInvoice(
            "f-01-07-2026",
            currency: "PLN",
            unitPrice: 100m,
            transactionDate: new DateOnly(2026, 7, 10),
            ksefNumber: null);

        var result = await fixture.BuildAsync("2026-07");

        Assert.Equal(string.Empty, Assert.Single(result.Document.Entries).KsefNumber);
    }

    [Fact]
    public async Task MultipleInvoicesInOneMonth_SortAndNumberCorrectly()
    {
        using var fixture = new EwidencjaFixture();
        fixture.ExchangeRateProvider.Set("USD", new DateOnly(2026, 7, 27), 3.8m);
        fixture.ExchangeRateProvider.Set("USD", new DateOnly(2026, 7, 10), 3.7m);
        fixture.AddInvoice("f-02-07-2026", currency: "USD", unitPrice: 200m, transactionDate: new DateOnly(2026, 7, 27));
        fixture.AddInvoice("f-01-07-2026", currency: "USD", unitPrice: 100m, transactionDate: new DateOnly(2026, 7, 10));

        var result = await fixture.BuildAsync("2026-07");

        Assert.Equal(2, result.Document.Entries.Count);
        Assert.Equal(1, result.Document.Entries[0].Lp);
        Assert.Equal("F/01/07/2026", result.Document.Entries[0].AccountingDocumentNumber);
        Assert.Equal(2, result.Document.Entries[1].Lp);
        Assert.Equal("F/02/07/2026", result.Document.Entries[1].AccountingDocumentNumber);
    }

    [Fact]
    public async Task MonthlyTotals_AreCalculatedAcrossInvoices()
    {
        using var fixture = new EwidencjaFixture();
        fixture.ExchangeRateProvider.Set("USD", new DateOnly(2026, 7, 10), 4m);
        fixture.AddInvoice("f-01-07-2026", currency: "PLN", unitPrice: 100m, transactionDate: new DateOnly(2026, 7, 5));
        fixture.AddInvoice("f-02-07-2026", currency: "USD", unitPrice: 50m, transactionDate: new DateOnly(2026, 7, 10));

        var result = await fixture.BuildAsync("2026-07");

        Assert.Equal(300m, result.Document.TotalRevenue);
        Assert.Equal(300m, result.Document.TotalsByRate[12m]);
        Assert.Equal(0m, result.Document.TotalsByRate[17m]);
    }

    private sealed class EwidencjaFixture : IDisposable
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), $"ksef-ewidencja-tests-{Guid.NewGuid():N}");
        private readonly FakeTransactionDateProvider _transactionDateProvider = new();

        public FakeExchangeRateProvider ExchangeRateProvider { get; } = new();

        public void AddInvoice(
            string baseName,
            string currency,
            decimal unitPrice,
            DateOnly transactionDate,
            string issueDate = "2026-08-05",
            string? saleDate = null,
            string? ksefNumber = "123-KSEF",
            decimal? submissionRate = null,
            string? exchangeDifferenceDocumentNumber = null)
        {
            string monthDirectory = Path.Combine(_root, "2026-07");
            Directory.CreateDirectory(monthDirectory);
            string yamlPath = Path.Combine(monthDirectory, $"{baseName}.yaml");
            string confirmationPath = Path.Combine(monthDirectory, $"{baseName}-Potwierdzenie.pdf");
            string submissionPath = Path.Combine(monthDirectory, $"{baseName}.ksef-submission.json");
            string invoiceNumber = InvoiceNumberFromBaseName(baseName);
            string saleDateValue = saleDate ?? transactionDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

            File.WriteAllText(yamlPath, BuildYaml(invoiceNumber, issueDate, saleDateValue, currency, unitPrice, exchangeDifferenceDocumentNumber));
            File.WriteAllText(confirmationPath, "not used by fake provider");
            File.WriteAllText(submissionPath, BuildSubmissionJson(yamlPath, invoiceNumber, issueDate, saleDateValue, currency, unitPrice, ksefNumber, submissionRate));
            _transactionDateProvider.Set(confirmationPath, transactionDate);
        }

        public Task<EwidencjaGenerationResult> BuildAsync(string month)
        {
            var generator = new EwidencjaGenerator(
                new InvoiceInputService(),
                _transactionDateProvider,
                ExchangeRateProvider);

            return generator.BuildAsync(_root, DateOnly.ParseExact($"{month}-01", "yyyy-MM-dd", CultureInfo.InvariantCulture));
        }

        public void Dispose()
        {
            if (Directory.Exists(_root))
                Directory.Delete(_root, recursive: true);
        }

        private static string BuildYaml(
            string invoiceNumber,
            string issueDate,
            string saleDate,
            string currency,
            decimal unitPrice,
            string? exchangeDifferenceDocumentNumber)
        {
            var paymentExtras = new StringBuilder();
            if (exchangeDifferenceDocumentNumber != null)
                paymentExtras.AppendLine($"  ExchangeDifferenceDocumentNumber: {exchangeDifferenceDocumentNumber}");

            return $$"""
              InvoiceNumber: {{invoiceNumber}}
              IssueDate: {{issueDate}}
              SaleDate: {{saleDate}}
              Currency: {{currency}}
              Seller:
                Nip: 6751818483
                Name: Seller
                Address:
                  Street: Street
                  BuildingNumber: "1"
                  PostalCode: 00-000
                  City: City
              Buyer:
                Name: Overclock Labs, Inc
                Address:
                  Street: Market Street
                  BuildingNumber: "548"
                  PostalCode: "94104"
                  City: San Francisco
                  CountryCode: US
              LineItems:
                - Description: uslugi programistyczne
                  Unit: usl.
                  Quantity: 1
                  UnitPriceNet: {{unitPrice.ToString(CultureInfo.InvariantCulture)}}
                  VatRate: 0
              Payment:
                FormaPlatnosci: 6
                BankAccount: PL123
              {{paymentExtras}}
              """;
        }

        private static string BuildSubmissionJson(
            string yamlPath,
            string invoiceNumber,
            string issueDate,
            string saleDate,
            string currency,
            decimal unitPrice,
            string? ksefNumber,
            decimal? submissionRate)
        {
            string exchangeRateJson = submissionRate == null
                ? "null"
                : $$"""
                  {
                          "Currency": "{{currency}}",
                          "Table": "001/A/NBP/2026",
                          "EffectiveDate": "{{saleDate}}",
                          "Rate": {{submissionRate.Value.ToString(CultureInfo.InvariantCulture)}}
                        }
                  """;
            string ksefJson = ksefNumber == null ? "null" : $"\"{ksefNumber}\"";

            return $$"""
              {
                "SourcePath": "{{yamlPath.Replace("\\", "\\\\")}}",
                "SourceFormat": "yaml",
                "ReferenceNumber": "ref",
                "VerificationUrl": "https://example.test",
                "KsefNumber": {{ksefJson}},
                "PollingError": null,
                "SubmittedAtUtc": "2026-08-05T00:00:00Z",
                "InvoiceData": {
                  "InvoiceNumber": "{{invoiceNumber}}",
                  "IssueDate": "{{issueDate}}",
                  "SaleDate": "{{saleDate}}",
                  "Currency": "{{currency}}",
                  "Seller": {
                    "Nip": "6751818483",
                    "Name": "Seller",
                    "Address": {
                      "Street": "Street",
                      "BuildingNumber": "1",
                      "PostalCode": "00-000",
                      "City": "City",
                      "CountryCode": null
                    }
                  },
                  "Buyer": {
                    "Nip": null,
                    "Name": "Overclock Labs, Inc",
                    "Address": {
                      "Street": "Market Street",
                      "BuildingNumber": "548",
                      "PostalCode": "94104",
                      "City": "San Francisco",
                      "CountryCode": "US"
                    }
                  },
                  "LineItems": [
                    {
                      "Description": "uslugi programistyczne",
                      "Unit": "usl.",
                      "Quantity": 1,
                      "UnitPriceNet": {{unitPrice.ToString(CultureInfo.InvariantCulture)}},
                      "VatRate": 0
                    }
                  ],
                  "Payment": {
                    "FormaPlatnosci": 6,
                    "BankAccount": "PL123"
                  },
                  "ExchangeRate": {{exchangeRateJson}}
                }
              }
              """;
        }

        private static string InvoiceNumberFromBaseName(string baseName)
        {
            string[] parts = baseName.Split('-');
            return $"F/{parts[1]}/{parts[2]}/{parts[3]}";
        }
    }

    private sealed class FakeTransactionDateProvider : ITransactionDateProvider
    {
        private readonly Dictionary<string, DateOnly> _dates = new(StringComparer.OrdinalIgnoreCase);

        public void Set(string path, DateOnly date) => _dates[path] = date;

        public Task<DateOnly> GetTransactionDateAsync(string pdfPath, CancellationToken ct = default)
        {
            if (!_dates.TryGetValue(pdfPath, out var date))
                throw new InvalidOperationException($"No transaction date fixture for {pdfPath}.");

            return Task.FromResult(date);
        }
    }

    public sealed class FakeExchangeRateProvider : IExchangeRateProvider
    {
        private readonly Dictionary<(string Currency, DateOnly Date), decimal> _rates = [];

        public List<(string Currency, DateOnly Date)> Calls { get; } = [];

        public void Set(string currency, DateOnly date, decimal rate) => _rates[(currency, date)] = rate;

        public Task<InvoiceExchangeRate> GetAverageRateForDayBeforeAsync(string currency, DateOnly date, CancellationToken ct = default)
        {
            Calls.Add((currency, date));
            if (!_rates.TryGetValue((currency, date), out decimal rate))
                throw new InvalidOperationException($"No exchange-rate fixture for {currency} on {date:yyyy-MM-dd}.");

            return Task.FromResult(new InvoiceExchangeRate
            {
                Currency = currency,
                Table = "fixture",
                EffectiveDate = date.AddDays(-1).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                Rate = rate,
            });
        }
    }
}
