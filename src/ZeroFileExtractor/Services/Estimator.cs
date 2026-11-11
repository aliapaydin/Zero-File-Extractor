using ZeroFileExtractor.Engine;

namespace ZeroFileExtractor.Services;

/// <summary>
/// İş başlamadan önce süre ve boyut tahmini. Başlangıç değerleri tipik bir dizüstü için ölçülmüş
/// ortalamalardır; her biten işten sonra bu bilgisayarın gerçek hızı öğrenilip tahmin iyileştirilir.
/// </summary>
public static class Estimator
{
    /// <summary>Varsayılan sıkıştırma hızları (MB/sn), düzey 0–5.</summary>
    private static double BaselineCompress(CreateFormat format, int level)
    {
        level = Math.Clamp(level, 0, 5);
        double[] table = format switch
        {
            CreateFormat.Zip => [250, 70, 55, 35, 22, 15],
            CreateFormat.SevenZip => [12, 12, 8, 4.5, 3, 2],
            CreateFormat.Tar => [300, 300, 300, 300, 300, 300],
            CreateFormat.TarGz => [65, 65, 50, 32, 20, 14],
            CreateFormat.TarBz2 => [7, 7, 7, 7, 7, 7],
            CreateFormat.TarZst => [150, 150, 100, 40, 12, 4],
            CreateFormat.Rar => [300, 90, 60, 35, 28, 22],
            _ => [35, 35, 35, 35, 35, 35],
        };
        return table[level];
    }

    private static double BaselineExtract(ArchiveKind kind) => kind switch
    {
        ArchiveKind.Zip => 150,
        ArchiveKind.SevenZip => 60,
        ArchiveKind.Rar => 45,
        ArchiveKind.Tar => 300,
        ArchiveKind.TarGz or ArchiveKind.GZip => 140,
        ArchiveKind.TarBz2 or ArchiveKind.BZip2 => 20,
        ArchiveKind.TarXz or ArchiveKind.Xz => 60,
        ArchiveKind.TarZst or ArchiveKind.Zstd => 280,
        _ => 80,
    };

    private static string CompressKey(CreateFormat format, int level) => $"speed.c.{(int)format}.{Math.Clamp(level, 0, 5)}";
    private static string ExtractKey(ArchiveKind kind) => $"speed.x.{(int)kind}";

    public static double CompressSpeed(CreateFormat format, int level)
    {
        var learned = AppSettings.Current.GetDouble(CompressKey(format, level));
        return learned > 0 ? learned : BaselineCompress(format, level);
    }

    /// <summary>Bu hız, bu bilgisayarda ölçüldü mü (yoksa genel ortalama mı)?</summary>
    public static bool IsLearned(CreateFormat format, int level) => AppSettings.Current.GetDouble(CompressKey(format, level)) > 0;

    public static TimeSpan CompressTime(CreateFormat format, int level, long bytes, int fileCount)
    {
        double seconds = bytes / 1048576.0 / CompressSpeed(format, level) + fileCount * 0.0006;
        return TimeSpan.FromSeconds(Math.Max(1, seconds));
    }

    public static TimeSpan ExtractTime(ArchiveKind kind, long bytes, int fileCount)
    {
        var learned = AppSettings.Current.GetDouble(ExtractKey(kind));
        double speed = learned > 0 ? learned : BaselineExtract(kind);
        return TimeSpan.FromSeconds(Math.Max(1, bytes / 1048576.0 / speed + fileCount * 0.001));
    }

    public static void LearnCompress(CreateFormat format, int level, long bytes, TimeSpan elapsed) =>
        Learn(CompressKey(format, level), bytes, elapsed);

    public static void LearnExtract(ArchiveKind kind, long bytes, TimeSpan elapsed) =>
        Learn(ExtractKey(kind), bytes, elapsed);

    private static void Learn(string key, long bytes, TimeSpan elapsed)
    {
        // Çok küçük işler ölçüm için güvenilmez.
        if (bytes < 20 * 1048576 || elapsed.TotalSeconds < 1.5) return;
        double measured = bytes / 1048576.0 / elapsed.TotalSeconds;
        double previous = AppSettings.Current.GetDouble(key);
        AppSettings.Current.SetDouble(key, previous > 0 ? previous * 0.6 + measured * 0.4 : measured);
    }

    /// <summary>Dosya türlerine bakarak yaklaşık arşiv boyutu.</summary>
    public static long CompressedSize(IEnumerable<SourceItem> items, CreateFormat format, int level)
    {
        if (format == CreateFormat.Tar || (level <= 0 && ArchiveFormats.SupportsStore(format)))
            return items.Sum(i => i.Size);

        double formatFactor = format switch
        {
            CreateFormat.SevenZip => 0.78,
            CreateFormat.TarBz2 => 0.9,
            CreateFormat.Rar => 0.85,
            CreateFormat.TarZst => 0.95,
            _ => 1.0,
        };
        double levelFactor = Math.Clamp(level, 1, 5) switch { 1 => 1.18, 2 => 1.08, 3 => 1.0, 4 => 0.97, _ => 0.94 };

        double total = 0;
        foreach (var item in items)
        {
            if (item.IsDirectory) continue;
            var ext = Path.GetExtension(item.FullPath);
            double ratio;
            if (ArchiveFormats.IncompressibleExtensions.Contains(ext)) ratio = 0.985;
            else if (ArchiveFormats.TextLikeExtensions.Contains(ext)) ratio = Math.Min(0.95, 0.24 * formatFactor * levelFactor);
            else ratio = Math.Min(0.97, 0.58 * formatFactor * levelFactor);
            total += item.Size * ratio;
        }
        return (long)total;
    }
}
