namespace ZarobitokApp.Services;

/// <summary>Записує звіт у PDF-файл. На Android — вбудованим PdfDocument.</summary>
public static class ReportExporter
{
    public static void WritePdf(IReadOnlyList<ReportRow> rows, string path)
    {
#if ANDROID
        global::ZarobitokApp.Platforms.Android.PdfReportWriter.Write(rows, path);
#else
        throw new PlatformNotSupportedException("PDF reports are only available on Android.");
#endif
    }
}
