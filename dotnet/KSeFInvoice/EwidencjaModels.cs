namespace KSeFInvoice;

public static class EwidencjaRates
{
    public static readonly decimal[] OrderedRates = [17m, 15m, 14m, 12.5m, 12m, 10m, 8.5m, 5.5m, 3m];
    public const decimal DefaultRevenueRate = 12m;
}

public sealed class EwidencjaEntry
{
    public int Lp { get; set; }
    public required DateOnly EntryDate { get; init; }
    public required DateOnly RevenueDate { get; init; }
    public string KsefNumber { get; init; } = string.Empty;
    public required string AccountingDocumentNumber { get; init; }
    public string ContractorTaxIdentifier { get; init; } = string.Empty;
    public required decimal RyczaltRate { get; init; }
    public required decimal RevenuePln { get; init; }
    public required string Notes { get; init; }
    public required string SourceBaseName { get; init; }

    public decimal AmountForRate(decimal rate) => RyczaltRate == rate ? RevenuePln : 0m;
}

public sealed class EwidencjaDocument
{
    public required DateOnly Month { get; init; }
    public required IReadOnlyList<EwidencjaEntry> Entries { get; init; }
    public required IReadOnlyDictionary<decimal, decimal> TotalsByRate { get; init; }
    public required decimal TotalRevenue { get; init; }
}

public sealed class EwidencjaGenerationResult
{
    public required EwidencjaDocument Document { get; init; }
    public required string OutputPath { get; init; }
    public required IReadOnlyList<string> MissingCurrencyDifferenceData { get; init; }
}
