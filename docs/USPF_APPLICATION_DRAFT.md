# USPF Changemaker Challenge 2026 — Financial Inclusion application draft

**Status: working draft; founder review and evidence required before submission.**

The official Financial Inclusion prompt asks how an accessible, trustworthy and affordable ICT solution can help underserved people and small businesses use financial services, make safe decisions, build credit readiness and withstand shocks. It asks applicants to explain how the solution can work in an underserved Nigerian community. [Official problem statement](https://uspfchangemaker.ng/problems/financial-inclusion.html)

The official challenge page lists the submission deadline as **15 November 2026, 11:59 p.m. WAT**. Confirm the InnoX portal still accepts submissions before relying on this draft. [Official challenge page](https://www.uspfchangemaker.ng/)

## Proposed solution description

Invoice AI is a digital record-keeping and cash-flow tool for freelancers and small businesses. It helps a user create customer invoices, record money received, track supplier bills and payments, import a bank statement for reconciliation, and review plain-language summaries of recorded income, expenses and balances. A user can use the core workspace as a freelancer; organization and team setup are optional.

The product is designed to make everyday business records easier to create and understand. Those records can help a business owner explain trading activity when preparing for a conversation with a financial institution. Invoice AI does not issue loans, make credit decisions, guarantee eligibility or connect users to a lender. Its financial summaries reflect information entered in the workspace and can be incomplete when transactions have not been recorded.

## Problem and intended users

Many small businesses work with irregular income and lack a consistent, easy-to-share record of invoices, payments and supplier costs. Paper records, scattered messages and unfamiliar accounting terms can make it harder to understand what is owed, what has been paid and what information may be useful when discussing financial services.

**Intended first users (founder to confirm):** Nigerian freelancers, market traders and other microbusiness owners who currently manage sales and costs with paper, messaging apps or separate spreadsheets, including people in communities with unreliable connectivity or limited access to bookkeeping support.

**Pilot location/community (founder to complete):** [Name the community and state. Confirm that it is underserved and explain the basis for that description.]

## How it works

1. A business owner creates an account and can start as a freelancer without setting up an organization or inviting a team.
2. The owner saves customer details, creates invoices and records payments received. Supplier bills and payments can be tracked separately.
3. The owner can review receivables, payables, financial summaries and a due-item cash-flow forecast based on recorded invoices and bills.
4. A bank statement CSV or Excel file can be imported for review and matching against payments already recorded. A match does not independently prove a payment occurred.
5. Plain-language financial guidance explains the difference between sales, cash received, estimated profit and outstanding balances, and encourages users to verify payment status through their bank or payment provider.
6. When internet access drops while creating a new invoice, the current frontend can retain a draft in that browser tab. It labels the draft as unsent; the user must reconnect and explicitly save it. This is limited offline support, not a full offline system or background sync.

## Current capabilities and limits

- Invoice, customer, supplier bill, payment, reporting and bank-statement workflows are implemented in the product code. Their deployed provider flows and target-user usability still require live verification.
- AI supplier-bill extraction depends on the server’s configured AI capability and internet access; the product must not be presented as working offline for extraction.
- Financial reports are based on records entered into Invoice AI. The 30-day forecast is based on known invoice and bill due dates; it is not a prediction of all future income or costs.
- Financial-records PDF export is user initiated. No lender integration, credit score, loan offer or approval guarantee is claimed.
- Online invoice payment status depends on successful payment-provider verification/webhook delivery; the product must not claim independent bank confirmation.

## Inclusion and affordability approach

The product offers a freelancer path without requiring a formal organization workflow. Its pricing page includes a free entry plan and paid plans; the founder should verify the current live prices and local affordability before making a price claim in the application. The interface uses plain labels and explains what summaries mean. The current offline invoice draft reduces the risk of losing work during a short connection interruption, while requiring an explicit save after reconnection.

**Still to validate with intended users:** onboarding comprehension, use on low-cost Android phones, data consumption, readability, local-language needs, assisted access needs, pricing affordability and task completion under intermittent connectivity.

## Pilot and impact measurement plan

No user-impact measurements are claimed yet. During a pilot, collect only consented and appropriate data, record a baseline, and compare it with the end of the pilot. Suggested measures:

- Number of consenting pilot users who create and maintain business records for at least four weeks.
- Share of pilot users able to correctly identify an invoice balance, cash received and recorded supplier costs in a short usability task.
- Time and task-completion rate for creating an invoice and recording a payment, including a brief connectivity interruption.
- Share of invoices and supplier bills with a customer/supplier, date, amount and payment status recorded.
- User-reported confidence in understanding cash flow and keeping records, using the same short questions before and after the pilot.
- Number of users who choose to use an exported financial-records report in a real financial-services conversation; record only with explicit consent and do not treat this as evidence of credit approval.

Report sample size, dates, method, missing responses and limitations. Do not convert sign-ups, generated demo data or feature usage into claims of improved financial inclusion or lending outcomes.

## Demonstration plan

Use the clearly labelled fictional demo workspace and disclose that all its transactions are synthetic. Demonstrate a freelancer creating a customer invoice, recording a partial payment, viewing the outstanding balance and cash-flow summary, then importing a sample bank statement and matching a known recorded payment. Show the offline draft only in a browser test that disconnects before saving and clearly point out that it is still unsent. Keep Paystack test-mode activity distinct from real payments and never imply that a screenshot or imported bank line alone verifies payment.

**Evidence to attach:** current screenshots from the deployed product; a short screen recording if permitted; dated evidence of a successful staging workflow; a sample financial-records PDF with fictitious data; and a note identifying demo records as synthetic.

## Team and ownership — founder must complete

- **Applicant / team lead:** Ajibike Abdussomad — confirm preferred name format and role/title.
- **Other team members:** [Add each person’s name, role, location and consent to be listed; write “solo applicant” if applicable and allowed.]
- **Organization / venture name:** Invoice AI [confirm legal/operating name and whether registration details are requested.]
- **State and community of operation:** [Confirm.] 
- **Contact details:** [Use details requested by InnoX; do not put private contact information into public materials.]

## Final checks before submitting

- Confirm applicant eligibility, team rules, required fields, file types and any response-length limits in the InnoX form.
- Confirm the pilot community and avoid describing a community as underserved without a defensible basis.
- Fill in team details and product deployment status accurately.
- Attach only screenshots and evidence from the current build; label fictional demo data.
- Add actual user research or clearly present the measures above as a proposed evaluation plan.
- Review privacy, consent, payment-safety and data-retention claims. Do not promise lending, credit access or fraud detection that the product does not provide.
- Submit through the official InnoX application link only after founder review.
