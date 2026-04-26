using System.Globalization;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace PiggyBank.App.Analytics.Reports;

/// <summary>
/// QuestPDF document for a single Month export. Pure rendering class —
/// takes a fully-resolved <see cref="MonthPdfReportData"/> from the VM,
/// no DI, no DB access. Lives outside the VM so it can be unit-tested
/// against a fixed DTO if we ever want to.
///
/// Layout (A4 portrait):
///  - Header band: profile DisplayName + period + status pill
///  - Metrics row: salary | outgoings | spend | running balance | projected savings
///  - Outgoings table (Name, Amount, Alloc?, Used, Remaining)
///  - Ledger table (Date, Payee, Category, Amount)
///  - Spend-by-category bar list
///  - Footer: generated-by line + page X of Y
/// </summary>
public sealed class MonthPdfReport(MonthPdfReportData data) : IDocument
{
    private static readonly CultureInfo EnGb = CultureInfo.GetCultureInfo("en-GB");

    // Brand-ish palette — kept here rather than coupling to App.xaml so
    // the report is portable. PiggyBank pink for the status pill, warm
    // greys for table rules.
    private const string AccentColour = "#EC4899";   // pink-500
    private const string AccentSoftColour = "#FCE7F3"; // pink-100
    private const string MutedColour = "#64748B";    // slate-500
    private const string RuleColour = "#E2E8F0";     // slate-200
    private const string OverspendColour = "#DC2626"; // red-600
    private const string GoodColour = "#16A34A";     // green-600

    private readonly MonthPdfReportData _data = data;

    public DocumentMetadata GetMetadata() => new()
    {
        Title = $"PiggyBank — {_data.PeriodLabel}",
        Author = "PiggyBank",
        Subject = $"Monthly report for {_data.ProfileDisplayName}",
        Creator = "PiggyBank",
    };

    public DocumentSettings GetSettings() => DocumentSettings.Default;

    public void Compose(IDocumentContainer container)
    {
        container.Page(page =>
        {
            page.Size(PageSizes.A4);
            page.Margin(36);
            page.PageColor(Colors.White);
            page.DefaultTextStyle(t => t.FontSize(10).FontColor(Colors.Black));

            page.Header().Element(ComposeHeader);
            page.Content().Element(ComposeContent);
            page.Footer().Element(ComposeFooter);
        });
    }

    private void ComposeHeader(IContainer container)
    {
        container.PaddingBottom(12).Column(col =>
        {
            col.Item().Row(row =>
            {
                row.RelativeItem().Column(left =>
                {
                    left.Item().Text("PiggyBank")
                        .FontSize(20).Bold().FontColor(AccentColour);
                    left.Item().Text(_data.ProfileDisplayName)
                        .FontSize(13).SemiBold();
                    left.Item().PaddingTop(2).Text(_data.PeriodLabel)
                        .FontSize(10).FontColor(MutedColour);
                });

                row.ConstantItem(80).AlignRight().AlignMiddle()
                    .Background(_data.IsClosed ? AccentSoftColour : "#DCFCE7")
                    .PaddingVertical(4).PaddingHorizontal(10)
                    .Text(_data.IsClosed ? "Closed" : "Open")
                    .FontSize(10).SemiBold()
                    .FontColor(_data.IsClosed ? AccentColour : "#166534");
            });

            col.Item().PaddingTop(8).LineHorizontal(0.5f).LineColor(RuleColour);
        });
    }

    private void ComposeContent(IContainer container)
    {
        container.PaddingVertical(8).Column(col =>
        {
            col.Spacing(14);
            col.Item().Element(ComposeMetricsRow);
            col.Item().Element(ComposeOutgoingsTable);
            col.Item().Element(ComposeLedgerTable);
            col.Item().Element(ComposeCategoryRollup);
        });
    }

    private void ComposeMetricsRow(IContainer container)
    {
        container.Row(row =>
        {
            row.Spacing(8);
            row.RelativeItem().Element(c => MetricCard(c, "Salary", _data.MonthlySalary, null));
            row.RelativeItem().Element(c => MetricCard(c, "Outgoings", _data.TotalOutgoings, null));
            row.RelativeItem().Element(c => MetricCard(c, "Spend", -Math.Abs(_data.MonthlySpendTotal), null));
            row.RelativeItem().Element(c => MetricCard(c, "Running balance", _data.GrandTotal, null));
            row.RelativeItem().Element(c => MetricCard(c, "Projected savings", _data.ProjectedSavings,
                _data.IsProjectedPositive ? GoodColour : OverspendColour));
        });
    }

