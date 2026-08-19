using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using YamlDotNet.Serialization;

namespace KSeFInvoice;

public sealed class InvoiceInputService
{
    private static readonly Regex ExchangeRateDescriptionRegex = new(
        @"NBP\s+(?<table>\S+)\s+z dnia\s+(?<date>\d{2}-\d{2}-\d{4})",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private readonly IDeserializer _yamlDeserializer;
    private readonly JsonSerializerOptions _jsonSerializerOptions;

    private sealed record DescriptionItem(string Key, string? Value);

    public InvoiceInputService()
    {
        _yamlDeserializer = new DeserializerBuilder()
            .IgnoreUnmatchedProperties()
            .WithCaseInsensitivePropertyMatching()
            .Build();

        _jsonSerializerOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNameCaseInsensitive = true,
        };
    }

    public IReadOnlyList<InvoiceInputFile> DiscoverInputFiles(string invoicesRootPath)
    {
        if (!Directory.Exists(invoicesRootPath))
            return [];

        var files = Directory
            .EnumerateFiles(invoicesRootPath, "*.*", SearchOption.AllDirectories)
            .Where(path =>
            {
                string extension = Path.GetExtension(path);
                return extension.Equals(".xml", StringComparison.OrdinalIgnoreCase)
                    || extension.Equals(".yaml", StringComparison.OrdinalIgnoreCase)
                    || extension.Equals(".yml", StringComparison.OrdinalIgnoreCase);
            })
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .Select(path =>
            {
                string basePath = Path.Combine(
                    Path.GetDirectoryName(path) ?? string.Empty,
                    Path.GetFileNameWithoutExtension(path));

                return new InvoiceInputFile
                {
                    InputPath = path,
                    PdfPath = $"{basePath}.pdf",
                    SubmissionPath = $"{basePath}.ksef-submission.json",
                    Format = GetFormat(path),
                };
            })
            .ToList();

        var duplicateOutputs = files
            .GroupBy(file => file.PdfPath, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1)
            .ToList();

        if (duplicateOutputs.Count != 0)
        {
            string conflicts = string.Join(
                Environment.NewLine,
                duplicateOutputs.Select(group => $"  {group.Key}: {string.Join(", ", group.Select(item => item.InputPath))}"));

            throw new InvalidOperationException(
                $"Multiple invoice inputs map to the same PDF output path:{Environment.NewLine}{conflicts}");
        }

        return files;
    }

    public async Task<LoadedInvoiceSource> LoadAsync(InvoiceInputFile file, CancellationToken ct = default)
    {
        return file.Format switch
        {
            InvoiceSourceFormat.Xml => await LoadXmlAsync(file, ct),
            InvoiceSourceFormat.Yaml => await LoadYamlAsync(file, ct),
            _ => throw new InvalidOperationException($"Unsupported invoice format for '{file.InputPath}'."),
        };
    }

    public async Task<InvoiceSubmissionRecord?> TryLoadSubmissionAsync(InvoiceInputFile file, CancellationToken ct = default)
    {
        if (!File.Exists(file.SubmissionPath))
            return null;

        await using var stream = File.OpenRead(file.SubmissionPath);
        var record = await JsonSerializer.DeserializeAsync<InvoiceSubmissionRecord>(stream, _jsonSerializerOptions, ct);

        if (record == null)
            throw new InvalidOperationException($"Submission record '{file.SubmissionPath}' is empty or invalid.");

        ValidateInvoiceData(record.InvoiceData, file.InputPath);
        return record;
    }

    public async Task SaveSubmissionAsync(InvoiceInputFile file, InvoiceSubmissionRecord record, CancellationToken ct = default)
    {
        string directory = Path.GetDirectoryName(file.SubmissionPath)
            ?? throw new InvalidOperationException($"Cannot determine directory for '{file.SubmissionPath}'.");

        Directory.CreateDirectory(directory);

        await using var stream = File.Create(file.SubmissionPath);
        await JsonSerializer.SerializeAsync(stream, record, _jsonSerializerOptions, ct);
    }

    private async Task<LoadedInvoiceSource> LoadYamlAsync(InvoiceInputFile file, CancellationToken ct)
    {
        string yaml = await File.ReadAllTextAsync(file.InputPath, ct);
        var invoiceData = _yamlDeserializer.Deserialize<InvoiceData>(yaml)
            ?? throw new InvalidOperationException($"Could not deserialize YAML invoice '{file.InputPath}'.");

        ValidateInvoiceData(invoiceData, file.InputPath);

        return new LoadedInvoiceSource
        {
            File = file,
            InvoiceData = invoiceData,
        };
    }

