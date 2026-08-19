using System.Globalization;
using KSeFInvoice;

namespace KSeFInvoice.Tests;

internal static class TestInvoices
{
    public static InvoiceData Create(
        string invoiceNumber = "F/01/07/2026",
        string issueDate = "2026-07-10",
        string? saleDate = null,
        string? currency = "PLN",
        string? placeOfIssue = "Warsaw",
        string? notes = null,
        string? vatExemptionClause = null,
        List<InvoiceLineItem>? lineItems = null)
    {
        return new InvoiceData
        {
            InvoiceNumber = invoiceNumber,
            IssueDate = issueDate,
            SaleDate = saleDate,
            Currency = currency,
            PlaceOfIssue = placeOfIssue,
            Seller = new Party
            {
                Nip = "6751818483",
                Name = "Seller Ltd",
                Address = new Address
                {
                    Street = "Seller Street",
                    BuildingNumber = "1",
                    PostalCode = "00-001",
                    City = "Warsaw",
                    CountryCode = "PL",
                },
            },
            Buyer = new Party
            {
                Name = "Overclock Labs, Inc",
                Address = new Address
                {
                    Street = "Market Street",
                    BuildingNumber = "548",
                    PostalCode = "94104",
                    City = "San Francisco",
                    CountryCode = "US",
                },
            },
            LineItems = lineItems ?? [Line()],
            VatExemptionClause = vatExemptionClause,
            Notes = notes,
            Payment = new PaymentInfo
            {
                FormaPlatnosci = 6,
                BankAccount = "PL123",
            },
        };
    }

    public static InvoiceLineItem Line(
        string description = "Software services",
        string unit = "usl.",
        decimal quantity = 1m,
        decimal unitPriceNet = 100m,
        int vatRate = 23)
    {
        return new InvoiceLineItem
        {
            Description = description,
            Unit = unit,
            Quantity = quantity,
            UnitPriceNet = unitPriceNet,
            VatRate = vatRate,
        };
    }

    public static string CreateYaml()
    {
        return $$"""
          InvoiceNumber: F/01/07/2026
          IssueDate: 2026-07-10
          SaleDate: 2026-07-10
          Currency: USD
          PlaceOfIssue: Warsaw
          Seller:
            Nip: 6751818483
            Name: Seller Ltd
            Address:
              Street: Seller Street
              BuildingNumber: "1"
              PostalCode: 00-001
              City: Warsaw
              CountryCode: PL
          Buyer:
            Name: Overclock Labs, Inc
            Address:
              Street: Market Street
              BuildingNumber: "548"
              PostalCode: "94104"
              City: San Francisco
              CountryCode: US
          LineItems:
            - Description: Software services
              Unit: usl.
              Quantity: 1
              UnitPriceNet: {{100m.ToString(CultureInfo.InvariantCulture)}}
              VatRate: 0
          Payment:
            FormaPlatnosci: 6
            BankAccount: PL123
          """;
    }
}
