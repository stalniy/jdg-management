using System.Globalization;
using System.Text;
using KSeFInvoice;

namespace KSeFInvoice.Tests;

public sealed class PdfTransactionDateExtractorTests
{
    [Fact]
    public void ExtractFromText_ToleratesPdfTextFormatting()
    {
        Assert.Equal(new DateOnly(2026, 7, 31), PdfTransactionDateExtractor.ExtractFromText("Data transakcji: 31-07-2026"));
        Assert.Equal(new DateOnly(2026, 7, 31), PdfTransactionDateExtractor.ExtractFromText("Data transakcji 31.07.2026"));
        Assert.Equal(new DateOnly(2026, 7, 31), PdfTransactionDateExtractor.ExtractFromText("Data\ntransakcji\n31-07-2026"));
        Assert.Equal(new DateOnly(2026, 7, 31), PdfTransactionDateExtractor.ExtractFromText("Data transakcji: 2026-07-31"));
    }

    [Fact]
    public async Task GetTransactionDateAsync_ReadsSyntheticPdfTextLayer()
    {
        string path = Path.Combine(Path.GetTempPath(), $"ksef-pdf-date-{Guid.NewGuid():N}.pdf");
        await File.WriteAllBytesAsync(path, CreateMinimalPdf("Data transakcji: 31-07-2026"));

        try
        {
            var date = await new PdfTransactionDateExtractor().GetTransactionDateAsync(path);
            Assert.Equal(new DateOnly(2026, 7, 31), date);
        }
        finally
        {
            if (File.Exists(path))
                File.Delete(path);
        }
    }

    [Fact]
    public void ExtractFromText_MissingTransactionDate_FailsExplicitly()
    {
        var exception = Assert.Throws<InvalidOperationException>(
            () => PdfTransactionDateExtractor.ExtractFromText("Data wystawienia: 05-08-2026"));

        Assert.Contains("Could not determine 'Data transakcji'", exception.Message, StringComparison.Ordinal);
    }

    private static byte[] CreateMinimalPdf(string text)
    {
        static string EscapePdfString(string value) => value.Replace("\\", "\\\\").Replace("(", "\\(").Replace(")", "\\)");

        string content = $"BT /F1 12 Tf 72 720 Td ({EscapePdfString(text)}) Tj ET";
        string[] objects =
        [
            "1 0 obj\n<< /Type /Catalog /Pages 2 0 R >>\nendobj\n",
            "2 0 obj\n<< /Type /Pages /Kids [3 0 R] /Count 1 >>\nendobj\n",
            "3 0 obj\n<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Resources << /Font << /F1 5 0 R >> >> /Contents 4 0 R >>\nendobj\n",
            $"4 0 obj\n<< /Length {Encoding.ASCII.GetByteCount(content)} >>\nstream\n{content}\nendstream\nendobj\n",
            "5 0 obj\n<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>\nendobj\n",
        ];

        var builder = new StringBuilder("%PDF-1.4\n");
        var offsets = new List<int>();
        foreach (string obj in objects)
        {
            offsets.Add(Encoding.ASCII.GetByteCount(builder.ToString()));
            builder.Append(obj);
        }

        int xrefOffset = Encoding.ASCII.GetByteCount(builder.ToString());
        builder.Append("xref\n0 6\n");
        builder.Append("0000000000 65535 f \n");
        foreach (int offset in offsets)
            builder.Append(CultureInfo.InvariantCulture, $"{offset:0000000000} 00000 n \n");

        builder.Append("trailer\n<< /Size 6 /Root 1 0 R >>\n");
        builder.Append(CultureInfo.InvariantCulture, $"startxref\n{xrefOffset}\n%%EOF\n");
        return Encoding.ASCII.GetBytes(builder.ToString());
    }
}
