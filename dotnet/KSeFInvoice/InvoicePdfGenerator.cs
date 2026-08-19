using PuppeteerSharp;
using PuppeteerSharp.Media;

namespace KSeFInvoice;

public sealed class PdfGenerationOptions
{
    public bool Landscape { get; init; }
}

public static class InvoicePdfGenerator
{
    public static async Task GenerateAsync(string html, string outputPath, PdfGenerationOptions? options = null)
    {
        options ??= new PdfGenerationOptions();
        string? directory = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);

        var browserFetcher = new BrowserFetcher();
        await browserFetcher.DownloadAsync();

        await using var browser = await Puppeteer.LaunchAsync(new LaunchOptions { Headless = true });
        await using var page = await browser.NewPageAsync();
        await page.SetContentAsync(html, new NavigationOptions { WaitUntil = [WaitUntilNavigation.Networkidle0] });
        await page.PdfAsync(outputPath, new PdfOptions
        {
            Format = PaperFormat.A4,
            Landscape = options.Landscape,
            PrintBackground = true,
            MarginOptions = new PuppeteerSharp.Media.MarginOptions
            {
                Top = "10mm",
                Bottom = "10mm",
                Left = "10mm",
                Right = "10mm",
            }
        });
    }
}
