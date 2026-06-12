using System.Globalization;

namespace KSeFInvoice;

public static class InvoiceMath
{
    public static decimal GetLineNet(InvoiceLineItem item) => item.Quantity * item.UnitPriceNet;

    public static decimal GetLineVat(InvoiceLineItem item)
    {
        decimal net = GetLineNet(item);
        return item.VatRate == 0 ? 0m : net * item.VatRate / 100m;
    }

    public static decimal GetLineGross(InvoiceLineItem item) => GetLineNet(item) + GetLineVat(item);

    public static (decimal Net, decimal Vat, decimal Gross) GetTotals(InvoiceData data)
    {
        decimal totalNet = 0m;
        decimal totalVat = 0m;

        foreach (var item in data.LineItems)
        {
            totalNet += GetLineNet(item);
            totalVat += GetLineVat(item);
        }

        return (totalNet, totalVat, totalNet + totalVat);
    }

    public static Dictionary<int, (decimal Net, decimal Vat, decimal Gross)> GroupByVatRate(InvoiceData data)
    {
        var groups = new Dictionary<int, (decimal Net, decimal Vat, decimal Gross)>();

        foreach (var item in data.LineItems)
        {
            decimal net = GetLineNet(item);
            decimal vat = GetLineVat(item);
            decimal gross = net + vat;

            if (groups.TryGetValue(item.VatRate, out var existing))
            {
                groups[item.VatRate] = (existing.Net + net, existing.Vat + vat, existing.Gross + gross);
                continue;
            }

            groups[item.VatRate] = (net, vat, gross);
        }

        return groups;
    }

    public static string FormatDecimal(decimal value) => value.ToString("F2", CultureInfo.InvariantCulture);

    public static string FormatQuantity(decimal quantity) => quantity.ToString("0.####", CultureInfo.InvariantCulture);

    public static string FormatPolishAmount(decimal value)
    {
        string formatted = value.ToString("N2", CultureInfo.InvariantCulture);
        return formatted.Replace(",", " ").Replace(".", ",");
    }

    public static string FormatPolishRate(decimal value)
    {
        return value.ToString("0.####", CultureInfo.InvariantCulture).Replace(".", ",");
    }

    public static string GetCurrency(InvoiceData data)
    {
        return string.IsNullOrWhiteSpace(data.Currency)
            ? "PLN"
            : data.Currency.Trim().ToUpperInvariant();
    }

    public static DateOnly ParseDate(string value, string fieldName)
    {
        if (DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
            return parsed;

        throw new InvalidOperationException($"{fieldName} must use yyyy-MM-dd format. Received '{value}'.");
    }

    public static string GetSaleDate(InvoiceData data) => data.SaleDate ?? data.IssueDate;

    public static string JoinNonEmpty(params string?[] parts)
    {
        return string.Join(" ", parts.Where(part => !string.IsNullOrWhiteSpace(part)));
    }
}
