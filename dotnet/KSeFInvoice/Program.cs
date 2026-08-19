using KSeF.Client.Api.Services;
using KSeF.Client.Core.Interfaces.Clients;
using KSeF.Client.Core.Interfaces.Services;
using KSeF.Client.DI;
using KSeFInvoice;
using Microsoft.Extensions.DependencyInjection;

var invoicesRootPath = Path.Combine(Directory.GetCurrentDirectory(), "invoices");
var inputService = new InvoiceInputService();
using var httpClient = new HttpClient();
var exchangeRateService = new ExchangeRateService(httpClient);

if (args.Length > 0)
{
    if (string.Equals(args[0], "ewidencja", StringComparison.OrdinalIgnoreCase))
    {
        try
        {
            await RunEwidencjaAsync(args.Skip(1).ToArray());
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Failed to generate ewidencja: {ex.Message}");
            Environment.ExitCode = 1;
        }

        return;
    }

    Console.Error.WriteLine($"Unknown command '{args[0]}'.");
    Environment.ExitCode = 1;
    return;
}

var invoiceFiles = inputService.DiscoverInputFiles(invoicesRootPath);
if (invoiceFiles.Count == 0)
{
    Console.WriteLine($"No invoice input files found under {invoicesRootPath}.");
    return;
}

Console.WriteLine($"Discovered {invoiceFiles.Count} invoice input file(s).");

ServiceProvider? serviceProvider = null;
InvoiceSender? sender = null;
SendInvoiceOptions? sendOptions = null;

int submittedCount = 0;
int regeneratedPdfCount = 0;
int skippedCount = 0;
int failedCount = 0;

try
{
    foreach (var file in invoiceFiles)
    {
        string relativeInputPath = Path.GetRelativePath(Directory.GetCurrentDirectory(), file.InputPath);
        string relativePdfPath = Path.GetRelativePath(Directory.GetCurrentDirectory(), file.PdfPath);

        if (File.Exists(file.PdfPath))
        {
            Console.WriteLine($"Skipping {relativeInputPath} because {relativePdfPath} already exists.");
            skippedCount++;
            continue;
        }

        Console.WriteLine($"Processing {relativeInputPath}...");

        try
        {
            var existingSubmission = await inputService.TryLoadSubmissionAsync(file);
            if (existingSubmission != null)
            {
                Console.WriteLine("  Found existing submission record. Regenerating PDF only.");
                await GeneratePdfAsync(existingSubmission.InvoiceData, existingSubmission, file.PdfPath);
                regeneratedPdfCount++;
                continue;
            }

            var loadedInvoice = await inputService.LoadAsync(file);
            EnsureSellerNip(loadedInvoice.InvoiceData, relativeInputPath);
            await EnrichExchangeRateIfNeededAsync(loadedInvoice.InvoiceData);
            var context = await EnsureSenderAsync();

            string invoiceXml = loadedInvoice.InvoiceXml ?? InvoiceXmlBuilder.Build(loadedInvoice.InvoiceData);
            if (loadedInvoice.InvoiceXml != null && loadedInvoice.InvoiceData.ExchangeRate != null)
            {
                invoiceXml = InvoiceXmlExchangeRateUpdater.Apply(loadedInvoice.InvoiceXml, loadedInvoice.InvoiceData);
            }

            var preparedInvoice = new PreparedInvoice
            {
                File = file,
                InvoiceData = loadedInvoice.InvoiceData,
                InvoiceXml = invoiceXml,
            };

            var submissionResult = await context.Sender.SendAsync(preparedInvoice, context.Options);
            if (submissionResult.RejectedByKsef)
            {
                Console.Error.WriteLine(
                    $"  Skipping {relativeInputPath} because KSeF returned terminal status {submissionResult.InvoiceStatusCode}: {submissionResult.InvoiceStatusDescription ?? "No description"}");
                skippedCount++;
                continue;
            }

            var submissionRecord = new InvoiceSubmissionRecord
            {
                SourcePath = relativeInputPath,
                SourceFormat = file.Format.ToString().ToLowerInvariant(),
                ReferenceNumber = submissionResult.ReferenceNumber,
                VerificationUrl = submissionResult.VerificationUrl,
                KsefNumber = submissionResult.KsefNumber,
                PollingError = submissionResult.PollingError,
                SubmittedAtUtc = DateTime.UtcNow.ToString("O"),
                InvoiceData = preparedInvoice.InvoiceData,
            };

            await inputService.SaveSubmissionAsync(file, submissionRecord);
            await GeneratePdfAsync(preparedInvoice.InvoiceData, submissionRecord, file.PdfPath);

            Console.WriteLine($"  Saved PDF to {relativePdfPath}");
            submittedCount++;
        }
        catch (Exception ex)
        {
            failedCount++;
            Console.Error.WriteLine($"  Failed to process {relativeInputPath}: {ex.Message}");
        }
    }
}
finally
{
    serviceProvider?.Dispose();
}

Console.WriteLine();
Console.WriteLine("Batch summary:");
Console.WriteLine($"  Submitted: {submittedCount}");
Console.WriteLine($"  Regenerated PDFs: {regeneratedPdfCount}");
Console.WriteLine($"  Skipped: {skippedCount}");
Console.WriteLine($"  Failed: {failedCount}");

if (failedCount > 0)
    Environment.ExitCode = 1;

return;

