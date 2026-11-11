using System.Globalization;

namespace ZeroFileExtractor.Services;

/// <summary>Boyut, süre ve tarih metinleri.</summary>
public static class Format
{
    public static string Size(long bytes)
    {
        if (bytes < 1024) return bytes.ToString("N0", CultureInfo.CurrentCulture) + " B";
        string[] units = ["KB", "MB", "GB", "TB"];
        double value = bytes;
        int unit = -1;
        do
        {
            value /= 1024;
            unit++;
        }
        while (value >= 1024 && unit < units.Length - 1);
        return value.ToString(value >= 100 ? "N0" : value >= 10 ? "N1" : "N2", CultureInfo.CurrentCulture) + " " + units[unit];
    }

    public static string Duration(TimeSpan time)
    {
        if (time.TotalSeconds < 60) return Loc.F("{0} sn", Math.Max(1, (int)Math.Round(time.TotalSeconds)));
        if (time.TotalMinutes < 60) return Loc.F("{0} dk {1} sn", (int)time.TotalMinutes, time.Seconds);
        return Loc.F("{0} sa {1} dk", (int)time.TotalHours, time.Minutes);
    }

    public static string Date(DateTime? time) =>
        time is { } t && t.Year > 1980 ? t.ToString("g", CultureInfo.CurrentCulture) : string.Empty;

    public static string Ratio(long compressed, long size) =>
        size <= 0 || compressed <= 0 ? string.Empty : (Math.Min(9.99, (double)compressed / size)).ToString("P0", CultureInfo.CurrentCulture);
}
