# Invoice AI

A backend for invoicing, accounts receivable, accounts payable, AI-assisted supplier invoice capture and financial
reporting for small businesses. Built with ASP.NET Core (.NET 10), EF Core 10 and PostgreSQL.

Live API docs: https://invoiceaibackend-yk9n.onrender.com/swagger
(free tier: the first request after idle takes about a minute)

## What is built

**Accounts and teams**
- Register (creates the user, the organization and an Owner membership in one transaction), login, email
  verification, forgot/reset password, change password, profile and business profile.
- 15-minute access tokens and rotating 30-day refresh tokens. Reusing an old refresh token ends all sessions.
- Roles (Owner, Admin, Accountant, Staff) mapped to permissions, invitations, member management, switching
  organization. Only Owners manage Owners, and the last Owner is protected.

**Receivables**
- Customers (create, edit, archive, restore, search, summary).
- Sales invoices: draft, issue with sequential numbering (`INV-0001`), partially paid, paid, void. Issued invoices are
  read-only. Overdue is calculated, never stored.
- Payments: partial and full, reversals with history, overpayment is refused even under concurrent requests.
- Invoice PDF (QuestPDF) and email with the PDF attached.

**Payables**
- Suppliers and supplier bills: draft, finalize, open, partially paid, paid, void.
- Duplicate protection: the same supplier and normalized bill number is refused. The same supplier, date and total
  gives a warning that must be confirmed.
- Bill payments with reversals, supplier summary.

**Supplier document pipeline**
- Upload PDF, JPEG or PNG (type checked from the file bytes, 5 MB, 20 pages, per-organization storage cap,
  SHA-256 duplicate refusal).
- Text is read from PDFs with PdfPig. Scanned PDFs are detected and flagged, but not extracted yet.
- Extraction is an explicit action. Nothing is sent to an AI service at upload.
- The AI answer goes through a strict parser and a validator. Confidence is computed from checks, not taken from
  the model. Up to 5 attempts are kept per document.
- A person reviews and approves. Approval creates a **draft** bill only. The AI never creates a finalized record.

**AI financial assistant**
- `POST /api/assistant/ask` with seven read-only tools: financial summary, year comparison, expense report,
  receivables aging, payables aging, overdue invoices, bills due.
- Tools are filtered by the caller's role. The model never writes SQL and never calculates figures. Every answer
  includes the verified source data so users can check it.
- Off by default (`Assistant__Enabled=false`). Nothing is sent to Google until it is enabled with a key.

**Reports and audit**
- Dashboard, expense report, financial summary (accrual and cash side by side), year-over-year comparison,
  receivables aging, payables aging.
- Append-only audit log written automatically on every save.

## Not built yet (roadmap)

Customer statements, payment reminders and background jobs, email retries, CSV/Excel import and export,
bank statement import and reconciliation, subscriptions and Paystack, scanned-PDF extraction (OCR), object storage
for documents, malware scanning, credit notes, recurring invoices, multi-currency, logo upload, account lockout,
two-factor authentication, and a frontend.

## Architecture

    Host (controllers, startup) -> Infrastructure (database, email, PDF, AI) -> Application (use cases) -> Domain (rules)

- No MediatR. Controllers call handlers directly. Every class in the Application project whose name ends in
  `Handler` is registered at startup.
- Business rules live in the Domain layer. Money is `decimal`, rounded in one place (`MoneyMath`, away from zero)
  and stored as `numeric(18,2)`.
- Tenant isolation: every organization-owned entity has a global query filter, and writes to another organization are
  refused. Permissions are loaded from the database on every request.
- Concurrency: payments, bill payments and document approval lock the row (`SELECT ... FOR UPDATE`) inside a
  transaction. Invoice numbers come from an atomic upsert.
- AI providers sit behind interfaces (`IAssistantModel`, `IDocumentExtractionModel`) so they can be replaced.

## Run locally

1. Install the .NET 10 SDK and Docker Desktop.
2. Start PostgreSQL and a test mailbox: `docker compose up -d`
3. Create the tables: in the Package Manager Console (default project: Infrastructure) run `Update-Database`
4. Run the `Host` project, then open `/swagger`.

Locally, emails are written to the console, or to the mailbox at http://localhost:3000 if you set the SMTP options.
`Add-Migration` does not need Docker. `Update-Database` and the tests do.

## Configuration

Settings come from `appsettings.json`, `appsettings.Development.json`, User Secrets and environment variables
(double underscore, for example `Jwt__Key`). Never commit real secrets.

