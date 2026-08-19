using System.Globalization;

namespace KSeFInvoice;

public sealed class EwidencjaGenerator(
    InvoiceInputService inputService,
    ITransactionDateProvider transactionDateProvider,
    IExchangeRateProvider exchangeRateProvider)
{
    private const string ProgrammingServicesNote = "USŁUGI PROGRAMISTYCZNE";
    private const string CurrencyDifferenceNote = "Różnica kursowa";

    public async Task<EwidencjaGenerationResult> GenerateAsync(
        string invoicesRootPath,
        DateOnly month,
        CancellationToken ct = default)
    {
        var result = await BuildAsync(invoicesRootPath, month, ct);
        string html = EwidencjaHtmlGenerator.Generate(result.Document);
        await InvoicePdfGenerator.GenerateAsync(html, result.OutputPath, new PdfGenerationOptions { Landscape = true });
        return result;
    }

    public async Task<EwidencjaGenerationResult> BuildAsync(
        string invoicesRootPath,
        DateOnly month,
        CancellationToken ct = default)
    {
        DateOnly requestedMonth = new(month.Year, month.Month, 1);
        string monthFolder = Path.Combine(invoicesRootPath, requestedMonth.ToString("yyyy-MM", CultureInfo.InvariantCulture));

        if (!Directory.Exists(monthFolder))
            throw new InvalidOperationException($"Invoice folder '{monthFolder}' does not exist.");

        var invoiceFiles = inputService
            .DiscoverInputFiles(monthFolder)
            .Where(file => file.Format == InvoiceSourceFormat.Yaml)
            .OrderBy(file => file.InputPath, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (invoiceFiles.Count == 0)
            throw new InvalidOperationException($"No YAML invoices found in '{monthFolder}'.");

        var entries = new List<EwidencjaEntry>();
        var missingCurrencyDifferenceData = new List<string>();

        foreach (var file in invoiceFiles)
        {
            var confirmationPdfPath = FindConfirmationPdf(file);
            if (!File.Exists(file.SubmissionPath))
                throw new InvalidOperationException($"Missing KSeF submission sidecar for '{file.InputPath}': expected '{file.SubmissionPath}'.");

            var loadedInvoice = await inputService.LoadAsync(file, ct);
            var submission = await inputService.TryLoadSubmissionAsync(file, ct)
                ?? throw new InvalidOperationException($"Submission sidecar '{file.SubmissionPath}' is empty.");

            string currency = InvoiceMath.GetCurrency(loadedInvoice.InvoiceData);
            DateOnly saleDate = InvoiceMath.ParseDate(InvoiceMath.GetSaleDate(loadedInvoice.InvoiceData), "SaleDate");
            decimal revenueInInvoiceCurrency = InvoiceMath.GetTotals(loadedInvoice.InvoiceData).Net;
            string sourceBaseName = Path.GetFileNameWithoutExtension(file.InputPath);

            if (saleDate.Year == requestedMonth.Year && saleDate.Month == requestedMonth.Month)
            {
                var saleRate = await GetExchangeRateAsync(currency, saleDate, ct);
                decimal revenuePln = ConvertRevenueToPln(currency, revenueInInvoiceCurrency, saleRate);

                entries.Add(new EwidencjaEntry
                {
                    EntryDate = saleDate,
                    RevenueDate = saleDate,
                    KsefNumber = submission.KsefNumber ?? string.Empty,
                    AccountingDocumentNumber = loadedInvoice.InvoiceData.InvoiceNumber,
                    ContractorTaxIdentifier = GetContractorTaxIdentifier(loadedInvoice.InvoiceData.Buyer),
                    RyczaltRate = EwidencjaRates.DefaultRevenueRate,
                    RevenuePln = revenuePln,
                    Notes = ProgrammingServicesNote,
                    SourceBaseName = sourceBaseName,
                });
            }

            if (string.Equals(currency, "PLN", StringComparison.OrdinalIgnoreCase))
                continue;

            DateOnly transactionDate = await transactionDateProvider.GetTransactionDateAsync(confirmationPdfPath, ct);
            if (transactionDate.Year != requestedMonth.Year || transactionDate.Month != requestedMonth.Month)
                continue;

            var settlementRate = await GetExchangeRateAsync(currency, transactionDate, ct);

            var difference = TryCreateCurrencyDifferenceEntry(
                loadedInvoice.InvoiceData,
                submission,
                sourceBaseName,
                transactionDate,
                settlementRate,
                revenueInInvoiceCurrency,
                missingCurrencyDifferenceData);

            if (difference != null)
                entries.Add(difference);
        }

        entries = entries
            .OrderBy(entry => entry.EntryDate)
            .ThenBy(entry => entry.SourceBaseName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(entry => entry.Notes == CurrencyDifferenceNote ? 1 : 0)
            .ToList();

        for (int i = 0; i < entries.Count; i++)
            entries[i].Lp = i + 1;

        var totalsByRate = EwidencjaRates.OrderedRates
            .ToDictionary(rate => rate, rate => RoundPln(entries.Sum(entry => entry.AmountForRate(rate))));

        var document = new EwidencjaDocument
        {
            Month = requestedMonth,
            Entries = entries,
            TotalsByRate = totalsByRate,
            TotalRevenue = RoundPln(entries.Sum(entry => entry.RevenuePln)),
        };

        string outputBaseName = entries.LastOrDefault()?.SourceBaseName
            ?? Path.GetFileNameWithoutExtension(invoiceFiles.Last().InputPath);
        string outputPath = Path.Combine(monthFolder, $"{outputBaseName}-ewidencja.pdf");

        return new EwidencjaGenerationResult
        {
            Document = document,
            OutputPath = outputPath,
            MissingCurrencyDifferenceData = missingCurrencyDifferenceData,
        };
    }

    private async Task<InvoiceExchangeRate?> GetExchangeRateAsync(
        string currency,
        DateOnly date,
        CancellationToken ct)
    {
        if (string.Equals(currency, "PLN", StringComparison.OrdinalIgnoreCase))
            return null;

        return await exchangeRateProvider.GetAverageRateForDayBeforeAsync(currency, date, ct);
    }

    private static decimal ConvertRevenueToPln(
        string currency,
        decimal amount,
        InvoiceExchangeRate? settlementRate)
    {
        if (string.Equals(currency, "PLN", StringComparison.OrdinalIgnoreCase))
            return RoundPln(amount);

        if (settlementRate == null)
            throw new InvalidOperationException($"Missing settlement exchange rate for {currency}.");

        return RoundPln(amount * settlementRate.Rate);
    }

    private static EwidencjaEntry? TryCreateCurrencyDifferenceEntry(
        InvoiceData invoiceData,
        InvoiceSubmissionRecord submission,
        string sourceBaseName,
        DateOnly transactionDate,
        InvoiceExchangeRate? settlementRate,
        decimal revenueInInvoiceCurrency,
        List<string> missingCurrencyDifferenceData)
    {
        string currency = InvoiceMath.GetCurrency(invoiceData);
        if (string.Equals(currency, "PLN", StringComparison.OrdinalIgnoreCase))
            return null;

        var missing = new List<string>();
        if (submission.InvoiceData.ExchangeRate == null)
            missing.Add("ksef-submission.InvoiceData.ExchangeRate");
        if (settlementRate == null)
            missing.Add("settlement exchange rate");

        if (missing.Count != 0)
        {
            missingCurrencyDifferenceData.Add($"{sourceBaseName}: {string.Join(", ", missing)}");
            return null;
        }

        decimal invoiceValuePln = RoundPln(revenueInInvoiceCurrency * submission.InvoiceData.ExchangeRate!.Rate);
        decimal settlementValuePln = RoundPln(revenueInInvoiceCurrency * settlementRate!.Rate);
        decimal differencePln = RoundPln(settlementValuePln - invoiceValuePln);

        if (differencePln == 0m)
            return null;

        return new EwidencjaEntry
        {
            EntryDate = transactionDate,
            RevenueDate = transactionDate,
            AccountingDocumentNumber = GetExchangeDifferenceDocumentNumber(invoiceData, transactionDate, currency),
            RyczaltRate = EwidencjaRates.DefaultRevenueRate,
            RevenuePln = differencePln,
            Notes = CurrencyDifferenceNote,
            SourceBaseName = sourceBaseName,
        };
    }

    private static string FindConfirmationPdf(InvoiceInputFile file)
    {
        string directory = Path.GetDirectoryName(file.InputPath)
            ?? throw new InvalidOperationException($"Cannot determine directory for '{file.InputPath}'.");
        string baseName = Path.GetFileNameWithoutExtension(file.InputPath);
        string shortPrefix = string.Join("-", baseName.Split('-').Take(2));

        var candidates = Directory
            .EnumerateFiles(directory, "*.pdf", SearchOption.TopDirectoryOnly)
            .Where(path =>
            {
                string name = Path.GetFileNameWithoutExtension(path);
                return name.Contains("Potwierdzenie", StringComparison.OrdinalIgnoreCase)
                    && (name.StartsWith(baseName, StringComparison.OrdinalIgnoreCase)
                        || name.StartsWith(shortPrefix, StringComparison.OrdinalIgnoreCase));
            })
            .OrderBy(path => path.Length)
            .ThenBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return candidates.Count switch
        {
            0 => throw new InvalidOperationException($"Missing confirmation PDF for '{file.InputPath}'. Expected a '*Potwierdzenie*.pdf' file matching '{baseName}'."),
            1 => candidates[0],
            _ => throw new InvalidOperationException($"Multiple confirmation PDFs match '{file.InputPath}': {string.Join(", ", candidates)}"),
        };
    }

    private static string GetContractorTaxIdentifier(Party buyer)
    {
        if (!string.IsNullOrWhiteSpace(buyer.Nip))
            return buyer.Nip;

        return buyer.Address.CountryCode ?? string.Empty;
    }

    private static string GetExchangeDifferenceDocumentNumber(
        InvoiceData invoiceData,
        DateOnly transactionDate,
        string currency)
    {
        if (!string.IsNullOrWhiteSpace(invoiceData.Payment?.ExchangeDifferenceDocumentNumber))
            return invoiceData.Payment.ExchangeDifferenceDocumentNumber;

        string invoiceOrdinal = invoiceData.InvoiceNumber
            .Split('/', StringSplitOptions.RemoveEmptyEntries)
            .ElementAtOrDefault(1)
            ?? transactionDate.Month.ToString("00", CultureInfo.InvariantCulture);

        return $"RKUR/{invoiceOrdinal}/{transactionDate.Year}/{currency}";
    }

    private static decimal RoundPln(decimal amount) => Math.Round(amount, 2, MidpointRounding.AwayFromZero);
}
