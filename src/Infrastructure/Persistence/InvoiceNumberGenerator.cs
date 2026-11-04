using Application.Invoices;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Persistence;

public class InvoiceNumberGenerator(AppDbContext context) : IInvoiceNumberGenerator
{
    public async Task<string> NextAsync(Guid organizationId, CancellationToken cancellationToken = default)
    {
        // One atomic statement: create the counter at 1, or add 1 to it. A concurrent caller waits for the
        // row lock, so two invoices can never receive the same number. The values are sent as parameters.
        var rows = await context.Database.SqlQuery<int>($"""
            INSERT INTO "InvoiceCounters" ("OrganizationId", "LastNumber")
            VALUES ({organizationId}, 1)
            ON CONFLICT ("OrganizationId") DO UPDATE
            SET "LastNumber" = "InvoiceCounters"."LastNumber" + 1
            RETURNING "LastNumber" AS "Value"
            """).ToListAsync(cancellationToken);

        return $"INV-{rows.Single():D4}";
    }
}