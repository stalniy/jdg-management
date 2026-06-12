namespace KSeFInvoice;

public enum InvoiceSourceFormat
{
    Xml,
    Yaml,
}

public sealed class InvoiceInputFile
{
    public required string InputPath { get; init; }
    public required string PdfPath { get; init; }
    public required string SubmissionPath { get; init; }
    public required InvoiceSourceFormat Format { get; init; }
}

public sealed class LoadedInvoiceSource
{
    public required InvoiceInputFile File { get; init; }
    public required InvoiceData InvoiceData { get; init; }
    public string? InvoiceXml { get; init; }
}

public sealed class PreparedInvoice
{
    public required InvoiceInputFile File { get; init; }
    public required InvoiceData InvoiceData { get; init; }
    public required string InvoiceXml { get; init; }
}

public sealed class InvoiceSubmissionRecord
{
    public required string SourcePath { get; init; }
    public required string SourceFormat { get; init; }
    public required string ReferenceNumber { get; init; }
    public required string VerificationUrl { get; init; }
    public string? KsefNumber { get; init; }
    public string? PollingError { get; init; }
    public required string SubmittedAtUtc { get; init; }
    public required InvoiceData InvoiceData { get; init; }
}
