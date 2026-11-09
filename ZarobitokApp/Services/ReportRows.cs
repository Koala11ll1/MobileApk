using System.Globalization;
using ZarobitokApp.Localization;

namespace ZarobitokApp.Services;

public enum RowKind { Title, Subtitle, Heading, Summary, TableHeader, TableRow, TableSubtotal, TableTotal, Body, Spacer }

/// <summary>
/// Рядок звіту без прив'язки до PDF: тип, комірки і ширини стовпців. Малює його вже платформний код.
/// Так увесь зміст звіту (числа, підписи, мова) перевіряється звичайними тестами.
/// </summary>
public sealed record ReportRow(RowKind Kind, string[] Cells, float[]? Weights = null, int LeftColumns = 1);

public static class ReportRows
{
    private static readonly float[] ShiftWeights = { 1.25f, 1.55f, 0.9f, 0.85f, 1.1f, 1.2f, 1.1f };
    private static readonly float[] ExtraWeights = { 3f, 1f, 1.6f };
    private static readonly float[] SummaryWeights = { 3f, 2f };

    public static List<ReportRow> Build(ReportData d, string currency, bool use12Hour)
    {
        string Money(decimal v) => EarningsCalculator.Format(v, currency);
        string Date(DateTime t) => t.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture);
        string Time(DateTime t) => EarningsCalculator.FormatTime(t, use12Hour);

        var rows = new List<ReportRow>
        {
            new(RowKind.Title, new[] { L.T("Звіт про заробіток") }),
            new(RowKind.Subtitle, new[] { $"{Date(d.From)} — {Date(d.To)}" }),
            new(RowKind.Spacer, Array.Empty<string>()),
            new(RowKind.Heading, new[] { L.T("Підсумок") }),
            Summary(L.T("Усього"), Money(d.Total)),
        };

        if (d.TaxPercent > 0)
            rows.Add(Summary(L.F("Після податку ({0:0.##}%)", d.TaxPercent), Money(d.Net)));

        rows.Add(Summary(L.T("За час"), Money(d.TimePay)));
        rows.Add(Summary(L.T("Допзаробіток"), Money(d.ExtrasPay)));
        rows.Add(Summary(L.T("Відпрацьовано"), EarningsCalculator.FormatDuration(d.Worked)));
        rows.Add(Summary(L.T("Змін / робочих днів"), $"{d.ShiftCount} / {d.WorkDays}"));
        rows.Add(Summary(L.T("Середній заробіток за годину"), d.ShiftCount > 0 ? Money(d.PerHour) : "—"));

        rows.Add(new(RowKind.Spacer, Array.Empty<string>()));

        if (d.ShiftCount == 0)
        {
            rows.Add(new(RowKind.Body, new[] { L.T("За цей період змін немає.") }));
            return rows;
        }

        rows.Add(new(RowKind.Heading, new[] { L.T("По змінах") }));
        rows.Add(new(RowKind.TableHeader,
            new[] { L.T("Дата"), L.T("Час"), L.T("Годин"), L.T("Ставка"), L.T("За час"), L.T("Допзаробіток"), L.T("Разом") },
            ShiftWeights, LeftColumns: 2));

        foreach (var day in d.Days)
        {
            for (var i = 0; i < day.Shifts.Count; i++)
            {
                var s = day.Shifts[i];
                rows.Add(new(RowKind.TableRow, new[]
                {
                    i == 0 ? Date(day.Day) : string.Empty,
                    $"{Time(s.StartLocal)}–{Time(s.EndLocal)}",
                    EarningsCalculator.FormatDuration(s.Duration),
                    s.Rate.ToString("0.##", CultureInfo.CurrentCulture),
                    Money(s.TimePay), Money(s.Extras), Money(s.Total)
                }, ShiftWeights, LeftColumns: 2));
            }

            if (day.Shifts.Count > 1)
            {
                rows.Add(new(RowKind.TableSubtotal, new[]
                {
                    string.Empty, L.T("Разом за день"), EarningsCalculator.FormatDuration(day.Worked), string.Empty,
                    Money(day.TimePay), Money(day.Extras), Money(day.Total)
                }, ShiftWeights, LeftColumns: 2));
            }
        }

        rows.Add(new(RowKind.TableTotal, new[]
        {
            L.T("Разом"), string.Empty, EarningsCalculator.FormatDuration(d.Worked), string.Empty,
            Money(d.TimePay), Money(d.ExtrasPay), Money(d.Total)
        }, ShiftWeights, LeftColumns: 2));

        if (d.Extras.Count > 0)
        {
            rows.Add(new(RowKind.Spacer, Array.Empty<string>()));
            rows.Add(new(RowKind.Heading, new[] { L.T("Допзаробіток по видах") }));
            rows.Add(new(RowKind.TableHeader,
                new[] { L.T("Вид"), L.T("Кількість"), L.T("Сума") }, ExtraWeights));

            foreach (var x in d.Extras)
                rows.Add(new(RowKind.TableRow, new[] { x.Label, x.Count.ToString(), Money(x.Amount) }, ExtraWeights));
        }

        return rows;
    }

    private static ReportRow Summary(string label, string value)
        => new(RowKind.Summary, new[] { label, value }, SummaryWeights);
}
