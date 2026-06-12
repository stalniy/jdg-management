using System.Globalization;
using System.Security;
using System.Text;

namespace KSeFInvoice;

public static class InvoiceXmlBuilder
{
    public static string Build(InvoiceData data)
    {
        var totals = InvoiceMath.GetTotals(data);
        var vatGroups = InvoiceMath.GroupByVatRate(data);
        string currency = InvoiceMath.GetCurrency(data);
        string saleDate = InvoiceMath.GetSaleDate(data);

        // VAT summary XML
        var vatSummary = new StringBuilder();
        foreach (var (rate, values) in vatGroups.OrderByDescending(item => item.Key))
        {
            decimal net = values.Net;
            decimal vat = values.Vat;

            if (rate == 23)
            {
                vatSummary.AppendLine($"      <P_13_1>{Fmt(net)}</P_13_1>");
                vatSummary.AppendLine($"      <P_14_1>{Fmt(vat)}</P_14_1>");
            }
            else if (rate == 8)
            {
                vatSummary.AppendLine($"      <P_13_3>{Fmt(net)}</P_13_3>");
                vatSummary.AppendLine($"      <P_14_3>{Fmt(vat)}</P_14_3>");
            }
            else if (rate == 5)
            {
                vatSummary.AppendLine($"      <P_13_5>{Fmt(net)}</P_13_5>");
                vatSummary.AppendLine($"      <P_14_5>{Fmt(vat)}</P_14_5>");
            }
            else if (rate == 0)
            {
                vatSummary.AppendLine($"      <P_13_7>{Fmt(net)}</P_13_7>");
            }
        }

        string placeXml = data.PlaceOfIssue != null
            ? $"\n      <P_1M>{Esc(data.PlaceOfIssue)}</P_1M>"
            : "";

        string zwolnienieXml = data.VatExemptionClause != null
            ? $"<P_19>1</P_19>\n           <P_19A>{Esc(data.VatExemptionClause)}</P_19A>"
            : "<P_19N>1</P_19N>";

        string notesXml = BuildAdditionalDescriptionsXml(data, currency, totals.Gross);

        string paymentXml = "";
        if (data.Payment != null)
        {
            var bankPart = data.Payment.BankAccount != null
                ? $"\n         <RachunekBankowy>\n             <NrRB>{Esc(data.Payment.BankAccount)}</NrRB>\n         </RachunekBankowy>"
                : "";
            paymentXml = $"\n      <Platnosc>\n         <FormaPlatnosci>{data.Payment.FormaPlatnosci}</FormaPlatnosci>{bankPart}\n      </Platnosc>";
        }

        // Line items
        var lineItemsXml = new StringBuilder();
        for (int i = 0; i < data.LineItems.Count; i++)
        {
            lineItemsXml.Append(BuildLineItem(data.LineItems[i], i + 1, data.ExchangeRate?.Rate));
        }

        return $@"<?xml version=""1.0"" encoding=""UTF-8""?>
<Faktura xmlns:etd=""http://crd.gov.pl/xml/schematy/dziedzinowe/mf/2022/01/05/eD/DefinicjeTypy/"" xmlns:xsi=""http://www.w3.org/2001/XMLSchema-instance"" xmlns=""http://crd.gov.pl/wzor/2025/06/25/13775/"">
   <Naglowek>
      <KodFormularza kodSystemowy=""FA (3)"" wersjaSchemy=""1-0E"">FA</KodFormularza>
      <WariantFormularza>3</WariantFormularza>
      <DataWytworzeniaFa>{data.IssueDate}T00:00:00Z</DataWytworzeniaFa>
      <SystemInfo>ksef-client-csharp</SystemInfo>
   </Naglowek>
   {BuildSellerXml(data.Seller)}
   {BuildBuyerXml(data.Buyer)}
   <Fa>
      <KodWaluty>{currency}</KodWaluty>
      <P_1>{data.IssueDate}</P_1>{placeXml}
      <P_2>{Esc(data.InvoiceNumber)}</P_2>
      <P_6>{saleDate}</P_6>
      {vatSummary.ToString().TrimEnd()}
      <P_15>{Fmt(totals.Gross)}</P_15>
      <Adnotacje>
         <P_16>2</P_16>
         <P_17>2</P_17>
         <P_18>2</P_18>
         <P_18A>2</P_18A>
         <Zwolnienie>
           {zwolnienieXml}
         </Zwolnienie>
         <NoweSrodkiTransportu>
           <P_22N>1</P_22N>
         </NoweSrodkiTransportu>
         <P_23>2</P_23>
         <PMarzy>
           <P_PMarzyN>1</P_PMarzyN>
         </PMarzy>
      </Adnotacje>
      <RodzajFaktury>VAT</RodzajFaktury>{notesXml}
      {lineItemsXml.ToString().TrimEnd()}{paymentXml}
   </Fa>
</Faktura>";
    }

