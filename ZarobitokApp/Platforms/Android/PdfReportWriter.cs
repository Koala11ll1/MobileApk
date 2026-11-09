using Android.Graphics;
using Android.Graphics.Pdf;
using ZarobitokApp.Localization;
using ZarobitokApp.Services;

namespace ZarobitokApp.Platforms.Android;

/// <summary>
/// Малює рядки звіту (див. ReportRows) у PDF вбудованим android.graphics.pdf — без сторонніх пакетів.
/// Сторінка A4, рядки фіксованої висоти; номер сторінки "i з N" відомий наперед, бо спершу розкладаємо рядки.
/// </summary>
public static class PdfReportWriter
{
    private const int PageWidth = 595, PageHeight = 842, Margin = 36, FooterSpace = 28;

    public static void Write(IReadOnlyList<ReportRow> rows, string path)
    {
        var pages = Paginate(rows);

        using var doc = new PdfDocument();
        var body = new Paint(PaintFlags.AntiAlias) { TextSize = 9.5f, Color = Color.Rgb(30, 30, 30) };
        var bold = new Paint(PaintFlags.AntiAlias) { TextSize = 9.5f, Color = Color.Rgb(30, 30, 30) };
        bold.SetTypeface(Typeface.DefaultBold);
        var fill = new Paint { Color = Color.White };

        for (var p = 0; p < pages.Count; p++)
        {
            var info = new PdfDocument.PageInfo.Builder(PageWidth, PageHeight, p + 1).Create()!;
            var page = doc.StartPage(info)!;
            var canvas = page.Canvas!;

            var y = (float)Margin;
            var stripe = false;
            foreach (var row in pages[p])
            {
                var h = Height(row.Kind);
                Draw(canvas, row, y, h, body, bold, fill, ref stripe);
                y += h;
            }

            var footer = new Paint(PaintFlags.AntiAlias) { TextSize = 8f, Color = Color.Rgb(120, 120, 120) };
            canvas.DrawText($"{L.T("Заробіток")} · {DateTime.Now:dd.MM.yyyy HH:mm}", Margin, PageHeight - 20, footer);
            footer.TextAlign = Paint.Align.Right;
            canvas.DrawText(L.F("Стор. {0} з {1}", p + 1, pages.Count), PageWidth - Margin, PageHeight - 20, footer);

            doc.FinishPage(page);
        }

        using var stream = File.Create(path);
        doc.WriteTo(stream);
    }

    /// <summary>Розкладає рядки по сторінках; якщо таблиця переходить на нову сторінку — повторює її шапку.</summary>
    private static List<List<ReportRow>> Paginate(IReadOnlyList<ReportRow> rows)
    {
        var pages = new List<List<ReportRow>> { new() };
        var used = 0f;
        var limit = PageHeight - Margin * 2 - FooterSpace;
        ReportRow? header = null;

        foreach (var row in rows)
        {
            if (row.Kind == RowKind.TableHeader) header = row;
            else if (row.Kind is not (RowKind.TableRow or RowKind.TableSubtotal or RowKind.TableTotal)) header = null;

            var h = Height(row.Kind);
            if (used + h > limit && pages[^1].Count > 0)
            {
                pages.Add(new List<ReportRow>());
                used = 0;

                // Рядок таблиці почав нову сторінку — спершу повторюємо шапку.
                if (header is not null && row.Kind != RowKind.TableHeader)
                {
                    pages[^1].Add(header);
                    used += Height(RowKind.TableHeader);
                }
            }

            pages[^1].Add(row);
            used += h;
        }

        return pages;
    }

    private static float Height(RowKind kind) => kind switch
    {
        RowKind.Title => 30,
        RowKind.Subtitle => 18,
        RowKind.Heading => 24,
        RowKind.Summary => 15,
        RowKind.TableHeader => 18,
        RowKind.TableRow => 14.5f,
        RowKind.TableSubtotal => 14.5f,
        RowKind.TableTotal => 18,
        RowKind.Spacer => 10,
        _ => 16
    };

