namespace KSeFInvoice;

public class Address
{
    public required string Street { get; set; }
    public required string BuildingNumber { get; set; }
    public required string PostalCode { get; set; }
    public required string City { get; set; }
    public string? CountryCode { get; set; }
}

public class Party
{
    public string? Nip { get; set; }
    public required string Name { get; set; }
    public required Address Address { get; set; }
}

public class InvoiceLineItem
{
    public required string Description { get; set; }
    public required string Unit { get; set; }
    public required decimal Quantity { get; set; }
    public required decimal UnitPriceNet { get; set; }
    public required int VatRate { get; set; }
}

public class PaymentInfo
{
    public required int FormaPlatnosci { get; set; }
    public string? BankAccount { get; set; }
    public string? ExchangeDifferenceDocumentNumber { get; set; }
}

public class InvoiceExchangeRate
{
    public required string Currency { get; set; }
    public required string Table { get; set; }
    public required string EffectiveDate { get; set; }
    public required decimal Rate { get; set; }
}

public class InvoiceData
{
    public required string InvoiceNumber { get; set; }
    public required string IssueDate { get; set; }
    public string? SaleDate { get; set; }
    public string? Currency { get; set; }
    public string? PlaceOfIssue { get; set; }
    public required Party Seller { get; set; }
    public required Party Buyer { get; set; }
    public required List<InvoiceLineItem> LineItems { get; set; }
    public string? VatExemptionClause { get; set; }
    public string? Notes { get; set; }
    public PaymentInfo? Payment { get; set; }
    public InvoiceExchangeRate? ExchangeRate { get; set; }
}
