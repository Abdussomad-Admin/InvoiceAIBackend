# Invoice AI

A backend for invoicing, accounts receivable, accounts payable and financial reporting for small businesses.
Built with ASP.NET Core (.NET 10), EF Core and PostgreSQL.

Live API docs: [https://invoiceaibackend-yk9n.onrender.com]/swagger  (free tier: the first request after idle takes about a minute)

## What is built

- **Accounts:** register, login, email verification, password reset, refresh tokens, logout, switching organization.
- **Team:** roles (Owner, Admin, Accountant, Staff), invitations, member management, profile and business profile.
- **Customers:** create, edit, archive, search.
- **Sales invoices:** draft, issue with sequential numbering, void, PDF download, email with PDF attached.
- **Payments:** partial and full payments, reversals with history, overpayment protection, overdue detection.
- **Suppliers and bills:** supplier records, bills with draft/finalize flow, duplicate detection, payments, reversals.
- **Reports:** dashboard, receivables aging, payables aging, expense report, financial summary, year comparison.
- **Audit log:** an automatic, append-only record of important changes.

## Not built yet (roadmap)

AI invoice extraction (upload and OCR), AI financial assistant, bank import and reconciliation, payment reminders
and background jobs, subscriptions and Paystack, CSV/Excel import and export, customer statements, a frontend,
credit notes, recurring invoices, multi-currency, and logo upload.

## Architecture

Layers, each depending only on the one below it:

    Host (controllers, startup)  ->  Infrastructure (database, email, PDF)  ->  Application (use cases)  ->  Domain (rules)

- Business rules live in the Domain layer and are covered by unit tests.
- Every request goes controller -> handler -> repository -> entity -> database.
- Money is `decimal`, rounded in one place, stored as `numeric(18,2)`.

## Run locally

1. Install the .NET 10 SDK and Docker Desktop.
2. Start PostgreSQL and a test mailbox: `docker compose up -d`
3. Create the tables: in the Package Manager Console (default project: Infrastructure) run `Update-Database`
4. Run the `Host` project, then open `/swagger`.

Locally, emails are written to the console (or to the mailbox at http://localhost:3000 if you set the SMTP options).

## Configuration

Settings come from `appsettings.json`, environment variables, and User Secrets. Never commit real secrets.

| Setting | Purpose |
|---|---|
| `ConnectionStrings__Default` | PostgreSQL connection string |
| `Jwt__Key` | Signing secret, at least 32 characters |
| `Email__Provider` | `Log`, `Smtp` or `Brevo` |
| `Email__ApiKey`, `Email__FromAddress` | Brevo API key and verified sender |
| `Database__MigrateOnStartup` | `true` applies migrations when the app starts |
| `ASPNETCORE_FORWARDEDHEADERS_ENABLED` | `true` when running behind a proxy such as Render |

## Tests

`dotnet test` runs [216] tests. Most use a real PostgreSQL database, so Docker must be running.
They cover calculations, statuses, permissions, tenant isolation, concurrency (no duplicate invoice numbers, no
overpayment) and failure cases.

## Deployment

Deployed on Render using the included `Dockerfile`, with a Render PostgreSQL database.
The Render free database **expires 30 days after creation (4th of December, 2026)**, then is deleted after a grace period.
The free web service sleeps when idle.

## Security notes

- Passwords are stored only as hashes. Access tokens last 15 minutes. Refresh tokens rotate, and reuse ends all sessions.
- Roles map to permissions, checked against the database on every request.
- Every organization's data is isolated: reads are filtered and writes to another organization are refused.
- Email links are single-use, expire, and are stored only as hashes.
- Sensitive endpoints are rate limited.

## Known limitations

- Email delivery is "accepted by the provider", with no bounce tracking and no automatic retries.
- Profit is an estimate: it only counts recorded invoices and supplier bills.
- One currency per organization, with 2 decimal places.
- An unverified email can still sign in. Registering reveals whether an email already has an account.
- Refresh tokens are returned in the response body, so a frontend must store them carefully.

## Licences

PDFs use QuestPDF (version [x.y.z]) under its Community License: free for individuals and organisations under
USD 1M annual revenue. Check the current terms before commercial use.