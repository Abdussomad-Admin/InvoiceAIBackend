# Fictional demo workspace

The optional demo seed creates a separate workspace called **Abeokuta Stitch & Style (FICTIONAL DEMO)**. It never adds sample rows to an existing business. Seeded names, contact details, invoices, bills, and payment references are invented. Email addresses use the reserved `.invalid` domain.

## Enable locally

Run the backend in `Development` with a development database that has already been migrated. Set a demo login password through environment variables; do not add it to source control:

```powershell
$env:ASPNETCORE_ENVIRONMENT = "Development"
$env:DemoScenario__Enabled = "true"
$env:DemoScenario__Email = "demo-owner@invoiceai.local"
$env:DemoScenario__Password = "Use-A-Private-Development-Password-42!"
dotnet run --project src/Host
```

Open the local frontend and sign in with the email and password you configured. The seeder is disabled by default, refuses to run outside `Development`, and does nothing if that demo email already exists. Disable it by setting `DemoScenario__Enabled=false` or removing the variable.

The seed does not send email or call Paystack. Sample payments are explicitly marked fictional manual bank-transfer records; they are not evidence of provider-verified transfers. To demonstrate a real checkout and webhook, configure Paystack test credentials and a test payment account separately, then use a newly created test invoice. Do not represent the seeded records as real customer payments.

## Seeded example

| Record | Example state |
|---|---|
| DEMO-INV-001 | Fully paid invoice for an Ankara outfit |
| DEMO-INV-002 | Partially paid invoice for shop uniforms |
| DEMO-INV-003 | Overdue unpaid invoice for custom outfits |
| DEMO-INV-004 | Invoice due next month for event clothing |
| DEMO-BILL-001 | Fully paid fabric and zip supplier bill |
| DEMO-BILL-002 | Partially paid thread and packaging bill |
| DEMO-BILL-003 | Overdue workshop electricity bill |

All example dates are relative to the seed date so the overdue and upcoming states remain useful when the seed is first run.

## Walkthrough

1. Sign in to the clearly labelled fictional workspace; open **Invoices** and **Supplier bills** to review the sample records.
2. Open **Payments** and **Financial health** to see the recorded payments and balances.
3. Open **Financial health** to review income, costs, cash movement, outstanding balances, and the due-date forecast.
4. Ask Invoice AI to explain the recorded figures and overdue items; check the answer against the report.
5. Open **Financial records report**, preview the selected period, review the privacy note, and download the PDF. Use the device share menu only if you choose to share the fictional report.
6. Separately, create a fresh test invoice and complete a Paystack test-mode checkout to demonstrate provider verification. The seeded historical payments do not simulate that verification.
