using System.Xml.Linq;
using KSeFInvoice;

namespace KSeFInvoice.Tests;

public sealed class InvoiceXmlExchangeRateUpdaterTests
{
    [Fact]
    public void Apply_PlnInvoice_ReturnsOriginalXml()
    {
        var invoice = TestInvoices.Create(currency: "PLN");
        string xml = InvoiceXmlBuilder.Build(invoice);

        string updated = InvoiceXmlExchangeRateUpdater.Apply(xml, invoice);

        Assert.Equal(xml, updated);
    }

    [Fact]
    public void Apply_ForeignCurrency_InsertsLineRatesAndDescriptions()
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
        string xml = InvoiceXmlBuilder.Build(TestInvoices.Create(currency: "USD", saleDate: "2026-07-10", lineItems:
        [
            TestInvoices.Line(quantity: 1m, unitPriceNet: 100m, vatRate: 0),
        ]));

        XDocument document = XDocument.Parse(InvoiceXmlExchangeRateUpdater.Apply(xml, invoice));
        XNamespace ns = document.Root!.Name.Namespace;
        XElement fa = document.Root.Element(ns + "Fa")!;

        Assert.Equal("3.7692", fa.Element(ns + "FaWiersz")!.Element(ns + "KursWaluty")!.Value);
        Assert.Equal("376,92 PLN (100,00 USD)", DescriptionValue(fa, ns, "Brutto"));
        Assert.Contains("tabela kursów średnich NBP 001/A/NBP/2026", DescriptionValue(fa, ns, "Kurs"), StringComparison.Ordinal);
    }

    [Fact]
    public void Apply_ForeignCurrency_UpdatesExistingRateDescriptions()
    {
        var invoice = TestInvoices.Create(currency: "EUR", saleDate: "2026-07-10", lineItems:
        [
            TestInvoices.Line(quantity: 2m, unitPriceNet: 10m, vatRate: 0),
        ]);
        invoice.ExchangeRate = new InvoiceExchangeRate
        {
            Currency = "EUR",
            Table = "002/A/NBP/2026",
            EffectiveDate = "2026-07-08",
            Rate = 4.25m,
        };

        string xml = """
          <Faktura xmlns="http://crd.gov.pl/wzor/2025/06/25/13775/">
            <Fa>
              <RodzajFaktury>VAT</RodzajFaktury>
              <DodatkowyOpis><Klucz>Brutto</Klucz><Wartosc>old</Wartosc></DodatkowyOpis>
              <DodatkowyOpis><Klucz>Kurs</Klucz></DodatkowyOpis>
              <FaWiersz>
                <P_12>zw</P_12>
                <KursWaluty>1.1</KursWaluty>
              </FaWiersz>
            </Fa>
          </Faktura>
          """;

        XDocument document = XDocument.Parse(InvoiceXmlExchangeRateUpdater.Apply(xml, invoice));
        XNamespace ns = document.Root!.Name.Namespace;
        XElement fa = document.Root.Element(ns + "Fa")!;

        Assert.Equal("4.25", fa.Element(ns + "FaWiersz")!.Element(ns + "KursWaluty")!.Value);
        Assert.Equal("85,00 PLN (20,00 EUR)", DescriptionValue(fa, ns, "Brutto"));
        Assert.Contains("1 EUR = 4,25 PLN", DescriptionValue(fa, ns, "Kurs"), StringComparison.Ordinal);
    }

    private static string DescriptionValue(XElement fa, XNamespace ns, string key)
    {
        return fa.Elements(ns + "DodatkowyOpis")
            .Single(element => element.Element(ns + "Klucz")?.Value == key)
            .Element(ns + "Wartosc")!
            .Value;
    }
}
