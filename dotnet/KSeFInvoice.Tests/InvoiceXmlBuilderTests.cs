using System.Xml.Linq;
using KSeFInvoice;

namespace KSeFInvoice.Tests;

public sealed class InvoiceXmlBuilderTests
{
    [Fact]
    public void Build_PlnInvoice_ContainsCoreFieldsVatSummaryAndEscapedValues()
    {
        var invoice = TestInvoices.Create(
            invoiceNumber: "F/&/2026",
            placeOfIssue: "Kraków & Warsaw",
            notes: "Use <safe> handling",
            lineItems:
            [
                TestInvoices.Line(description: "Consulting & support", quantity: 2m, unitPriceNet: 100m, vatRate: 23),
                TestInvoices.Line(description: "Training", quantity: 1m, unitPriceNet: 50m, vatRate: 8),
            ]);

        XDocument document = XDocument.Parse(InvoiceXmlBuilder.Build(invoice));
        XNamespace ns = document.Root!.Name.Namespace;
        XElement fa = document.Root.Element(ns + "Fa")!;

        Assert.Equal("PLN", fa.Element(ns + "KodWaluty")!.Value);
        Assert.Equal("F/&/2026", fa.Element(ns + "P_2")!.Value);
        Assert.Equal("Kraków & Warsaw", fa.Element(ns + "P_1M")!.Value);
        Assert.Equal("200.00", fa.Element(ns + "P_13_1")!.Value);
        Assert.Equal("46.00", fa.Element(ns + "P_14_1")!.Value);
        Assert.Equal("50.00", fa.Element(ns + "P_13_3")!.Value);
        Assert.Equal("4.00", fa.Element(ns + "P_14_3")!.Value);
        Assert.Equal("300.00", fa.Element(ns + "P_15")!.Value);
        Assert.Equal("Use <safe> handling", DescriptionValue(fa, ns, "Uwagi"));
    }

    [Fact]
    public void Build_ZeroVatAndExemption_UsesExpectedKsefElements()
    {
        var invoice = TestInvoices.Create(
            vatExemptionClause: "art. 43",
            lineItems: [TestInvoices.Line(unitPriceNet: 125m, vatRate: 0)]);

        XDocument document = XDocument.Parse(InvoiceXmlBuilder.Build(invoice));
        XNamespace ns = document.Root!.Name.Namespace;
        XElement fa = document.Root.Element(ns + "Fa")!;

        Assert.Equal("125.00", fa.Element(ns + "P_13_7")!.Value);
        Assert.Equal("art. 43", fa.Element(ns + "Adnotacje")!.Element(ns + "Zwolnienie")!.Element(ns + "P_19A")!.Value);
        Assert.Equal("zw", fa.Element(ns + "FaWiersz")!.Element(ns + "P_12")!.Value);
    }

    [Fact]
    public void Build_ForeignCurrency_AddsExchangeRateDescriptionsAndLineRates()
    {
        var invoice = TestInvoices.Create(currency: "USD", saleDate: "2026-07-10", lineItems:
        [
            TestInvoices.Line(quantity: 1m, unitPriceNet: 100m, vatRate: 0),
        ]);
        invoice.ExchangeRate = new InvoiceExchangeRate
        {
            Currency = "USD",
            Table = "001/A/NBP/2026",
            EffectiveDate = "2026-07-09",
            Rate = 3.7692m,
        };

        XDocument document = XDocument.Parse(InvoiceXmlBuilder.Build(invoice));
        XNamespace ns = document.Root!.Name.Namespace;
        XElement fa = document.Root.Element(ns + "Fa")!;

        Assert.Equal("3.7692", fa.Element(ns + "FaWiersz")!.Element(ns + "KursWaluty")!.Value);
        Assert.Equal("376,92 PLN (100,00 USD)", DescriptionValue(fa, ns, "Brutto"));
        Assert.Contains("1 USD = 3,7692 PLN", DescriptionValue(fa, ns, "Kurs"), StringComparison.Ordinal);
        Assert.Contains("z dnia 09-07-2026", DescriptionValue(fa, ns, "Kurs"), StringComparison.Ordinal);
    }

    private static string DescriptionValue(XElement fa, XNamespace ns, string key)
    {
        return fa.Elements(ns + "DodatkowyOpis")
            .Single(element => element.Element(ns + "Klucz")?.Value == key)
            .Element(ns + "Wartosc")!
            .Value;
    }
}
