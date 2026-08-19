using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using UglyToad.PdfPig;

namespace KSeFInvoice;

public interface ITransactionDateProvider
{
    Task<DateOnly> GetTransactionDateAsync(string pdfPath, CancellationToken ct = default);
}

public sealed class PdfTransactionDateExtractor : ITransactionDateProvider
{
    private static readonly Regex TransactionDateRegex = new(
        @"Data\s+transakcji\s*:?\s*(?<date>(?:\d{1,2}[-.]\d{1,2}[-.]\d{4})|(?:\d{4}-\d{2}-\d{2}))",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly string[] SupportedDateFormats =
    [
        "dd-MM-yyyy",
        "d-M-yyyy",
        "dd.MM.yyyy",
        "d.M.yyyy",
        "yyyy-MM-dd",
    ];

    public Task<DateOnly> GetTransactionDateAsync(string pdfPath, CancellationToken ct = default)
    {
        if (!File.Exists(pdfPath))
            throw new FileNotFoundException($"Confirmation PDF '{pdfPath}' does not exist.", pdfPath);

        string text = ExtractText(pdfPath);
        return Task.FromResult(ExtractFromText(text, pdfPath));
    }

    public static DateOnly ExtractFromText(string text, string sourceName = "PDF text")
    {
        var match = TransactionDateRegex.Match(text);
        if (!match.Success)
            throw new InvalidOperationException($"Could not determine 'Data transakcji' in {sourceName}.");

        string value = match.Groups["date"].Value;
        if (DateOnly.TryParseExact(value, SupportedDateFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
            return parsed;

        throw new InvalidOperationException($"Could not parse 'Data transakcji' value '{value}' in {sourceName}.");
    }

    private static string ExtractText(string pdfPath)
    {
        var builder = new StringBuilder();

        using var document = PdfDocument.Open(pdfPath);
        foreach (var page in document.GetPages())
        {
            if (builder.Length > 0)
                builder.AppendLine();

            builder.Append(page.Text);
        }

        string text = builder.ToString();
        if (string.IsNullOrWhiteSpace(text))
            throw new InvalidOperationException($"Confirmation PDF '{pdfPath}' has no usable text layer.");

        return text;
    }
}