    private static void MetricCard(IContainer container, string label, decimal value, string? valueColour)
    {
        container
            .Background("#F8FAFC")
            .Border(0.5f).BorderColor(RuleColour)
            .Padding(10)
            .Column(col =>
            {
                col.Item().Text(label.ToUpperInvariant())
                    .FontSize(8).SemiBold().FontColor(MutedColour);
                col.Item().PaddingTop(4).Text(FormatMoney(value))
                    .FontSize(13).SemiBold().FontColor(valueColour ?? Colors.Black);
            });
    }

    private void ComposeOutgoingsTable(IContainer container)
    {
        container.Column(col =>
        {
            col.Item().Text("Outgoings").FontSize(12).SemiBold();
            col.Item().PaddingTop(6).Table(table =>
            {
                table.ColumnsDefinition(c =>
                {
                    c.RelativeColumn(3);  // Name
                    c.RelativeColumn(2);  // Amount
                    c.RelativeColumn(1);  // Alloc?
                    c.RelativeColumn(2);  // Used
                    c.RelativeColumn(2);  // Remaining
                });

                table.Header(header =>
                {
                    HeaderCell(header.Cell(), "Name");
                    HeaderCell(header.Cell(), "Amount", alignRight: true);
                    HeaderCell(header.Cell(), "Alloc?");
                    HeaderCell(header.Cell(), "Used", alignRight: true);
                    HeaderCell(header.Cell(), "Remaining", alignRight: true);
                });

                if (_data.Outgoings.Count == 0)
                {
                    table.Cell().ColumnSpan(5).PaddingVertical(6)
                        .Text("No outgoings recorded for this month.")
                        .FontColor(MutedColour).Italic();
                }
                else
                {
                    foreach (var o in _data.Outgoings)
                    {
                        BodyCell(table.Cell(), o.Name);
                        BodyCell(table.Cell(), FormatMoney(o.Amount), alignRight: true);
                        BodyCell(table.Cell(), o.IsAllocation ? "Yes" : "");
                        BodyCell(table.Cell(),
                            o.IsAllocation ? FormatMoney(-Math.Abs(o.AllocationUsed)) : "",
                            alignRight: true);
                        BodyCell(table.Cell(),
                            o.IsAllocation ? FormatMoney(o.AllocationRemaining) : "",
                            alignRight: true,
                            colour: o.IsAllocation && o.AllocationRemaining < 0m ? OverspendColour : null);
                    }

                    // Totals row
                    var outgoingsTotal = _data.Outgoings.Sum(o => o.Amount);
                    BodyCell(table.Cell(), "Total", bold: true);
                    BodyCell(table.Cell(), FormatMoney(outgoingsTotal), alignRight: true, bold: true);
                    BodyCell(table.Cell(), "");
                    BodyCell(table.Cell(), "");
                    BodyCell(table.Cell(), "");
                }
            });
        });
    }

    private void ComposeLedgerTable(IContainer container)
    {
        container.Column(col =>
        {
            col.Item().Text("Ledger").FontSize(12).SemiBold();
            col.Item().PaddingTop(6).Table(table =>
            {
                table.ColumnsDefinition(c =>
                {
                    c.RelativeColumn(2);  // Date
                    c.RelativeColumn(4);  // Payee
                    c.RelativeColumn(3);  // Category
                    c.RelativeColumn(2);  // Amount
                });

                table.Header(header =>
                {
                    HeaderCell(header.Cell(), "Date");
                    HeaderCell(header.Cell(), "Payee");
                    HeaderCell(header.Cell(), "Category");
                    HeaderCell(header.Cell(), "Amount", alignRight: true);
                });

                if (_data.Transactions.Count == 0)
                {
                    table.Cell().ColumnSpan(4).PaddingVertical(6)
                        .Text("No transactions recorded for this month.")
                        .FontColor(MutedColour).Italic();
                }
                else
                {
                    foreach (var t in _data.Transactions)
                    {
                        BodyCell(table.Cell(), t.Date.ToString("dd MMM", EnGb));
                        BodyCell(table.Cell(), t.Payee);
                        BodyCell(table.Cell(), t.CategoryName ?? "");
                        BodyCell(table.Cell(), FormatMoney(t.Amount),
                            alignRight: true,
                            colour: t.Amount < 0m ? OverspendColour : null);
                    }
                }
            });
        });
    }