async Task RunEwidencjaAsync(string[] commandArgs)
{
    DateOnly month = ParseMonth(commandArgs);
    var generator = new EwidencjaGenerator(
        inputService,
        new PdfTransactionDateExtractor(),
        exchangeRateService);

    var result = await generator.GenerateAsync(invoicesRootPath, month);
    string relativeOutputPath = Path.GetRelativePath(Directory.GetCurrentDirectory(), result.OutputPath);

    Console.WriteLine($"Generated ewidencja PDF: {relativeOutputPath}");
    Console.WriteLine($"Rows: {result.Document.Entries.Count}");
    Console.WriteLine($"Total revenue: {InvoiceMath.FormatPolishAmount(result.Document.TotalRevenue)} PLN");

    if (result.MissingCurrencyDifferenceData.Count == 0)
        return;

    Console.WriteLine();
    Console.WriteLine("Currency-difference rows were not generated for invoice(s) missing settlement data:");
    foreach (string missing in result.MissingCurrencyDifferenceData)
        Console.WriteLine($"  {missing}");
}

DateOnly ParseMonth(string[] commandArgs)
{
    for (int i = 0; i < commandArgs.Length; i++)
    {
        if (!string.Equals(commandArgs[i], "--month", StringComparison.OrdinalIgnoreCase))
            continue;

        if (i + 1 >= commandArgs.Length)
            throw new InvalidOperationException("Missing value for --month. Expected yyyy-MM.");

        return ParseMonthValue(commandArgs[i + 1]);
    }

    throw new InvalidOperationException("Missing --month argument. Example: ewidencja --month 2026-07.");
}

DateOnly ParseMonthValue(string value)
{
    if (DateOnly.TryParseExact(
        $"{value}-01",
        "yyyy-MM-dd",
        System.Globalization.CultureInfo.InvariantCulture,
        System.Globalization.DateTimeStyles.None,
        out var parsed))
    {
        return parsed;
    }

    throw new InvalidOperationException($"Month must use yyyy-MM format. Received '{value}'.");
}

async Task GeneratePdfAsync(InvoiceData invoiceData, InvoiceSubmissionRecord submissionRecord, string pdfPath)
{
    byte[] qrPng = QrCodeService.GenerateQrCode(submissionRecord.VerificationUrl);
    string qrBase64 = Convert.ToBase64String(qrPng);
    string qrDataUrl = $"data:image/png;base64,{qrBase64}";

    string html = InvoiceHtmlGenerator.Generate(invoiceData, new InvoiceHtmlOptions
    {
        VerificationUrl = submissionRecord.VerificationUrl,
        QrCodeDataUrl = qrDataUrl,
        KsefNumber = submissionRecord.KsefNumber,
    });

    await InvoicePdfGenerator.GenerateAsync(html, pdfPath);
}

async Task EnrichExchangeRateIfNeededAsync(InvoiceData invoiceData)
{
    string currency = InvoiceMath.GetCurrency(invoiceData);
    if (string.Equals(currency, "PLN", StringComparison.OrdinalIgnoreCase))
        return;

    string exchangeRateSourceDate = InvoiceMath.GetSaleDate(invoiceData);
    Console.WriteLine($"  Fetching exchange rate from NBP using {exchangeRateSourceDate} (P_6 / SaleDate) minus 1 day...");
    await exchangeRateService.EnrichAsync(invoiceData);
}

void EnsureSellerNip(InvoiceData invoiceData, string relativeInputPath)
{
    if (string.IsNullOrWhiteSpace(invoiceData.Seller.Nip))
    {
        throw new InvalidOperationException(
            $"Seller NIP is required in {relativeInputPath} to authenticate and submit the invoice to KSeF.");
    }
}

async Task<(InvoiceSender Sender, SendInvoiceOptions Options)> EnsureSenderAsync()
{
    if (sender != null && sendOptions != null)
        return (sender, sendOptions);

    sendOptions = new SendInvoiceOptions
    {
        BaseUrl = string.Equals(Environment.GetEnvironmentVariable("KSEF_ENV"), "PROD", StringComparison.OrdinalIgnoreCase) ? KsefEnvironmentsUris.PROD : KsefEnvironmentsUris.TEST,
        CertPath = Environment.GetEnvironmentVariable("KSEF_PUBLIC_KEY_PATH")
            ?? throw new InvalidOperationException("KSEF_PUBLIC_KEY_PATH env var is required"),
        KeyPath = Environment.GetEnvironmentVariable("KSEF_PRIVATE_KEY_PATH")
            ?? throw new InvalidOperationException("KSEF_PRIVATE_KEY_PATH env var is required"),
        KeyPassword = Environment.GetEnvironmentVariable("KSEF_PRIVATE_KEY_PASSWORD")
            ?? throw new InvalidOperationException("KSEF_PRIVATE_KEY_PASSWORD env var is required"),
    };

    var services = new ServiceCollection();
    services.AddKSeFClient(options =>
    {
        options.BaseUrl = sendOptions.BaseUrl;
    });
    services.AddCryptographyClient();

    serviceProvider = services.BuildServiceProvider();

    var ksefClient = serviceProvider.GetRequiredService<IKSeFClient>();
    var cryptoService = serviceProvider.GetRequiredService<ICryptographyService>();
    var verificationLinkService = serviceProvider.GetRequiredService<IVerificationLinkService>();

    Console.WriteLine("Initializing KSeF cryptography...");
    await cryptoService.WarmupAsync(CancellationToken.None);

    sender = new InvoiceSender(ksefClient, cryptoService, verificationLinkService);
    return (sender, sendOptions);
}
