# JDG management

## Requirements

Requires dotnet Core >= 10

## Submit invoices to KSeF

Place invoice input files under subfolders inside `invoices/`.

Accepted input formats:

- `.xml` as a ready-to-submit KSeF invoice
- `.yaml` / `.yml` shaped like `InvoiceData` from `dotnet/KSeFInvoice/Program.cs`

Example layout:

```text
invoices/
  2026-04/
    f-01-04-2026.yaml
    f-02-04-2026.xml
```

Each input file maps to files with the same basename in the same folder:

- `f-01-04-2026.pdf`
- `f-01-04-2026.ksef-submission.json`

Behavior:

- if the PDF already exists, the invoice is treated as processed and is skipped
- if the submission sidecar exists but the PDF is missing, the PDF is regenerated without re-submitting to KSeF
- XML and YAML invoices in foreign currency fetch the NBP exchange rate automatically using `P_6` / `SaleDate` minus 1 day, falling back to earlier days only when NBP has no published rate for that date

Minimal YAML example:

```yaml
InvoiceNumber: F/01/04/2026
IssueDate: 2026-04-30
SaleDate: 2026-04-14
Currency: USD
PlaceOfIssue: City
Seller:
  Nip: 1234567890
  Name: John Doe
  Address:
    Street: XXX
    BuildingNumber: XX / XX
    PostalCode: XX-XXX
    City: XXX
Buyer:
  Name: Company, Inc
  Address:
    Street: Some Street
    BuildingNumber: "123"
    PostalCode: "12345"
    City: Client City
    CountryCode: US
LineItems:
  - Description: usługi programistycznie
    Unit: usł.
    Quantity: 1
    UnitPriceNet: 1000
    VatRate: 0
VatExemptionClause: art. 113 ust. 1i9 ustawy o VAT
Notes: odwrotne obciąźenie, reverse charge
Payment:
  FormaPlatnosci: 6
  BankAccount: PLXXX
```

Run:

```bash
dotnet run --project dotnet/KSeFInvoice/KSeFInvoice.csproj
```

The app submits only invoices that do not already have an output PDF or submission sidecar.

## Test env

to switch to TEST env, just update `KSEF_ENV` in launchSettings.json to `TEST`
