using System.Globalization;
using System.Text;
using System.Web;

namespace KSeFInvoice;

public class InvoiceHtmlOptions
{
    public string? QrCodeDataUrl { get; set; }
    public string? VerificationUrl { get; set; }
    public string? KsefNumber { get; set; }
}

public static class InvoiceHtmlGenerator
{
    public static string Generate(InvoiceData data, InvoiceHtmlOptions? options = null)
    {
        options ??= new InvoiceHtmlOptions();
        string currency = InvoiceMath.GetCurrency(data);
        var totals = InvoiceMath.GetTotals(data);

        // Line items rows
        var rows = new StringBuilder();
        for (int i = 0; i < data.LineItems.Count; i++)
        {
            var item = data.LineItems[i];
            decimal net = InvoiceMath.GetLineNet(item);
            string vatLabel = item.VatRate == 0 ? "zw" : $"{item.VatRate}%";
            decimal gross = InvoiceMath.GetLineGross(item);

            rows.Append($@"
      <tr>
        <td class=""center"">{i + 1}</td>
        <td class=""center"">{i + 1}</td>
        <td>{Esc(item.Description)}</td>
        <td class=""right"">{Fmt(item.UnitPriceNet)}</td>
        <td class=""right"">{FmtQty(item.Quantity)}</td>
        <td class=""center"">{Esc(item.Unit)}</td>
        <td class=""center"">{vatLabel}</td>
        <td class=""right"">{Fmt(net)}</td>
        <td class=""right"">{Fmt(gross)}</td>
      </tr>");
        }

        // VAT summary
        var vatSummaryRows = new StringBuilder();
        int idx = 0;
        foreach (var (rate, values) in InvoiceMath.GroupByVatRate(data).OrderByDescending(item => item.Key))
        {
            string label = rate == 0 ? "zw" : $"{rate}%";
            vatSummaryRows.Append($@"
      <tr>
        <td class=""center"">{++idx}</td>
        <td>{label}</td>
        <td class=""right"">{Fmt(values.Net)}</td>
        <td class=""right"">{Fmt(values.Vat)}</td>
        <td class=""right"">{Fmt(values.Gross)}</td>
      </tr>");
        }

        var paymentMethods = new Dictionary<int, string>
        {
            { 1, "Gotówka" }, { 2, "Karta" }, { 3, "Bon" }, { 4, "Czek" },
            { 5, "Kredyt" }, { 6, "Przelew" }, { 7, "Mobilna" }
        };

        string paymentLabel = data.Payment != null
            ? paymentMethods.GetValueOrDefault(data.Payment.FormaPlatnosci, "Inna")
            : "Brak zapłaty";

        string bankAccountHtml = data.Payment?.BankAccount != null
            ? $@"<div class=""payment-info"">Nr rachunku bankowego: {Esc(data.Payment.BankAccount)}</div>"
            : "";

        string zwolnienieHtml = data.VatExemptionClause != null
            ? $@"<hr><h2>Zwolnienie</h2><div class=""note-section"">Podstawa prawna: {Esc(data.VatExemptionClause)}</div>"
            : "";

        string notesHtml = data.Notes != null
            ? $@"<hr><h2>Dodatkowy opis</h2><div class=""note-section"">{Esc(data.Notes)}</div>"
            : "";

        string exchangeRateHtml = "";
        if (data.ExchangeRate != null && !string.Equals(currency, "PLN", StringComparison.OrdinalIgnoreCase))
        {
            decimal totalGrossPln = totals.Gross * data.ExchangeRate.Rate;
            DateOnly effectiveDate = InvoiceMath.ParseDate(data.ExchangeRate.EffectiveDate, "ExchangeRate.EffectiveDate");

            exchangeRateHtml = $@"
  <hr>
  <h2>Kurs waluty</h2>
  <div class=""note-section"">1 {Esc(currency)} = {Esc(InvoiceMath.FormatPolishRate(data.ExchangeRate.Rate))} PLN, tabela kursów średnich NBP {Esc(data.ExchangeRate.Table)} z dnia {effectiveDate:dd-MM-yyyy}</div>
  <div class=""note-section"">Kwota brutto po przeliczeniu: {Esc(Fmt(totalGrossPln))} PLN ({Esc(Fmt(totals.Gross))} {Esc(currency)})</div>";
        }

        string ksefNumberHtml = options.KsefNumber != null
            ? $@"<div class=""ksef-number"">Numer KSeF: {Esc(options.KsefNumber)}</div>"
            : "";

        string placeHtml = data.PlaceOfIssue != null
            ? $"<span>Miejsce wystawienia: {Esc(data.PlaceOfIssue)}</span>"
            : "";
        string saleDateHtml = data.SaleDate != null
            ? $"<span>Data wykonania usługi: {Esc(data.SaleDate)}</span>"
            : "";

        string qrHtml = "";
        if (options.QrCodeDataUrl != null && options.VerificationUrl != null)
        {
            qrHtml = $@"
  <div class=""qr-section"">
    <h2>Sprawdź, czy Twoja faktura znajduje się w KSeF!</h2>
    <div class=""qr-content"">
      <img src=""{options.QrCodeDataUrl}"" alt=""QR Code"">
      <div class=""qr-text"">
        <div>Nie możesz zeskanować kodu z obrazka? Kliknij w link weryfikacyjny i przejdź do weryfikacji faktury!</div>
        <br>
        <a href=""{Esc(options.VerificationUrl)}"">{Esc(options.VerificationUrl)}</a>
      </div>
    </div>
  </div>";
        }

        return $@"<!DOCTYPE html>
<html lang=""pl"">
<head>
<meta charset=""UTF-8"">
<title>Faktura {Esc(data.InvoiceNumber)}</title>
<style>
  @page {{ size: A4; margin: 15mm 20mm; }}
  * {{ box-sizing: border-box; margin: 0; padding: 0; }}
  body {{
    font-family: Arial, Helvetica, sans-serif;
    font-size: 10pt;
    color: #000;
    line-height: 1.4;
    padding: 30px 40px;
    max-width: 800px;
    margin: 0 auto;
  }}
  h2 {{ font-size: 13pt; font-weight: bold; margin: 20px 0 8px 0; }}
  h3 {{ font-size: 12pt; font-weight: bold; margin-bottom: 8px; }}
  .header {{ display: flex; justify-content: space-between; align-items: flex-start; margin-bottom: 20px; }}
  .header-title {{ font-size: 20pt; font-weight: bold; color: #000; }}
  .header-title .e {{ color: #c0392b; }}
  .header-meta {{ text-align: right; font-size: 9pt; color: #555; }}
  .header-meta .invoice-number {{ font-size: 16pt; font-weight: bold; color: #000; }}
  .header-meta .ksef-number {{ font-size: 8pt; color: #555; }}
  hr {{ border: none; border-top: 1px solid #ccc; margin: 16px 0; }}
  .parties {{ display: flex; gap: 40px; margin-bottom: 10px; }}
  .party {{ flex: 1; font-size: 9.5pt; line-height: 1.5; }}
  .details {{ font-size: 9.5pt; margin-bottom: 10px; }}
  .details span {{ margin-right: 40px; }}
  table {{ width: 100%; border-collapse: collapse; margin-bottom: 10px; font-size: 8.5pt; }}
  thead th {{ background: #f0f0f0; border: 1px solid #ccc; padding: 5px 8px; text-align: left; font-weight: bold; font-size: 8pt; }}
  tbody td {{ border: 1px solid #ccc; padding: 5px 8px; }}
  .center {{ text-align: center; }}
  .right {{ text-align: right; }}
  .total-line {{ text-align: right; font-size: 12pt; font-weight: bold; margin: 16px 0 20px 0; }}
  .payment-info {{ font-size: 9.5pt; margin-bottom: 6px; }}
  .note-section {{ font-size: 9.5pt; margin-bottom: 10px; }}
  .footer-system {{ margin-top: 40px; font-size: 8pt; color: #888; }}
  .qr-section {{ margin-top: 20px; padding-top: 16px; border-top: 1px solid #ccc; }}
  .qr-section h2 {{ color: #c0392b; font-size: 12pt; margin-bottom: 12px; }}
  .qr-content {{ display: flex; gap: 30px; align-items: flex-start; }}
  .qr-content img {{ width: 180px; height: 180px; }}
  .qr-text {{ font-size: 9pt; color: #c0392b; line-height: 1.6; }}
  .qr-text a {{ color: #2980b9; word-break: break-all; }}
</style>
</head>
<body>

  <div class=""header"">
    <div class=""header-title"">Krajowy System <span class=""e"">e</span>-Faktur</div>
    <div class=""header-meta"">
      Numer Faktury:<br>
      <div class=""invoice-number"">{Esc(data.InvoiceNumber)}</div>
      Faktura podstawowa
      {ksefNumberHtml}
    </div>
  </div>

  <hr>

  <div class=""parties"">
    {FormatParty("Sprzedawca", data.Seller, false)}
    {FormatParty("Nabywca", data.Buyer, true)}
  </div>

  <hr>

  <h2>Szczegóły</h2>
  <div class=""details"">
    <span>Data wystawienia: {Esc(data.IssueDate)}</span>
    {saleDateHtml}
    {placeHtml}
  </div>
  <div class=""details"">
    Kod waluty: {Esc(currency)}
  </div>

  <hr>

  <h2>Pozycje</h2>
  <div class=""details"">Faktura wystawiona w walucie {Esc(currency)}</div>

  <table>
    <thead>
      <tr>
        <th class=""center"" style=""width:30px"">Lp.</th>
        <th class=""center"" style=""width:60px"">Unikalny numer wiersza</th>
        <th>Nazwa towaru lub usługi</th>
        <th class=""right"" style=""width:75px"">Cena jedn. netto</th>
        <th class=""right"" style=""width:40px"">Ilość</th>
        <th class=""center"" style=""width:45px"">Miara</th>
        <th class=""center"" style=""width:55px"">Stawka podatku</th>
        <th class=""right"" style=""width:80px"">Wartość sprzedaży netto</th>
        <th class=""right"" style=""width:85px"">Wartość sprzedaży brutto</th>
      </tr>
    </thead>
    <tbody>
      {rows}
    </tbody>
  </table>

  <div class=""total-line"">Kwota należności ogółem: {Fmt(totals.Gross)} {Esc(currency)}</div>

  <h2>Podsumowanie stawek podatku</h2>
  <table>
    <thead>
      <tr>
        <th class=""center"" style=""width:30px"">Lp.</th>
        <th>Stawka podatku</th>
        <th class=""right"">Kwota netto</th>
        <th class=""right"">Kwota podatku</th>
        <th class=""right"">Kwota brutto</th>
      </tr>
    </thead>
    <tbody>
      {vatSummaryRows}
    </tbody>
  </table>

  <h2>Płatność</h2>
  <div class=""payment-info"">Informacja o płatności: {paymentLabel}</div>
  {bankAccountHtml}

  {zwolnienieHtml}

  {notesHtml}

  {exchangeRateHtml}

  {qrHtml}

  <div class=""footer-system"">Wytworzona w: ksef-client-csharp</div>

</body>
</html>";
    }

    private static string FormatParty(string label, Party party, bool showJstGv)
    {
        string countryCode = party.Address.CountryCode ?? "PL";
        string countryName = countryCode == "PL" ? "Polska" : countryCode;
        string addressLine1 = Esc(InvoiceMath.JoinNonEmpty(party.Address.Street, party.Address.BuildingNumber));
        string addressLine2 = Esc(InvoiceMath.JoinNonEmpty(party.Address.PostalCode, party.Address.City));

        string nipPrefix = party.Nip != null ? "<div>Prefiks VAT: PL</div>" : "";
        string nipLine = party.Nip != null ? $@"<div><strong>NIP:</strong> {Esc(party.Nip)}</div>" : "";

        string jstGv = showJstGv
            ? @"<br><div>Faktura dotyczy jednostki podrzędnej JST: <strong>NIE</strong></div>
      <div>Faktura dotyczy członka grupy GV: <strong>NIE</strong></div>"
            : "";

        return $@"
    <div class=""party"">
      <h3>{label}</h3>
      {nipPrefix}
      {nipLine}
      <div><strong>Nazwa:</strong> {Esc(party.Name)}</div>
      <br>
      <div>Adres</div>
      <div>{addressLine1}</div>
      <div>{addressLine2}</div>
      <div>{countryName}</div>
      {jstGv}
    </div>";
    }

    private static string Esc(string s) => HttpUtility.HtmlEncode(s);

    private static string Fmt(decimal n) => InvoiceMath.FormatPolishAmount(n);

    private static string FmtQty(decimal quantity) => InvoiceMath.FormatQuantity(quantity);
}
