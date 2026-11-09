using ZarobitokApp.Localization;
using System.Globalization;
using System.Text;
using System.Text.Json;
using ZarobitokApp.Models;

namespace ZarobitokApp.Services;

/// <summary>Усі дані апки одним об'єктом — для резервної копії.</summary>
public sealed record BackupData(
    int Version,
    ShiftState State,
    List<ShiftLogEntry> Log,
    List<ExtraItem> Items,
    List<ExtraTick> Ticks);

/// <summary>Експорт у CSV і резервна копія в JSON. Без залежностей від MAUI.</summary>
public static class DataExport
{
    public const int CurrentVersion = 1;

    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    public static string ToBackupJson(
        ShiftState state, List<ShiftLogEntry> log, List<ExtraItem> items, List<ExtraTick> ticks)
        => JsonSerializer.Serialize(new BackupData(CurrentVersion, state, log, items, ticks), Json);

    /// <summary>
    /// Розбирає й перевіряє резервну копію. Повертає null, якщо файл не наш,
    /// пошкоджений або містить значення, що поламають апку (див. межі налаштувань).
    /// </summary>
    public static BackupData? FromBackupJson(string json)
    {
        try
        {
            var data = JsonSerializer.Deserialize<BackupData>(json, Json);
            if (data is null || data.Version != CurrentVersion
                || data.State is null || data.Log is null || data.Items is null || data.Ticks is null)
                return null;

            var s = data.State;
            if (s.RatePerHour is < 0 or > Limits.MaxRate
                || s.PlannedHours is <= 0 or > Limits.MaxPlannedHours
                || s.UnpaidBreakMinutes is < 0 or > Limits.MaxBreakMinutes
                || s.NightStartHour is < 0 or > 23
                || s.NightEndHour is < 0 or > 23
                || s.ReminderMinutes is < 0 or >= 24 * 60)
                return null;

            return data;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// CSV для Excel/Sheets: роздільник ';' (так він відкривається в українській
    /// локалі), числа з крапкою, дати у місцевому часі.
    /// </summary>
    public static string ToCsv(
        IEnumerable<ShiftLogEntry> log, IEnumerable<ExtraItem> items, IEnumerable<ExtraTick> ticks,
        string currency)
    {
        var itemList = items.ToList();
        var tickList = ticks.ToList();
        var inv = CultureInfo.InvariantCulture;

        var sb = new StringBuilder();
        sb.AppendLine(L.F("Дата;Початок;Кінець;Годин;Ставка ({0}/год);За час;Допзаробіток;Разом", Clean(currency)));

        foreach (var e in log.OrderBy(x => x.StartedAtUtc))
        {
            var start = e.StartedAtUtc.ToLocalTime();
            var end = e.EndedAtUtc.ToLocalTime();
            var extras = EarningsCalculator.TicksTotal(itemList, tickList, e.StartedAtUtc, e.EndedAtUtc);

            sb.AppendLine(string.Join(';',
                end.ToString("yyyy-MM-dd", inv),
                start.ToString("yyyy-MM-dd HH:mm", inv),
                end.ToString("yyyy-MM-dd HH:mm", inv),
                e.Duration.TotalHours.ToString("0.00", inv),
                e.RatePerHour.ToString("0.##", inv),
                e.Earned.ToString("0.00", inv),
                extras.ToString("0.00", inv),
                (e.Earned + extras).ToString("0.00", inv)));
        }

        return sb.ToString();
    }

    // Валюта — довільний текст користувача; ';' чи перенос зламали б колонки.
    private static string Clean(string s) => s.Replace(';', ',').Replace('\n', ' ').Replace('\r', ' ');
}

/// <summary>Допустимі межі налаштувань — одні й ті самі для екрана й для відновлення з копії.</summary>
public static class Limits
{
    public const decimal MaxRate = 1_000_000m;
    public const double MaxPlannedHours = 48;
    public const int MaxBreakMinutes = 24 * 60;
}
