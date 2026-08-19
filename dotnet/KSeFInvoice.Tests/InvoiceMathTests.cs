using KSeFInvoice;

namespace KSeFInvoice.Tests;

public sealed class InvoiceMathTests
{
    [Fact]
    public void LineAndInvoiceTotals_UseUnroundedDecimalMath()
    {
        var invoice = TestInvoices.Create(lineItems:
        [
            TestInvoices.Line(quantity: 2.5m, unitPriceNet: 10.10m, vatRate: 23),
            TestInvoices.Line(quantity: 3m, unitPriceNet: 7.25m, vatRate: 0),
        ]);

        Assert.Equal(25.25m, InvoiceMath.GetLineNet(invoice.LineItems[0]));
        Assert.Equal(5.8075m, InvoiceMath.GetLineVat(invoice.LineItems[0]));
        Assert.Equal(31.0575m, InvoiceMath.GetLineGross(invoice.LineItems[0]));
        Assert.Equal(0m, InvoiceMath.GetLineVat(invoice.LineItems[1]));

        var totals = InvoiceMath.GetTotals(invoice);
        Assert.Equal(47.00m, totals.Net);
        Assert.Equal(5.8075m, totals.Vat);
        Assert.Equal(52.8075m, totals.Gross);
    }

    [Fact]
    public void GroupByVatRate_AggregatesEachRate()
    {
        var invoice = TestInvoices.Create(lineItems:
        [
            TestInvoices.Line(quantity: 1m, unitPriceNet: 100m, vatRate: 23),
            TestInvoices.Line(quantity: 2m, unitPriceNet: 50m, vatRate: 23),
            TestInvoices.Line(quantity: 1m, unitPriceNet: 75m, vatRate: 8),
            TestInvoices.Line(quantity: 1m, unitPriceNet: 20m, vatRate: 0),
        ]);

        var groups = InvoiceMath.GroupByVatRate(invoice);

        Assert.Equal((Net: 200m, Vat: 46m, Gross: 246m), groups[23]);
        Assert.Equal((Net: 75m, Vat: 6m, Gross: 81m), groups[8]);
        Assert.Equal((Net: 20m, Vat: 0m, Gross: 20m), groups[0]);
    }

    [Theory]
    [InlineData(1234.5, "1234.50")]
    [InlineData(1234.567, "1234.57")]
    public void FormatDecimal_UsesInvariantTwoDecimalPlaces(decimal value, string expected)
    {
        Assert.Equal(expected, InvoiceMath.FormatDecimal(value));
    }

    [Theory]
    [InlineData(1, "1")]
    [InlineData(1.23456, "1.2346")]
    [InlineData(1.2300, "1.23")]
    public void FormatQuantity_TrimsToFourOptionalDecimals(decimal value, string expected)
    {
        Assert.Equal(expected, InvoiceMath.FormatQuantity(value));
    }

    [Theory]
    [InlineData(1234.5, "1 234,50")]
    [InlineData(1234567.891, "1 234 567,89")]
    public void FormatPolishAmount_UsesSpacesAndComma(decimal value, string expected)
    {
        Assert.Equal(expected, InvoiceMath.FormatPolishAmount(value));
    }

    [Theory]
    [InlineData(3.7, "3,7")]
    [InlineData(3.76920, "3,7692")]
    public void FormatPolishRate_UsesCommaAndOptionalDecimals(decimal value, string expected)
    {
        Assert.Equal(expected, InvoiceMath.FormatPolishRate(value));
    }

    [Theory]
    [InlineData(null, "PLN")]
    [InlineData("", "PLN")]
    [InlineData(" usd ", "USD")]
    public void GetCurrency_DefaultsAndNormalizes(string? value, string expected)
    {
        Assert.Equal(expected, InvoiceMath.GetCurrency(TestInvoices.Create(currency: value)));
    }

    [Fact]
    public void ParseDate_AcceptsIsoDateAndRejectsOtherFormats()
    {
        Assert.Equal(new DateOnly(2026, 7, 10), InvoiceMath.ParseDate("2026-07-10", "IssueDate"));

        var exception = Assert.Throws<InvalidOperationException>(() => InvoiceMath.ParseDate("10-07-2026", "IssueDate"));
        Assert.Contains("IssueDate must use yyyy-MM-dd format", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void SaleDateAndJoinNonEmpty_ApplyFallbacks()
    {
        Assert.Equal("2026-07-10", InvoiceMath.GetSaleDate(TestInvoices.Create(issueDate: "2026-07-10", saleDate: null)));
        Assert.Equal("Street 7 00-001 Warsaw", InvoiceMath.JoinNonEmpty("Street", null, "7", "", "00-001", "Warsaw"));
    }
}
