using System.Globalization;
using KSeFInvoice;

namespace KSeFInvoice.Tests;

public sealed class InvoiceInputServiceTests
{
    [Fact]
    public void DiscoverInputFiles_ReturnsSupportedFilesWithDerivedOutputsInStableOrder()
    {
        using var temp = TempDirectory.Create();
        string nested = Path.Combine(temp.Path, "Nested");
        Directory.CreateDirectory(nested);
        string yamlPath = Path.Combine(temp.Path, "b.yaml");
        string xmlPath = Path.Combine(nested, "a.xml");
        File.WriteAllText(yamlPath, "placeholder");
        File.WriteAllText(xmlPath, "placeholder");
        File.WriteAllText(Path.Combine(temp.Path, "ignore.txt"), "placeholder");

        var files = new InvoiceInputService().DiscoverInputFiles(temp.Path);

        Assert.Collection(
            files,
            file =>
            {
                Assert.Equal(yamlPath, file.InputPath);
                Assert.Equal(Path.Combine(temp.Path, "b.pdf"), file.PdfPath);
                Assert.Equal(Path.Combine(temp.Path, "b.ksef-submission.json"), file.SubmissionPath);
                Assert.Equal(InvoiceSourceFormat.Yaml, file.Format);
            },
            file =>
            {
                Assert.Equal(xmlPath, file.InputPath);
                Assert.Equal(Path.Combine(nested, "a.pdf"), file.PdfPath);
                Assert.Equal(Path.Combine(nested, "a.ksef-submission.json"), file.SubmissionPath);
                Assert.Equal(InvoiceSourceFormat.Xml, file.Format);
            });
    }

    [Fact]
    public void DiscoverInputFiles_DuplicateOutputNames_Throws()
    {
        using var temp = TempDirectory.Create();
        File.WriteAllText(Path.Combine(temp.Path, "invoice.xml"), "<xml />");
        File.WriteAllText(Path.Combine(temp.Path, "invoice.yaml"), "InvoiceNumber: F/1");

        var exception = Assert.Throws<InvalidOperationException>(() => new InvoiceInputService().DiscoverInputFiles(temp.Path));

        Assert.Contains("Multiple invoice inputs map to the same PDF output path", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task LoadAsync_YamlInput_DeserializesAndValidatesInvoice()
    {
        using var temp = TempDirectory.Create();
        string yamlPath = Path.Combine(temp.Path, "invoice.yaml");
        await File.WriteAllTextAsync(yamlPath, TestInvoices.CreateYaml());
        var file = new InvoiceInputFile
        {
            InputPath = yamlPath,
            PdfPath = Path.ChangeExtension(yamlPath, ".pdf"),
            SubmissionPath = Path.ChangeExtension(yamlPath, ".ksef-submission.json"),
            Format = InvoiceSourceFormat.Yaml,
        };

        var loaded = await new InvoiceInputService().LoadAsync(file);

        Assert.Same(file, loaded.File);
        Assert.Null(loaded.InvoiceXml);
        Assert.Equal("F/01/07/2026", loaded.InvoiceData.InvoiceNumber);
        Assert.Equal("USD", loaded.InvoiceData.Currency);
        Assert.Equal("Overclock Labs, Inc", loaded.InvoiceData.Buyer.Name);
        Assert.Equal(100m, loaded.InvoiceData.LineItems.Single().UnitPriceNet);
    }

    [Fact]
    public async Task LoadAsync_XmlInput_ParsesInvoiceDetailsAndExchangeRate()
    {
        using var temp = TempDirectory.Create();
        var invoice = TestInvoices.Create(currency: "USD", issueDate: "2026-07-11", saleDate: "2026-07-10");
        invoice.Notes = "Handle <carefully>";
        invoice.ExchangeRate = new InvoiceExchangeRate
        {
            Currency = "USD",
            Table = "001/A/NBP/2026",
            EffectiveDate = "2026-07-09",
            Rate = 3.7692m,
        };

        string xml = InvoiceXmlBuilder.Build(invoice);
        string xmlPath = Path.Combine(temp.Path, "invoice.xml");
        await File.WriteAllTextAsync(xmlPath, xml);
        var file = new InvoiceInputFile
        {
            InputPath = xmlPath,
            PdfPath = Path.ChangeExtension(xmlPath, ".pdf"),
            SubmissionPath = Path.ChangeExtension(xmlPath, ".ksef-submission.json"),
            Format = InvoiceSourceFormat.Xml,
        };

        var loaded = await new InvoiceInputService().LoadAsync(file);

        Assert.Equal(xml, loaded.InvoiceXml);
        Assert.Equal("F/01/07/2026", loaded.InvoiceData.InvoiceNumber);
        Assert.Equal("USD", loaded.InvoiceData.Currency);
        Assert.Equal("2026-07-10", loaded.InvoiceData.SaleDate);
        Assert.Equal("Handle <carefully>", loaded.InvoiceData.Notes);
        Assert.NotNull(loaded.InvoiceData.ExchangeRate);
        Assert.Equal("001/A/NBP/2026", loaded.InvoiceData.ExchangeRate.Table);
        Assert.Equal("2026-07-09", loaded.InvoiceData.ExchangeRate.EffectiveDate);
        Assert.Equal(3.7692m, loaded.InvoiceData.ExchangeRate.Rate);
    }

    [Fact]
    public async Task SaveAndTryLoadSubmissionAsync_RoundTripsSubmissionRecord()
    {
        using var temp = TempDirectory.Create();
        string inputPath = Path.Combine(temp.Path, "invoice.yaml");
        var file = new InvoiceInputFile
        {
            InputPath = inputPath,
            PdfPath = Path.ChangeExtension(inputPath, ".pdf"),
            SubmissionPath = Path.ChangeExtension(inputPath, ".ksef-submission.json"),
            Format = InvoiceSourceFormat.Yaml,
        };
        var record = new InvoiceSubmissionRecord
        {
            SourcePath = inputPath,
            SourceFormat = "yaml",
            ReferenceNumber = "reference",
            VerificationUrl = "https://example.test/verify",
            KsefNumber = "123-KSEF",
            PollingError = null,
            SubmittedAtUtc = "2026-07-10T12:00:00Z",
            InvoiceData = TestInvoices.Create(),
        };
        var service = new InvoiceInputService();

        await service.SaveSubmissionAsync(file, record);
        var loaded = await service.TryLoadSubmissionAsync(file);

        Assert.NotNull(loaded);
        Assert.Equal("reference", loaded.ReferenceNumber);
        Assert.Equal("123-KSEF", loaded.KsefNumber);
        Assert.Equal("F/01/07/2026", loaded.InvoiceData.InvoiceNumber);
    }
}
