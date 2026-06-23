using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Rihla.Services.ErpNext.Tools;

/// <summary>
/// Implements all ERP tool calls used by the AI assistant.
/// Tools are filtered per userRole so each employee only sees what they're permitted to query.
/// </summary>
public class ErpNextToolExecutor : IErpNextToolExecutor
{
    private readonly IErpNextClient _erp;
    private readonly ILogger<ErpNextToolExecutor> _logger;

    private static readonly JsonSerializerOptions _jsonRead = new(JsonSerializerDefaults.Web);

    public ErpNextToolExecutor(IErpNextClient erp, ILogger<ErpNextToolExecutor> logger)
    {
        _erp    = erp;
        _logger = logger;
    }

    // ── Tool Dispatcher ───────────────────────────────────────────────────────

    public async Task<ToolResult> ExecuteAsync(string toolName, string argumentsJson, string userRole)
    {
        _logger.LogInformation("Tool call: {Tool} args={Args}", toolName, argumentsJson);

        Dictionary<string, JsonElement> args;
        try
        {
            args = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(argumentsJson, _jsonRead)
                   ?? [];
        }
        catch
        {
            args = [];
        }

        try
        {
            return toolName switch
            {
                "get_sales_summary"         => await GetSalesSummaryAsync(args),
                "get_purchase_summary"      => await GetPurchaseSummaryAsync(args),
                "get_stock_balance"         => await GetStockBalanceAsync(args),
                "get_employee_list"         => await GetEmployeeListAsync(args),
                "get_outstanding_invoices"  => await GetOutstandingInvoicesAsync(args),
                "get_profit_loss"           => await GetProfitLossAsync(args),
                "get_customer_list"         => await GetCustomerListAsync(args),
                "get_item_list"             => await GetItemListAsync(args),
                "get_top_customers"         => await GetTopCustomersAsync(args),
                "get_expense_summary"       => await GetExpenseSummaryAsync(args),
                _                           => ToolResult.Fail($"Unknown tool: {toolName}")
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Tool {Tool} threw an exception", toolName);
            return ToolResult.Fail($"Tool execution failed: {ex.Message}");
        }
    }

    // ── Tool Implementations ──────────────────────────────────────────────────

    private async Task<ToolResult> GetSalesSummaryAsync(Dictionary<string, JsonElement> args)
    {
        var fromDate = GetString(args, "from_date") ?? GetFirstDayOfMonth();
        var toDate   = GetString(args, "to_date")   ?? GetToday();

        var filters = new Dictionary<string, string>
        {
            ["docstatus"]    = "1",
            ["posting_date"] = $"Between [{fromDate}, {toDate}]"
        };

        var fields = new List<string>
        {
            "name", "customer", "posting_date", "grand_total", "status", "currency"
        };

        var rows = await _erp.GetDocListAsync("Sales Invoice", filters, fields, limit: 200, orderBy: "posting_date desc");

        var total      = rows.Sum(r => GetDouble(r, "grand_total"));
        var count      = rows.Count;
        var paid       = rows.Count(r => r.GetValueOrDefault("status")?.ToString() == "Paid");
        var unpaid     = rows.Count(r => r.GetValueOrDefault("status")?.ToString() == "Unpaid");
        var overdue    = rows.Count(r => r.GetValueOrDefault("status")?.ToString() == "Overdue");

        return ToolResult.Ok(new
        {
            period       = new { from_date = fromDate, to_date = toDate },
            total_amount = total,
            invoice_count = count,
            paid_count   = paid,
            unpaid_count = unpaid,
            overdue_count = overdue,
            invoices     = rows.Take(20)
        });
    }

    private async Task<ToolResult> GetPurchaseSummaryAsync(Dictionary<string, JsonElement> args)
    {
        var fromDate = GetString(args, "from_date") ?? GetFirstDayOfMonth();
        var toDate   = GetString(args, "to_date")   ?? GetToday();

        var filters = new Dictionary<string, string>
        {
            ["docstatus"]    = "1",
            ["posting_date"] = $"Between [{fromDate}, {toDate}]"
        };

        var fields = new List<string>
        {
            "name", "supplier", "posting_date", "grand_total", "status", "currency"
        };

        var rows = await _erp.GetDocListAsync("Purchase Invoice", filters, fields, limit: 200, orderBy: "posting_date desc");

        var total = rows.Sum(r => GetDouble(r, "grand_total"));

        return ToolResult.Ok(new
        {
            period        = new { from_date = fromDate, to_date = toDate },
            total_amount  = total,
            invoice_count = rows.Count,
            invoices      = rows.Take(20)
        });
    }

    private async Task<ToolResult> GetStockBalanceAsync(Dictionary<string, JsonElement> args)
    {
        var itemCode    = GetString(args, "item_code");
        var itemGroup   = GetString(args, "item_group");
        var warehouse   = GetString(args, "warehouse");

        var filters = new Dictionary<string, string>();
        if (!string.IsNullOrEmpty(itemCode))  filters["item_code"]  = itemCode!;
        if (!string.IsNullOrEmpty(warehouse)) filters["warehouse"]  = warehouse!;

        var fields = new List<string>
        {
            "item_code", "item_name", "warehouse", "actual_qty", "valuation_rate", "stock_value"
        };

        var rows = await _erp.GetDocListAsync("Bin", filters, fields, limit: 100);

        var totalValue = rows.Sum(r => GetDouble(r, "stock_value"));

        return ToolResult.Ok(new
        {
            total_stock_value = totalValue,
            item_count        = rows.Count,
            items             = rows
        });
    }

    private async Task<ToolResult> GetEmployeeListAsync(Dictionary<string, JsonElement> args)
    {
        var department = GetString(args, "department");
        var status     = GetString(args, "status") ?? "Active";

        var filters = new Dictionary<string, string> { ["status"] = status };
        if (!string.IsNullOrEmpty(department)) filters["department"] = department!;

        var fields = new List<string>
        {
            "name", "employee_name", "designation", "department",
            "date_of_joining", "status", "cell_number"
        };

        var rows = await _erp.GetDocListAsync("Employee", filters, fields, limit: 200);

        return ToolResult.Ok(new
        {
            total_employees = rows.Count,
            employees       = rows
        });
    }

    private async Task<ToolResult> GetOutstandingInvoicesAsync(Dictionary<string, JsonElement> args)
    {
        var partyType = GetString(args, "party_type") ?? "Customer";

        var filters = new Dictionary<string, string>
        {
            ["docstatus"]              = "1",
            ["outstanding_amount"]     = "> 0"
        };

        var doctype = partyType == "Supplier" ? "Purchase Invoice" : "Sales Invoice";
        var partyField = partyType == "Supplier" ? "supplier" : "customer";

        var fields = new List<string>
        {
            "name", partyField, "due_date", "grand_total", "outstanding_amount", "currency"
        };

        var rows = await _erp.GetDocListAsync(doctype, filters, fields, limit: 100, orderBy: "due_date asc");

        var totalOutstanding = rows.Sum(r => GetDouble(r, "outstanding_amount"));

        return ToolResult.Ok(new
        {
            party_type          = partyType,
            total_outstanding   = totalOutstanding,
            invoice_count       = rows.Count,
            invoices            = rows
        });
    }

    private async Task<ToolResult> GetProfitLossAsync(Dictionary<string, JsonElement> args)
    {
        var fromDate  = GetString(args, "from_date") ?? GetFirstDayOfYear();
        var toDate    = GetString(args, "to_date")   ?? GetToday();

        var filters = new Dictionary<string, string>
        {
            ["from_fiscal_year"] = fromDate[..4],
            ["to_fiscal_year"]   = toDate[..4],
            ["from_date"]        = fromDate,
            ["to_date"]          = toDate,
            ["report_date"]      = toDate,
            ["periodicity"]      = "Monthly",
            ["accumulated_in_group_company"] = "0"
        };

        var rows = await _erp.RunReportAsync("Profit and Loss Statement", filters);

        return ToolResult.Ok(new
        {
            period = new { from_date = fromDate, to_date = toDate },
            data   = rows
        });
    }

    private async Task<ToolResult> GetCustomerListAsync(Dictionary<string, JsonElement> args)
    {
        var search     = GetString(args, "search");
        var territory  = GetString(args, "territory");

        var filters = new Dictionary<string, string> { ["disabled"] = "0" };
        if (!string.IsNullOrEmpty(territory)) filters["territory"] = territory!;

        var fields = new List<string>
        {
            "name", "customer_name", "customer_type", "territory",
            "customer_group", "mobile_no", "email_id"
        };

        var rows = await _erp.GetDocListAsync("Customer", filters, fields, limit: 100);

        if (!string.IsNullOrEmpty(search))
        {
            var s = search!.ToLower();
            rows = rows.Where(r =>
                (r.GetValueOrDefault("customer_name")?.ToString() ?? "").ToLower().Contains(s) ||
                (r.GetValueOrDefault("name")?.ToString() ?? "").ToLower().Contains(s))
                .ToList();
        }

        return ToolResult.Ok(new { count = rows.Count, customers = rows });
    }

    private async Task<ToolResult> GetItemListAsync(Dictionary<string, JsonElement> args)
    {
        var itemGroup = GetString(args, "item_group");
        var search    = GetString(args, "search");

        var filters = new Dictionary<string, string> { ["disabled"] = "0" };
        if (!string.IsNullOrEmpty(itemGroup)) filters["item_group"] = itemGroup!;

        var fields = new List<string>
        {
            "name", "item_name", "item_group", "stock_uom", "standard_rate", "description"
        };

        var rows = await _erp.GetDocListAsync("Item", filters, fields, limit: 100);

        if (!string.IsNullOrEmpty(search))
        {
            var s = search!.ToLower();
            rows = rows.Where(r =>
                (r.GetValueOrDefault("item_name")?.ToString() ?? "").ToLower().Contains(s) ||
                (r.GetValueOrDefault("name")?.ToString() ?? "").ToLower().Contains(s))
                .ToList();
        }

        return ToolResult.Ok(new { count = rows.Count, items = rows });
    }

    private async Task<ToolResult> GetTopCustomersAsync(Dictionary<string, JsonElement> args)
    {
        var fromDate = GetString(args, "from_date") ?? GetFirstDayOfMonth();
        var toDate   = GetString(args, "to_date")   ?? GetToday();
        var topN     = GetInt(args, "top_n", 10);

        var filters = new Dictionary<string, string>
        {
            ["docstatus"]    = "1",
            ["posting_date"] = $"Between [{fromDate}, {toDate}]"
        };

        var fields = new List<string> { "customer", "grand_total" };
        var rows   = await _erp.GetDocListAsync("Sales Invoice", filters, fields, limit: 500);

        var grouped = rows
            .GroupBy(r => r.GetValueOrDefault("customer")?.ToString() ?? "Unknown")
            .Select(g => new { customer = g.Key, total = g.Sum(r => GetDouble(r, "grand_total")) })
            .OrderByDescending(x => x.total)
            .Take(topN)
            .ToList();

        return ToolResult.Ok(new
        {
            period    = new { from_date = fromDate, to_date = toDate },
            top_customers = grouped
        });
    }

    private async Task<ToolResult> GetExpenseSummaryAsync(Dictionary<string, JsonElement> args)
    {
        var fromDate = GetString(args, "from_date") ?? GetFirstDayOfMonth();
        var toDate   = GetString(args, "to_date")   ?? GetToday();

        var filters = new Dictionary<string, string>
        {
            ["docstatus"]    = "1",
            ["posting_date"] = $"Between [{fromDate}, {toDate}]"
        };

        var fields = new List<string>
        {
            "name", "payee", "posting_date", "paid_amount", "mode_of_payment"
        };

        var rows  = await _erp.GetDocListAsync("Payment Entry", filters, fields, limit: 200);
        var total = rows.Sum(r => GetDouble(r, "paid_amount"));

        return ToolResult.Ok(new
        {
            period          = new { from_date = fromDate, to_date = toDate },
            total_expenses  = total,
            count           = rows.Count,
            entries         = rows.Take(20)
        });
    }

    // ── Tool Definitions (OpenAI format) ──────────────────────────────────────

    public IReadOnlyList<object> GetToolDefinitions(string userRole)
    {
        // All tools — role filtering can be applied here if needed
        var all = new List<object>
        {
            MakeTool("get_sales_summary",
                "Get sales invoices summary for a date range. Returns total amount, count, and status breakdown.",
                new { from_date = Param("string", "Start date YYYY-MM-DD (default: first day of current month)"),
                      to_date   = Param("string", "End date YYYY-MM-DD (default: today)") }),

            MakeTool("get_purchase_summary",
                "Get purchase invoices summary for a date range.",
                new { from_date = Param("string", "Start date YYYY-MM-DD"),
                      to_date   = Param("string", "End date YYYY-MM-DD") }),

            MakeTool("get_stock_balance",
                "Get current stock balance. Optionally filter by item code or warehouse.",
                new { item_code  = Param("string", "Filter by specific item code (optional)"),
                      warehouse  = Param("string", "Filter by warehouse (optional)"),
                      item_group = Param("string", "Filter by item group (optional)") }),

            MakeTool("get_employee_list",
                "Get list of employees. Optionally filter by department or status.",
                new { department = Param("string", "Department name to filter (optional)"),
                      status     = Param("string", "Employee status: Active or Left (default: Active)") }),

            MakeTool("get_outstanding_invoices",
                "Get invoices with outstanding (unpaid) amounts.",
                new { party_type = Param("string", "Customer or Supplier (default: Customer)") }),

            MakeTool("get_profit_loss",
                "Get Profit and Loss statement for a date range.",
                new { from_date = Param("string", "Start date YYYY-MM-DD (default: first day of year)"),
                      to_date   = Param("string", "End date YYYY-MM-DD (default: today)") }),

            MakeTool("get_customer_list",
                "Get list of customers. Optionally search by name.",
                new { search    = Param("string", "Search term for customer name (optional)"),
                      territory = Param("string", "Territory filter (optional)") }),

            MakeTool("get_item_list",
                "Get list of items/products in the system.",
                new { search     = Param("string", "Search term for item name (optional)"),
                      item_group = Param("string", "Item group filter (optional)") }),

            MakeTool("get_top_customers",
                "Get top customers by total sales in a date range.",
                new { from_date = Param("string", "Start date YYYY-MM-DD"),
                      to_date   = Param("string", "End date YYYY-MM-DD"),
                      top_n     = Param("integer", "Number of top customers to return (default: 10)") }),

            MakeTool("get_expense_summary",
                "Get payment entries (expenses) summary for a date range.",
                new { from_date = Param("string", "Start date YYYY-MM-DD"),
                      to_date   = Param("string", "End date YYYY-MM-DD") })
        };

        return all;
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static object MakeTool(string name, string description, object parameters) => new
    {
        type     = "function",
        function = new
        {
            name,
            description,
            parameters = new
            {
                type       = "object",
                properties = parameters
            }
        }
    };

    private static object Param(string type, string description) =>
        new { type, description };

    private static string? GetString(Dictionary<string, JsonElement> args, string key) =>
        args.TryGetValue(key, out var el) && el.ValueKind == JsonValueKind.String
            ? el.GetString()
            : null;

    private static int GetInt(Dictionary<string, JsonElement> args, string key, int defaultVal) =>
        args.TryGetValue(key, out var el) && el.TryGetInt32(out var v) ? v : defaultVal;

    private static double GetDouble(Dictionary<string, object?> row, string key) =>
        row.TryGetValue(key, out var v) && v is double d ? d : 0;

    private static string GetToday()           => DateTime.UtcNow.ToString("yyyy-MM-dd");
    private static string GetFirstDayOfMonth() => new DateTime(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1).ToString("yyyy-MM-dd");
    private static string GetFirstDayOfYear()  => new DateTime(DateTime.UtcNow.Year, 1, 1).ToString("yyyy-MM-dd");
}
