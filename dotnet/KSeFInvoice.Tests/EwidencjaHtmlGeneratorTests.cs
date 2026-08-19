using KSeFInvoice;

namespace KSeFInvoice.Tests;

public sealed class EwidencjaHtmlGeneratorTests
{
    [Fact]
    public void Generate_RendersEscapedRowsAndTotals()
    {
        var document = new EwidencjaDocument
        {
            Month = new DateOnly(2026, 7, 1),
            Entries =
            [
                new EwidencjaEntry
                {
                    Lp = 1,
                    EntryDate = new DateOnly(2026, 7, 10),
                    RevenueDate = new DateOnly(2026, 7, 10),
                    KsefNumber = "123<KSEF>",
                    AccountingDocumentNumber = "F/01/07/2026",
                    ContractorTaxIdentifier = "US",
                    RyczaltRate = 12m,
                    RevenuePln = 1234.56m,
                    Notes = "USŁUGI <PROGRAMISTYCZNE>",
                    SourceBaseName = "f-01-07-2026",
                },
            ],
            TotalsByRate = EwidencjaRates.OrderedRates.ToDictionary(rate => rate, rate => rate == 12m ? 1234.56m : 0m),
            TotalRevenue = 1234.56m,
        };

        string html = EwidencjaHtmlGenerator.Generate(document);

        Assert.Contains("<title>Ewidencja przychodów 2026-07</title>", html, StringComparison.Ordinal);
        Assert.Contains("123&lt;KSEF&gt;", html, StringComparison.Ordinal);
        Assert.Contains("USŁUGI &lt;PROGRAMISTYCZNE&gt;", html, StringComparison.Ordinal);
        Assert.Contains("1 234,56", html, StringComparison.Ordinal);
    }
}