    private static string BuildAdditionalDescriptionsXml(InvoiceData data, string currency, decimal totalGross)
    {
        var descriptions = new StringBuilder();

        if (!string.IsNullOrWhiteSpace(data.Notes))
            descriptions.Append(BuildDescription("Uwagi", data.Notes));

        if (data.ExchangeRate != null && !string.Equals(currency, "PLN", StringComparison.OrdinalIgnoreCase))
        {
            decimal totalGrossPln = totalGross * data.ExchangeRate.Rate;
            string foreignAmount = $"{InvoiceMath.FormatPolishAmount(totalGross)} {currency}";
            string plnAmount = $"{InvoiceMath.FormatPolishAmount(totalGrossPln)} PLN";
            DateOnly effectiveDate = InvoiceMath.ParseDate(data.ExchangeRate.EffectiveDate, "ExchangeRate.EffectiveDate");

            descriptions.Append(BuildDescription("Brutto", $"{plnAmount} ({foreignAmount})"));
            descriptions.Append(BuildDescription(
                "Kurs",
                $"Przeliczono po kursie 1 {currency} = {InvoiceMath.FormatPolishRate(data.ExchangeRate.Rate)} PLN, tabela kursów średnich NBP {data.ExchangeRate.Table} z dnia {effectiveDate:dd-MM-yyyy}; Przeliczona wartość: {plnAmount}"));
        }

        return descriptions.ToString();
    }

    private static string BuildDescription(string key, string value)
    {
        return $"\n      <DodatkowyOpis>\n       <Klucz>{Esc(key)}</Klucz>\n       <Wartosc>{Esc(value)}</Wartosc>\n      </DodatkowyOpis>";
    }

    private static string BuildLineItem(InvoiceLineItem item, int index, decimal? exchangeRate)
    {
        decimal netValue = InvoiceMath.GetLineNet(item);
        string vatLabel = item.VatRate == 0 ? "zw" : item.VatRate.ToString(CultureInfo.InvariantCulture);
        string exchangeRateXml = exchangeRate.HasValue
            ? $"\n        <KursWaluty>{FmtRate(exchangeRate.Value)}</KursWaluty>"
            : "";

        return $@"
      <FaWiersz>
        <NrWierszaFa>{index}</NrWierszaFa>
        <P_7>{Esc(item.Description)}</P_7>
        <P_8A>{Esc(item.Unit)}</P_8A>
        <P_8B>{InvoiceMath.FormatQuantity(item.Quantity)}</P_8B>
        <P_9A>{Fmt(item.UnitPriceNet)}</P_9A>
        <P_11>{Fmt(netValue)}</P_11>
        <P_12>{vatLabel}</P_12>
        {exchangeRateXml}
      </FaWiersz>";
    }

    private static string BuildPartyXml(Party party)
    {
        string countryCode = party.Address.CountryCode ?? "PL";
        string addressLine1 = InvoiceMath.JoinNonEmpty(party.Address.Street, party.Address.BuildingNumber);
        string addressLine2 = InvoiceMath.JoinNonEmpty(party.Address.PostalCode, party.Address.City);
        string idXml = party.Nip != null
            ? $"<NIP>{Esc(party.Nip)}</NIP>"
            : "<BrakID>1</BrakID>";

        return $@"
      <DaneIdentyfikacyjne>
         {idXml}
         <Nazwa>{Esc(party.Name)}</Nazwa>
      </DaneIdentyfikacyjne>
      <Adres>
         <KodKraju>{Esc(countryCode)}</KodKraju>
         <AdresL1>{Esc(addressLine1)}</AdresL1>
         <AdresL2>{Esc(addressLine2)}</AdresL2>
      </Adres>";
    }

    private static string BuildSellerXml(Party party)
    {
        return $@"
   <Podmiot1>
      {BuildPartyXml(party)}
   </Podmiot1>";
    }

    private static string BuildBuyerXml(Party party)
    {
        return $@"
   <Podmiot2>
      {BuildPartyXml(party)}
      <JST>2</JST>
      <GV>2</GV>
   </Podmiot2>";
    }

    private static string Esc(string s) => SecurityElement.Escape(s);

    private static string Fmt(decimal d) => InvoiceMath.FormatDecimal(d);

    private static string FmtRate(decimal value) => value.ToString("0.####", CultureInfo.InvariantCulture);
}