    private static async Task<LoadedInvoiceSource> LoadXmlAsync(InvoiceInputFile file, CancellationToken ct)
    {
        string xml = await File.ReadAllTextAsync(file.InputPath, ct);
        var document = XDocument.Parse(xml, LoadOptions.PreserveWhitespace);
        var invoiceData = ParseInvoiceData(document, file.InputPath);

        return new LoadedInvoiceSource
        {
            File = file,
            InvoiceData = invoiceData,
            InvoiceXml = xml,
        };
    }

    private static InvoiceData ParseInvoiceData(XDocument document, string sourcePath)
    {
        var root = document.Root ?? throw new InvalidOperationException($"Invoice XML '{sourcePath}' does not have a root element.");
        XNamespace ns = root.Name.Namespace;

        var fa = root.Element(ns + "Fa")
            ?? throw new InvalidOperationException($"Invoice XML '{sourcePath}' does not contain a <Fa> element.");

        var seller = ParseParty(root.Element(ns + "Podmiot1"), ns, sourcePath, "Podmiot1");
        var buyer = ParseParty(root.Element(ns + "Podmiot2"), ns, sourcePath, "Podmiot2");

        var lineItems = fa.Elements(ns + "FaWiersz")
            .Select(item => ParseLineItem(item, ns, sourcePath))
            .ToList();

        var paymentElement = fa.Element(ns + "Platnosc");
        PaymentInfo? payment = null;
        if (paymentElement != null)
        {
            payment = new PaymentInfo
            {
                FormaPlatnosci = ParseRequiredInt(paymentElement, ns, "FormaPlatnosci", sourcePath),
                BankAccount = paymentElement
                    .Element(ns + "RachunekBankowy")
                    ?.Element(ns + "NrRB")
                    ?.Value,
            };
        }

        var additionalDescriptions = fa.Elements(ns + "DodatkowyOpis")
            .Select(element => new DescriptionItem(
                element.Element(ns + "Klucz")?.Value ?? string.Empty,
                element.Element(ns + "Wartosc")?.Value))
            .Where(item => !string.IsNullOrWhiteSpace(item.Key))
            .ToList();

        string? rateDescription = additionalDescriptions
            .FirstOrDefault(item => string.Equals(item.Key, "Kurs", StringComparison.OrdinalIgnoreCase))
            ?.Value;

        string issueDate = GetRequiredValue(fa, ns, "P_1", sourcePath);
        string saleDate = GetRequiredValue(fa, ns, "P_6", sourcePath);
        string currency = GetRequiredValue(fa, ns, "KodWaluty", sourcePath).ToUpperInvariant();

        var exchangeRate = ParseExchangeRate(fa, rateDescription, currency, saleDate);

        var invoiceData = new InvoiceData
        {
            InvoiceNumber = GetRequiredValue(fa, ns, "P_2", sourcePath),
            IssueDate = issueDate,
            SaleDate = saleDate == issueDate ? null : saleDate,
            Currency = currency,
            PlaceOfIssue = fa.Element(ns + "P_1M")?.Value,
            Seller = seller,
            Buyer = buyer,
            LineItems = lineItems,
            VatExemptionClause = fa
                .Element(ns + "Adnotacje")
                ?.Element(ns + "Zwolnienie")
                ?.Element(ns + "P_19A")
                ?.Value,
            Notes = additionalDescriptions
                .FirstOrDefault(item => string.Equals(item.Key, "Uwagi", StringComparison.OrdinalIgnoreCase))
                ?.Value,
            Payment = payment,
            ExchangeRate = exchangeRate,
        };

        ValidateInvoiceData(invoiceData, sourcePath);
        return invoiceData;
    }

    private static Party ParseParty(XElement? partyElement, XNamespace ns, string sourcePath, string elementName)
    {
        if (partyElement == null)
            throw new InvalidOperationException($"Invoice XML '{sourcePath}' does not contain <{elementName}>.");

        var identity = partyElement.Element(ns + "DaneIdentyfikacyjne")
            ?? throw new InvalidOperationException($"Invoice XML '{sourcePath}' does not contain <DaneIdentyfikacyjne> in <{elementName}>.");
        var address = partyElement.Element(ns + "Adres")
            ?? throw new InvalidOperationException($"Invoice XML '{sourcePath}' does not contain <Adres> in <{elementName}>.");

        string line1 = GetRequiredValue(address, ns, "AdresL1", sourcePath);
        string line2 = GetRequiredValue(address, ns, "AdresL2", sourcePath);
        var (postalCode, city) = SplitPostalCodeAndCity(line2);

        return new Party
        {
            Nip = identity.Element(ns + "NIP")?.Value,
            Name = GetRequiredValue(identity, ns, "Nazwa", sourcePath),
            Address = new Address
            {
                Street = line1,
                BuildingNumber = string.Empty,
                PostalCode = postalCode,
                City = city,
                CountryCode = GetRequiredValue(address, ns, "KodKraju", sourcePath),
            },
        };
    }

