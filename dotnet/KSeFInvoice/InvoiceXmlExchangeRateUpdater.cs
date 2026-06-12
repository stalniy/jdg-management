using System.Globalization;
using System.Xml.Linq;

namespace KSeFInvoice;

public static class InvoiceXmlExchangeRateUpdater
{
    public static string Apply(string xml, InvoiceData invoiceData)
    {
        if (invoiceData.ExchangeRate == null || string.Equals(InvoiceMath.GetCurrency(invoiceData), "PLN", StringComparison.OrdinalIgnoreCase))
            return xml;

        var document = XDocument.Parse(xml, LoadOptions.PreserveWhitespace);
        var root = document.Root ?? throw new InvalidOperationException("Invoice XML does not have a root element.");
        XNamespace ns = root.Name.Namespace;

        var fa = root.Element(ns + "Fa")
            ?? throw new InvalidOperationException("Invoice XML does not contain a <Fa> element.");

        UpdateLineItemExchangeRates(fa, ns, invoiceData.ExchangeRate.Rate);
        UpsertAdditionalDescription(fa, ns, "Brutto", BuildGrossDescription(invoiceData));
        UpsertAdditionalDescription(fa, ns, "Kurs", BuildRateDescription(invoiceData));

        return document.ToString(SaveOptions.DisableFormatting);
    }

    private static void UpdateLineItemExchangeRates(XElement fa, XNamespace ns, decimal rate)
    {
        string formattedRate = rate.ToString("0.####", CultureInfo.InvariantCulture);

        foreach (var lineItem in fa.Elements(ns + "FaWiersz"))
        {
            var rateElement = lineItem.Element(ns + "KursWaluty");
            if (rateElement != null)
            {
                rateElement.Value = formattedRate;
                continue;
            }

            var vatElement = lineItem.Element(ns + "P_12");
            if (vatElement != null)
            {
                vatElement.AddAfterSelf(new XElement(ns + "KursWaluty", formattedRate));
                continue;
            }

            lineItem.Add(new XElement(ns + "KursWaluty", formattedRate));
        }
    }

    private static void UpsertAdditionalDescription(XElement fa, XNamespace ns, string key, string value)
    {
        var matchingDescription = fa.Elements(ns + "DodatkowyOpis")
            .FirstOrDefault(element => string.Equals(element.Element(ns + "Klucz")?.Value, key, StringComparison.OrdinalIgnoreCase));

        if (matchingDescription != null)
        {
            var valueElement = matchingDescription.Element(ns + "Wartosc");
            if (valueElement == null)
            {
                matchingDescription.Add(new XElement(ns + "Wartosc", value));
                return;
            }

            valueElement.Value = value;
            return;
        }

        var newDescription = new XElement(ns + "DodatkowyOpis",
            new XElement(ns + "Klucz", key),
            new XElement(ns + "Wartosc", value));

        var lastDescription = fa.Elements(ns + "DodatkowyOpis").LastOrDefault();
        if (lastDescription != null)
        {
            lastDescription.AddAfterSelf(newDescription);
            return;
        }

        var invoiceType = fa.Element(ns + "RodzajFaktury");
        if (invoiceType != null)
        {
            invoiceType.AddAfterSelf(newDescription);
            return;
        }

        fa.Add(newDescription);
    }

    private static string BuildGrossDescription(InvoiceData invoiceData)
    {
        if (invoiceData.ExchangeRate == null)
            throw new InvalidOperationException("Exchange rate is required to build gross description.");

        var totals = InvoiceMath.GetTotals(invoiceData);
        string currency = InvoiceMath.GetCurrency(invoiceData);
        decimal totalGrossPln = totals.Gross * invoiceData.ExchangeRate.Rate;
        string foreignAmount = $"{InvoiceMath.FormatPolishAmount(totals.Gross)} {currency}";
        string plnAmount = $"{InvoiceMath.FormatPolishAmount(totalGrossPln)} PLN";

        return $"{plnAmount} ({foreignAmount})";
    }

    private static string BuildRateDescription(InvoiceData invoiceData)
    {
        if (invoiceData.ExchangeRate == null)
            throw new InvalidOperationException("Exchange rate is required to build rate description.");

        var totals = InvoiceMath.GetTotals(invoiceData);
        string currency = InvoiceMath.GetCurrency(invoiceData);
        decimal totalGrossPln = totals.Gross * invoiceData.ExchangeRate.Rate;
        string plnAmount = $"{InvoiceMath.FormatPolishAmount(totalGrossPln)} PLN";
        DateOnly effectiveDate = InvoiceMath.ParseDate(invoiceData.ExchangeRate.EffectiveDate, "ExchangeRate.EffectiveDate");

        return $"Przeliczono po kursie 1 {currency} = {InvoiceMath.FormatPolishRate(invoiceData.ExchangeRate.Rate)} PLN, tabela kursów średnich NBP {invoiceData.ExchangeRate.Table} z dnia {effectiveDate:dd-MM-yyyy}; Przeliczona wartość: {plnAmount}";
    }
}
