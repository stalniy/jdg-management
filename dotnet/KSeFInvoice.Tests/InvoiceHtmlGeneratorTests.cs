using KSeFInvoice;

namespace KSeFInvoice.Tests;

public sealed class InvoiceHtmlGeneratorTests
{
    [Fact]
    public void Generate_RendersEscapedInvoiceFieldsTotalsAndOptionalKsefData()
    {
        var invoice = TestInvoices.Create(
            invoiceNumber: "F/<1>/2026",
            placeOfIssue: "Warsaw & Krakow",
            notes: "Do <not> inline",
            lineItems:
            [
                TestInvoices.Line(description: "Support & maintenance", quantity: 2m, unitPriceNet: 100m, vatRate: 23),
            ]);

        string html = InvoiceHtmlGenerator.Generate(invoice, new InvoiceHtmlOptions
        {
            KsefNumber = "123-KSEF",
            VerificationUrl = "https://example.test/verify?invoice=1&mode=full",
            QrCodeDataUrl = "data:image/png;base64,abc",
        });

        Assert.Contains("<title>Faktura F/&lt;1&gt;/2026</title>", html, StringComparison.Ordinal);
        Assert.Contains("Support &amp; maintenance", html, StringComparison.Ordinal);
        Assert.Contains("Do &lt;not&gt; inline", html, StringComparison.Ordinal);
        Assert.Contains("Warsaw &amp; Krakow", html, StringComparison.Ordinal);
        Assert.Contains("Numer KSeF: 123-KSEF", html, StringComparison.Ordinal);
        Assert.Contains("Kwota należności ogółem: 246,00 PLN", html, StringComparison.Ordinal);
        Assert.Contains("https://example.test/verify?invoice=1&amp;mode=full", html, StringComparison.Ordinal);
    }

    [Fact]
    public void Generate_ForeignCurrency_RendersExchangeRateBlock()
    {
        var invoice = TestInvoices.Create(currency: "USD", saleDate: "2026-07-10");
        invoice.ExchangeRate = new InvoiceExchangeRate
        {
            Currency = "USD",
            Table = "001/A/NBP/2026",
            EffectiveDate = "2026-07-09",
            Rate = 3.7692m,
        };

        string html = InvoiceHtmlGenerator.Generate(invoice);

        Assert.Contains("1 USD = 3,7692 PLN", html, StringComparison.Ordinal);
        Assert.Contains("z dnia 09-07-2026", html, StringComparison.Ordinal);
        Assert.Contains("Kwota brutto po przeliczeniu: 463,61 PLN (123,00 USD)", html, StringComparison.Ordinal);
    }
}
