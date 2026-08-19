using System.Globalization;
using System.Text;
using System.Web;

namespace KSeFInvoice;

public static class EwidencjaHtmlGenerator
{
    public static string Generate(EwidencjaDocument document)
    {
        var rows = new StringBuilder();
        foreach (var entry in document.Entries)
        {
            rows.Append($@"
      <tr>
        <td class=""center"">{entry.Lp}</td>
        <td class=""center"">{FmtDate(entry.EntryDate)}</td>
        <td class=""center"">{FmtDate(entry.RevenueDate)}</td>
        <td>{Esc(entry.KsefNumber)}</td>
        <td>{Esc(entry.AccountingDocumentNumber)}</td>
        <td>{Esc(entry.ContractorTaxIdentifier)}</td>
        {RateCells(entry)}
        <td class=""right"">{FmtAmount(entry.RevenuePln)}</td>
        <td>{Esc(entry.Notes)}</td>
      </tr>");
        }

        string totalsCells = string.Join(
            Environment.NewLine,
            EwidencjaRates.OrderedRates.Select(rate => $@"        <td class=""right"">{FmtAmount(document.TotalsByRate[rate])}</td>"));

        return $@"<!DOCTYPE html>
<html lang=""pl"">
<head>
<meta charset=""UTF-8"">
<title>Ewidencja przychodów {document.Month:yyyy-MM}</title>
<style>
  @page {{ size: A4 landscape; margin: 7mm; }}
  * {{ box-sizing: border-box; }}
  body {{ font-family: Arial, Helvetica, sans-serif; font-size: 8.5pt; color: #000; margin: 0; }}
  h1 {{ font-size: 15pt; font-weight: 400; margin: 0 0 8px 0; padding: 8px 2px; border-top: 2px solid #222; border-bottom: 2px solid #222; }}
  table {{ width: 100%; border-collapse: collapse; table-layout: fixed; }}
  th, td {{ border-bottom: 1px solid #aaa; padding: 5px 4px; vertical-align: middle; overflow-wrap: anywhere; }}
  thead th {{ border-bottom: 2px solid #000; font-weight: 700; text-align: center; }}
  .subhead th {{ border-bottom: 1px solid #000; }}
  .numbers th {{ font-size: 6.5pt; font-weight: 400; padding: 1px 4px 3px; }}
  .center {{ text-align: center; }}
  .right {{ text-align: right; }}
  .rate {{ width: 5.8%; }}
  .lp {{ width: 3%; }}
  .date {{ width: 6.5%; }}
  .ksef {{ width: 12%; }}
  .doc {{ width: 10%; }}
  .taxid {{ width: 10%; }}
  .total {{ width: 7%; }}
  .notes {{ width: 13%; }}
  .summary-title {{ margin-top: 22px; padding: 7px 2px; border-top: 2px solid #222; border-bottom: 2px solid #222; font-size: 9pt; }}
  .totals td {{ border-bottom: 2px solid #000; padding: 8px 4px; }}
  .footnote {{ margin-top: 10px; font-size: 8pt; line-height: 1.25; }}
</style>
</head>
<body>
  <h1>Ewidencja przychodów - Zapisy w buforze ewidencji ryczałtowej za miesiąc {document.Month:yyyy-MM}</h1>
  <table>
    <thead>
      <tr>
        <th class=""lp"">Lp.</th>
        <th class=""date"">Data wpisu</th>
        <th class=""date"">Data uzyskania przychodu</th>
        <th class=""ksef"">Numer KSeF</th>
        <th class=""doc"">Numer dowodu księgowego</th>
        <th class=""taxid"">Identyfikator podatkowy kontrahenta</th>
        <th colspan=""9"">Przychody objęte ryczałtem od przychodów ewidencjonowanych według stawki</th>
        <th class=""total"">Ogółem przychód</th>
        <th class=""notes"">Uwagi*</th>
      </tr>
      <tr class=""subhead"">
        <th colspan=""6""></th>
        {RateHeaders()}
        <th>(7+8+9+10+11+12+13+14+15)</th>
        <th></th>
      </tr>
      <tr class=""numbers"">
        {NumberHeaders()}
      </tr>
    </thead>
    <tbody>
      {rows}
    </tbody>
  </table>

  <div class=""summary-title"">Suma przychodów od początku miesiąca&nbsp;&nbsp; {document.Month:yyyy-MM}</div>
  <table class=""totals"">
    <tr>
      <td colspan=""6""></td>
{totalsCells}
      <td class=""right"">{FmtAmount(document.TotalRevenue)}</td>
      <td></td>
    </tr>
  </table>

  <div class=""footnote"">
    * Podatnicy, którzy zamierzają skorzystać z przewidzianej w art. 21 ust. 1a ustawy możliwości kwartalnego wpłacania ryczałtu od przychodów ewidencjonowanych, w kolumnie „Uwagi” mogą wpisywać datę otrzymania przychodu. Podatnicy, którzy na podstawie art. 15 ust. 1a ustawy są obowiązani w prowadzonej ewidencji wyodrębnić przychody objęte odpowiednio podatkiem tonażowym albo zryczałtowanym podatkiem od wartości sprzedanej produkcji i ryczałtem od przychodów ewidencjonowanych, przychody objęte odpowiednio podatkiem tonażowym albo zryczałtowanym podatkiem od wartości sprzedanej produkcji wykazują wyłącznie w kolumnie „Uwagi”.
  </div>
</body>
</html>";
    }

    private static string RateHeaders()
    {
        return string.Join(
            Environment.NewLine,
            EwidencjaRates.OrderedRates.Select(rate => $@"        <th class=""rate"">{FmtRate(rate)}</th>"));
    }

    private static string RateCells(EwidencjaEntry entry)
    {
        return string.Join(
            Environment.NewLine,
            EwidencjaRates.OrderedRates.Select(rate => $@"        <td class=""right"">{FmtAmount(entry.AmountForRate(rate))}</td>"));
    }

    private static string NumberHeaders()
    {
        return string.Join(
            Environment.NewLine,
            Enumerable.Range(1, 17).Select(number => $@"        <th>{number}</th>"));
    }

    private static string FmtDate(DateOnly date) => date.ToString("dd-MM-yyyy", CultureInfo.InvariantCulture);

    private static string FmtAmount(decimal value) => InvoiceMath.FormatPolishAmount(value);

    private static string FmtRate(decimal value)
    {
        string formatted = value % 1m == 0m
            ? value.ToString("0", CultureInfo.InvariantCulture)
            : value.ToString("0.0", CultureInfo.InvariantCulture).Replace(".", ",");

        return $"{formatted}%";
    }

    private static string Esc(string value) => HttpUtility.HtmlEncode(value);
}