    private static InvoiceLineItem ParseLineItem(XElement item, XNamespace ns, string sourcePath)
    {
        string vatValue = GetRequiredValue(item, ns, "P_12", sourcePath);

        return new InvoiceLineItem
        {
            Description = GetRequiredValue(item, ns, "P_7", sourcePath),
            Unit = GetRequiredValue(item, ns, "P_8A", sourcePath),
            Quantity = ParseRequiredDecimal(item, ns, "P_8B", sourcePath),
            UnitPriceNet = ParseRequiredDecimal(item, ns, "P_9A", sourcePath),
            VatRate = string.Equals(vatValue, "zw", StringComparison.OrdinalIgnoreCase)
                ? 0
                : int.Parse(vatValue, CultureInfo.InvariantCulture),
        };
    }

    private static InvoiceExchangeRate? ParseExchangeRate(
        XElement fa,
        string? rateDescription,
        string currency,
        string saleDate)
    {
        if (string.Equals(currency, "PLN", StringComparison.OrdinalIgnoreCase))
            return null;

        string? rateValue = fa
            .Elements(fa.Name.Namespace + "FaWiersz")
            .Elements(fa.Name.Namespace + "KursWaluty")
            .Select(element => element.Value)
            .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));

        if (rateValue == null)
            return null;

        string table = "NBP";
        string effectiveDate = saleDate;

        if (rateDescription != null && TryParseExchangeRateDescription(rateDescription, out string? descriptionTable, out string? descriptionDate))
        {
            table = descriptionTable ?? table;
            effectiveDate = descriptionDate ?? effectiveDate;
        }

        return new InvoiceExchangeRate
        {
            Currency = currency.ToUpperInvariant(),
            Table = table,
            EffectiveDate = effectiveDate,
            Rate = decimal.Parse(rateValue, CultureInfo.InvariantCulture),
        };
    }

    private static bool TryParseExchangeRateDescription(string description, out string? table, out string? effectiveDate)
    {
        var match = ExchangeRateDescriptionRegex.Match(description);
        if (!match.Success)
        {
            table = null;
            effectiveDate = null;
            return false;
        }

        table = match.Groups["table"].Value;
        effectiveDate = DateOnly
            .ParseExact(match.Groups["date"].Value, "dd-MM-yyyy", CultureInfo.InvariantCulture)
            .ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        return true;
    }

    private static (string PostalCode, string City) SplitPostalCodeAndCity(string line)
    {
        int separatorIndex = line.IndexOf(' ');
        if (separatorIndex < 0)
            return (line, string.Empty);

        return (
            line[..separatorIndex],
            line[(separatorIndex + 1)..].Trim());
    }

    private static string GetRequiredValue(XElement element, XNamespace ns, string name, string sourcePath)
    {
        return element.Element(ns + name)?.Value
            ?? throw new InvalidOperationException($"Invoice XML '{sourcePath}' is missing <{name}>.");
    }

    private static decimal ParseRequiredDecimal(XElement element, XNamespace ns, string name, string sourcePath)
    {
        string value = GetRequiredValue(element, ns, name, sourcePath);
        return decimal.Parse(value, CultureInfo.InvariantCulture);
    }

    private static int ParseRequiredInt(XElement element, XNamespace ns, string name, string sourcePath)
    {
        string value = GetRequiredValue(element, ns, name, sourcePath);
        return int.Parse(value, CultureInfo.InvariantCulture);
    }

    private static InvoiceSourceFormat GetFormat(string path)
    {
        string extension = Path.GetExtension(path);

        if (extension.Equals(".xml", StringComparison.OrdinalIgnoreCase))
            return InvoiceSourceFormat.Xml;

        return InvoiceSourceFormat.Yaml;
    }

    private static void ValidateInvoiceData(InvoiceData data, string sourcePath)
    {
        if (string.IsNullOrWhiteSpace(data.InvoiceNumber))
            throw new InvalidOperationException($"Invoice '{sourcePath}' is missing InvoiceNumber.");

        InvoiceMath.ParseDate(data.IssueDate, $"{sourcePath} IssueDate");

        if (!string.IsNullOrWhiteSpace(data.SaleDate))
            InvoiceMath.ParseDate(data.SaleDate, $"{sourcePath} SaleDate");

        if (data.Seller == null)
            throw new InvalidOperationException($"Invoice '{sourcePath}' is missing Seller.");

        if (data.Buyer == null)
            throw new InvalidOperationException($"Invoice '{sourcePath}' is missing Buyer.");

        if (data.LineItems == null || data.LineItems.Count == 0)
            throw new InvalidOperationException($"Invoice '{sourcePath}' must contain at least one line item.");

    }
}
