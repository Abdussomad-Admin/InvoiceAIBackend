using System.Globalization;
using System.Text.Json;
using Application.Bills;
using Application.Common.Interfaces;
using Application.Invoices;
using Application.Organizations;
using Application.Reports;
using Domain.Common;
using Domain.Organizations;

namespace Application.Assistant;

// Arguments come from a language model, so every value is parsed strictly. A bad value becomes a clear
// error that is handed back to the model, never a guess.
public static class ToolArgs
{
    public static DateOnly? Date(IReadOnlyDictionary<string, JsonElement> args, string name)
    {
        if (!args.TryGetValue(name, out var element) || element.ValueKind != JsonValueKind.String)
            return null;

        var text = element.GetString();
        if (string.IsNullOrWhiteSpace(text)) return null;

        if (!DateOnly.TryParseExact(text.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
            throw new DomainException($"'{name}' must be a date in the format YYYY-MM-DD.");

        return date;
    }

    public static int? Integer(IReadOnlyDictionary<string, JsonElement> args, string name)
    {
        if (!args.TryGetValue(name, out var element) ||
            element.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
            return null;

        if (element.ValueKind == JsonValueKind.Number && element.TryGetInt32(out var number))
            return number;

        if (element.ValueKind == JsonValueKind.String &&
            int.TryParse(element.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
            return parsed;

        throw new DomainException($"'{name}' must be a whole number.");
    }
}

public sealed class FinancialSummaryTool(GetFinancialSummaryHandler handler) : IAssistantTool
{
    public ToolDefinition Definition { get; } = new(
        "get_financial_summary",
        "Income, expenses, estimated profit and cash flow for a period, with monthly figures. Income and expenses exclude tax. " +
        "Use it for questions about profit, income, spending, or whether expenses exceed income. Defaults to the current month so far.",
        [
            new ToolParameter("from", "STRING", "Start date, YYYY-MM-DD. Optional.", false),
            new ToolParameter("to", "STRING", "End date, YYYY-MM-DD. Optional.", false)
        ]);

    public Permission RequiredPermission => Permission.ViewReports;

    public async Task<object> ExecuteAsync(IReadOnlyDictionary<string, JsonElement> arguments, CancellationToken cancellationToken)
        => await handler.Handle(
            new FinancialSummaryQuery(ToolArgs.Date(arguments, "from"), ToolArgs.Date(arguments, "to")), cancellationToken);
}

public sealed class YearComparisonTool(GetYearComparisonHandler handler) : IAssistantTool
{
    public ToolDefinition Definition { get; } = new(
        "get_year_comparison",
        "Compares a year so far with the same dates of the previous year: income, expenses, estimated profit and net cash flow, " +
        "with the change and percentage change. Use it for 'this year compared to last year'. Defaults to the current year.",
        [new ToolParameter("year", "INTEGER", "The year to compare, for example 2026. Optional.", false)]);

    public Permission RequiredPermission => Permission.ViewReports;

    public async Task<object> ExecuteAsync(IReadOnlyDictionary<string, JsonElement> arguments, CancellationToken cancellationToken)
        => await handler.Handle(new YearComparisonQuery(ToolArgs.Integer(arguments, "year")), cancellationToken);
}

public sealed class ExpenseReportTool(GetExpenseReportHandler handler) : IAssistantTool
{
    public ToolDefinition Definition { get; } = new(
        "get_expense_report",
        "What the business spent with suppliers in a period: net expenses, tax, gross billed, cash paid, monthly figures and top suppliers. " +
        "Defaults to the last three months.",
        [
            new ToolParameter("from", "STRING", "Start date, YYYY-MM-DD. Optional.", false),
            new ToolParameter("to", "STRING", "End date, YYYY-MM-DD. Optional.", false)
        ]);

    public Permission RequiredPermission => Permission.ViewReports;

    public async Task<object> ExecuteAsync(IReadOnlyDictionary<string, JsonElement> arguments, CancellationToken cancellationToken)
        => await handler.Handle(
            new ExpenseQuery(ToolArgs.Date(arguments, "from"), ToolArgs.Date(arguments, "to")), cancellationToken);
}

public sealed class ReceivablesAgingTool(GetAgingHandler handler) : IAssistantTool
{
    private const int MaxCustomers = 15;

    public ToolDefinition Definition { get; } = new(
        "get_receivables_aging",
        "Money customers owe the business right now, in aging buckets, with the customers owing the most (top 15). " +
        "Use it for 'which customers owe me money' and questions about overdue receivables.",
        []);

    public Permission RequiredPermission => Permission.ViewReports;

    public async Task<object> ExecuteAsync(IReadOnlyDictionary<string, JsonElement> arguments, CancellationToken cancellationToken)
    {
        var report = await handler.Handle(cancellationToken);
        return report with { Customers = report.Customers.Take(MaxCustomers).ToList() };
    }
}

public sealed class PayablesAgingTool(GetPayablesAgingHandler handler) : IAssistantTool
{
    private const int MaxSuppliers = 15;

    public ToolDefinition Definition { get; } = new(
        "get_payables_aging",
        "Money the business owes suppliers right now, in aging buckets, with amounts due within 7 and 30 days and the suppliers owed most (top 15).",
        []);

    public Permission RequiredPermission => Permission.ViewReports;

    public async Task<object> ExecuteAsync(IReadOnlyDictionary<string, JsonElement> arguments, CancellationToken cancellationToken)
    {
        var report = await handler.Handle(cancellationToken);
        return report with { Suppliers = report.Suppliers.Take(MaxSuppliers).ToList() };
    }
}

public sealed class CashFlowForecastTool(GetCashFlowForecastHandler handler) : IAssistantTool
{
    public ToolDefinition Definition { get; } = new(
        "get_cash_flow_forecast",
        "Shows outstanding invoices and supplier bills due in the next 30 days, overdue amounts separately, and the assumptions. Expected payments are not guaranteed and this is not a bank balance.",
        []);

    public Permission RequiredPermission => Permission.ViewReports;

    public async Task<object> ExecuteAsync(IReadOnlyDictionary<string, JsonElement> arguments, CancellationToken cancellationToken)
        => await handler.Handle(cancellationToken);
}

public sealed class OverdueInvoicesTool(ListInvoicesHandler handler) : IAssistantTool
{
    public ToolDefinition Definition { get; } = new(
        "list_overdue_invoices",
        "Lists issued invoices that are past their due date, with invoice number, customer, due date and balance (up to 20).",
        []);

    public Permission RequiredPermission => Permission.ViewData;

    public async Task<object> ExecuteAsync(IReadOnlyDictionary<string, JsonElement> arguments, CancellationToken cancellationToken)
    {
        var result = await handler.Handle(new ListInvoicesQuery(null, null, null, true, 1, 20), cancellationToken);
        return new { total = result.Total, shown = result.Items.Count, invoices = result.Items };
    }
}

public sealed class BillsDueTool(
    ListBillsHandler handler, IOrganizationRepository organizations,
    ICurrentOrganization currentOrganization, TimeProvider clock) : IAssistantTool
{
    public ToolDefinition Definition { get; } = new(
        "list_bills_due",
        "Lists supplier bills that are unpaid and due on or before a date, including overdue ones (up to 20). Defaults to the next 30 days.",
        [new ToolParameter("dueBy", "STRING", "Latest due date to include, YYYY-MM-DD. Optional.", false)]);

    public Permission RequiredPermission => Permission.ViewPayables;

    public async Task<object> ExecuteAsync(IReadOnlyDictionary<string, JsonElement> arguments, CancellationToken cancellationToken)
    {
        var organization = await InvoiceLookups.OrganizationAsync(organizations, currentOrganization, cancellationToken);
        var today = InvoiceLookups.Today(organization, clock);
        var dueBy = ToolArgs.Date(arguments, "dueBy") ?? today.AddDays(30);

        var result = await handler.Handle(new ListBillsQuery(null, null, null, null, dueBy, 1, 20), cancellationToken);
        return new { dueBy, total = result.Total, shown = result.Items.Count, bills = result.Items };
    }
}