    private static void Draw(Canvas canvas, ReportRow row, float y, float h, Paint body, Paint bold, Paint fill, ref bool stripe)
    {
        var left = (float)Margin;
        var width = PageWidth - Margin * 2;
        var baseline = y + h - 4.5f;

        switch (row.Kind)
        {
            case RowKind.Title:
                bold.TextSize = 20;
                canvas.DrawText(row.Cells[0], left, y + 21, bold);
                bold.TextSize = 9.5f;
                break;

            case RowKind.Subtitle:
                body.Color = Color.Rgb(100, 100, 100);
                body.TextSize = 11;
                canvas.DrawText(row.Cells[0], left, y + 13, body);
                body.Color = Color.Rgb(30, 30, 30);
                body.TextSize = 9.5f;
                break;

            case RowKind.Heading:
                bold.TextSize = 12;
                canvas.DrawText(row.Cells[0], left, y + 18, bold);
                bold.TextSize = 9.5f;
                break;

            case RowKind.Body:
                canvas.DrawText(row.Cells[0], left, baseline, body);
                break;

            case RowKind.Summary:
                canvas.DrawText(Fit(row.Cells[0], body, width * 0.6f), left, baseline, body);
                bold.TextAlign = Paint.Align.Right;
                canvas.DrawText(row.Cells[1], left + width * 0.62f, baseline, bold);
                bold.TextAlign = Paint.Align.Left;
                break;

            case RowKind.TableHeader:
                fill.Color = Color.Rgb(45, 55, 72);
                canvas.DrawRect(left, y, left + width, y + h, fill);
                DrawCells(canvas, row, left, width, baseline, bold, Color.White);
                stripe = false;
                break;

            case RowKind.TableRow:
                if (stripe)
                {
                    fill.Color = Color.Rgb(241, 243, 246);
                    canvas.DrawRect(left, y, left + width, y + h, fill);
                }
                DrawCells(canvas, row, left, width, baseline, body, Color.Rgb(30, 30, 30));
                stripe = !stripe;
                break;

            case RowKind.TableSubtotal:
                fill.Color = Color.Rgb(226, 232, 240);
                canvas.DrawRect(left, y, left + width, y + h, fill);
                DrawCells(canvas, row, left, width, baseline, bold, Color.Rgb(30, 30, 30));
                stripe = false;
                break;

            case RowKind.TableTotal:
                fill.Color = Color.Rgb(203, 213, 225);
                canvas.DrawRect(left, y, left + width, y + h, fill);
                DrawCells(canvas, row, left, width, baseline, bold, Color.Rgb(20, 20, 20));
                stripe = false;
                break;
        }
    }

    private static void DrawCells(Canvas canvas, ReportRow row, float left, float width, float baseline, Paint paint, Color color)
    {
        var weights = row.Weights!;
        var total = weights.Sum();
        var x = left;
        paint.Color = color;

        for (var i = 0; i < row.Cells.Length; i++)
        {
            var w = width * weights[i] / total;
            var text = Fit(row.Cells[i], paint, w - 6);

            if (i < row.LeftColumns)
            {
                paint.TextAlign = Paint.Align.Left;
                canvas.DrawText(text, x + 3, baseline, paint);
            }
            else
            {
                paint.TextAlign = Paint.Align.Right;
                canvas.DrawText(text, x + w - 3, baseline, paint);
            }

            x += w;
        }

        paint.TextAlign = Paint.Align.Left;
        paint.Color = Color.Rgb(30, 30, 30);
    }

    /// <summary>Скорочує текст з "…", щоб він не заліз на сусідню комірку.</summary>
    private static string Fit(string text, Paint paint, float maxWidth)
    {
        if (paint.MeasureText(text) <= maxWidth) return text;

        for (var n = text.Length - 1; n > 0; n--)
        {
            var candidate = text[..n] + "…";
            if (paint.MeasureText(candidate) <= maxWidth) return candidate;
        }

        return "…";
    }
}