    private void ComposeCategoryRollup(IContainer container)
    {
        var rollup = _data.CategoryRollups.Where(r => r.Total < 0m)
            .OrderBy(r => r.Total)  // most-negative first
            .ToList();
        if (rollup.Count == 0) return;

        var max = rollup.Max(r => Math.Abs(r.Total));

        container.Column(col =>
        {
            col.Item().Text("Spend by category").FontSize(12).SemiBold();
            col.Item().PaddingTop(6).Column(rows =>
            {
                rows.Spacing(4);
                foreach (var r in rollup)
                {
                    var magnitude = Math.Abs(r.Total);
                    // Two relative weights (filled, empty) split the available
                    // bar width — QuestPDF doesn't support percentage Width, so
                    // we lean on Row's relative columns to render proportionally.
                    var filledWeight = max == 0m ? 0f : (float)(magnitude / max);
                    if (filledWeight < 0.001f) filledWeight = 0.001f;  // visible nub
                    var emptyWeight = Math.Max(0.001f, 1f - filledWeight);

                    rows.Item().Row(row =>
                    {
                        row.RelativeItem(3).AlignMiddle().Text(r.Name).FontSize(10);
                        row.RelativeItem(6).AlignMiddle().Height(10).Row(bar =>
                        {
                            bar.RelativeItem(filledWeight).Background(AccentColour);
                            bar.RelativeItem(emptyWeight).Background(RuleColour);
                        });
                        row.RelativeItem(2).AlignMiddle().AlignRight()
                            .Text(FormatMoney(r.Total))
                            .FontSize(10).FontColor(OverspendColour);
                    });
                }
            });
        });
    }

    private void ComposeFooter(IContainer container)
    {
        container.PaddingTop(8).Column(col =>
        {
            col.Item().LineHorizontal(0.5f).LineColor(RuleColour);
            col.Item().PaddingTop(6).Row(row =>
            {
                row.RelativeItem().Text(text =>
                {
                    text.DefaultTextStyle(s => s.FontSize(8).FontColor(MutedColour));
                    text.Span($"Generated by PiggyBank · {_data.GeneratedAt:yyyy-MM-dd HH:mm}");
                });
                row.RelativeItem().AlignRight().Text(text =>
                {
                    text.DefaultTextStyle(s => s.FontSize(8).FontColor(MutedColour));
                    text.Span("Page ");
                    text.CurrentPageNumber();
                    text.Span(" of ");
                    text.TotalPages();
                });
            });
        });
    }

    // --- Cell helpers ---

    private static void HeaderCell(IContainer cell, string label, bool alignRight = false)
    {
        var c = cell
            .Background("#F1F5F9")
            .BorderBottom(0.5f).BorderColor(RuleColour)
            .PaddingVertical(4).PaddingHorizontal(6);
        if (alignRight) c = c.AlignRight();
        c.Text(label).FontSize(9).SemiBold().FontColor(MutedColour);
    }

    private static void BodyCell(
        IContainer cell,
        string text,
        bool alignRight = false,
        bool bold = false,
        string? colour = null)
    {
        var c = cell
            .BorderBottom(0.25f).BorderColor(RuleColour)
            .PaddingVertical(3).PaddingHorizontal(6);
        if (alignRight) c = c.AlignRight();
        var span = c.Text(text).FontSize(10);
        if (bold) span = span.SemiBold();
        if (colour is not null) span.FontColor(colour);
    }

    private static string FormatMoney(decimal value) =>
        value.ToString("£#,##0.00;-£#,##0.00;£0.00", EnGb);
}

// --- DTO -----------------------------------------------------------------

/// <summary>Everything <see cref="MonthPdfReport"/> needs to render. The VM
/// builds this from the same repos used by the CSV exporter; the report
/// itself does no DB work.</summary>
public sealed record MonthPdfReportData(
    string ProfileDisplayName,
    string PeriodLabel,
    bool IsClosed,
    decimal MonthlySalary,
    decimal CarriedOverBalance,
    decimal TotalOutgoings,
    decimal MonthlySpendTotal,
    decimal GrandTotal,
    decimal ProjectedSavings,
    bool IsProjectedPositive,
    IReadOnlyList<MonthPdfOutgoing> Outgoings,
    IReadOnlyList<MonthPdfTransaction> Transactions,
    IReadOnlyList<MonthPdfCategoryRollup> CategoryRollups,
    DateTime GeneratedAt);

public sealed record MonthPdfOutgoing(
    string Name,
    decimal Amount,
    bool IsAllocation,
    decimal AllocationUsed,
    decimal AllocationRemaining);

public sealed record MonthPdfTransaction(
    DateOnly Date,
    string Payee,
    string? CategoryName,
    decimal Amount);

public sealed record MonthPdfCategoryRollup(string Name, decimal Total);