| Setting | Purpose |
|---|---|
| `ConnectionStrings__Default` | PostgreSQL connection string |
| `Jwt__Key` | Signing secret, at least 32 characters. The development key is refused outside Development |
| `Jwt__Issuer`, `Jwt__Audience`, `Jwt__ExpiryMinutes`, `Jwt__RefreshTokenDays` | Token settings |
| `Email__Provider` | `Log` (default), `Smtp` or `Brevo` |
| `Email__ApiKey`, `Email__FromAddress`, `Email__FromName` | Brevo API key and verified sender |
| `Email__Host`, `Port`, `Security`, `Username`, `Password` | SMTP settings |
| `App__FrontendBaseUrl` | Base URL used in email links |
| `Cors__AllowedOrigins__0` | Browser origin allowed to call the API |
| `Database__MigrateOnStartup` | `true` applies migrations when the app starts |
| `ASPNETCORE_FORWARDEDHEADERS_ENABLED` | `true` when running behind a proxy such as Render |
| `Swagger__Enabled` | `true` shows Swagger outside Development |
| `Assistant__Enabled`, `Assistant__ApiKey`, `Assistant__Model` | Turn on the AI assistant and extraction |
| `Assistant__Thinking`, `TimeoutSeconds`, `MaxToolRounds`, `MaxQuestionLength`, `QuestionsPerHour` | Assistant tuning |
| `Documents__MaxStoredMegabytes` | Storage cap per organization |

## Rate limits

`auth` 10 per minute per IP. `assistant` 30 per hour per user. `upload` 30 per minute per user.
`extract` 20 per hour per user. The assistant limit is held in memory, per user and per server instance.

## Tests

`dotnet test` runs 317 tests. Most use a real PostgreSQL database (`invoiceai_test`), so Docker must be running.
They cover calculations, statuses, permissions, tenant isolation, concurrency (no duplicate invoice numbers, no
overpayment), document upload and parsing, approval, and failure cases. Assistant and extraction tests use a scripted
fake model.

## Deployment

Deployed on Render using the included `Dockerfile`, with a Render PostgreSQL database.

Render free tier caveats:
- The free PostgreSQL database **expires 30 days after creation (4 December 2026)** and is deleted after a grace
  period. It also holds only 1 GB. Documents are stored in this database, so they are lost with it.
- The web service sleeps after about 15 minutes idle. The next request takes about a minute.
- The filesystem is ephemeral and the CPU is slow.
- Outbound SMTP ports (25, 465, 587) are blocked, so use the Brevo provider there.

## Security notes

- Passwords are stored only as hashes. Access tokens last 15 minutes. Refresh tokens rotate, and reuse ends all
  sessions. Password resets end all sessions.
- Email links are single-use, expire, and are stored only as hashes.
- Roles map to permissions, checked against the database on every request.
- Every organization's data is isolated: reads are filtered and writes to another organization are refused.
- Uploads are validated from file bytes, size limited and de-duplicated by hash.
- Sensitive endpoints are rate limited. The audit log is append-only.

## AI and privacy

- The AI model is Gemma through the Google Gemini API (`gemma-4-26b-a4b-it` by default), behind replaceable
  interfaces. **The live calls have not yet been tested against the real service.** The scripted-model tests cover
  the application logic, not Google's API.
- The Gemini free tier may use submitted content to improve Google products. Check Google's current terms. Use dummy
  data until a decision is made. Real client data needs a paid provider that does not train on it.
- AI answers can be wrong. The assistant returns its source figures so users can check them. Extraction output is
  never trusted until a person reviews and approves it.

## Known limitations

- Profit is an estimate: only recorded invoices and supplier bills count. Other expenses are not included.
- One currency per organization, with 2 decimal places.
- Email "sent" means accepted by the provider. There is no bounce tracking and no automatic retry.
- An unverified email can still sign in. Registering reveals whether an email already has an account.
- Refresh tokens are returned in the response body, so a frontend must store them carefully.
- Documents live in PostgreSQL, there is no malware scanning, and scanned PDFs are not supported.
- Assistant usage limits are in memory, per user and per server instance.

## Licences

- PDFs use QuestPDF 2026.9.1 under its Community License: free for individuals and organisations under USD 1M annual
  revenue. Check the current terms before commercial use.
- PdfPig 0.1.16 and MailKit 4.18.2 are used for PDF text and SMTP. Check their licences before commercial release.