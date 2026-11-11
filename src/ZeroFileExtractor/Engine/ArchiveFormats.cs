namespace ZeroFileExtractor.Engine;

/// <summary>Bir arşivin nasıl açılacağını belirleyen tür.</summary>
public enum ArchiveKind
{
    Unknown,
    Zip,
    SevenZip,
    Rar,
    Tar,
    TarGz,
    TarBz2,
    TarXz,
    TarZst,
    TarLz,
    TarZ,
    GZip,
    BZip2,
    Xz,
    Zstd,
    LZip,
    Arj,
    Ace,
    Arc,
}

/// <summary>Oluşturulabilen arşiv biçimleri.</summary>
public enum CreateFormat
{
    Zip = 0,
    SevenZip = 1,
    Tar = 2,
    TarGz = 3,
    TarBz2 = 4,
    TarZst = 5,
    Rar = 6,
}

/// <summary>Uzantı ↔ biçim eşlemeleri ve ad yardımcıları.</summary>
public static class ArchiveFormats
{
    // Uzun (çift) uzantılar önce denenir.
    private static readonly (string Ext, ArchiveKind Kind)[] Map =
    [
        (".tar.gz", ArchiveKind.TarGz), (".tar.bz2", ArchiveKind.TarBz2), (".tar.xz", ArchiveKind.TarXz),
        (".tar.zst", ArchiveKind.TarZst), (".tar.lz", ArchiveKind.TarLz), (".tar.z", ArchiveKind.TarZ),
        (".tgz", ArchiveKind.TarGz), (".tbz2", ArchiveKind.TarBz2), (".tbz", ArchiveKind.TarBz2),
        (".txz", ArchiveKind.TarXz), (".tzst", ArchiveKind.TarZst), (".tlz", ArchiveKind.TarLz),
        (".zip", ArchiveKind.Zip), (".zipx", ArchiveKind.Zip), (".jar", ArchiveKind.Zip), (".war", ArchiveKind.Zip),
        (".apk", ArchiveKind.Zip), (".ipa", ArchiveKind.Zip), (".epub", ArchiveKind.Zip), (".cbz", ArchiveKind.Zip),
        (".nupkg", ArchiveKind.Zip), (".vsix", ArchiveKind.Zip), (".xpi", ArchiveKind.Zip),
        (".7z", ArchiveKind.SevenZip),
        (".rar", ArchiveKind.Rar), (".cbr", ArchiveKind.Rar),
        (".tar", ArchiveKind.Tar),
        (".gz", ArchiveKind.GZip), (".bz2", ArchiveKind.BZip2), (".xz", ArchiveKind.Xz),
        (".zst", ArchiveKind.Zstd), (".lz", ArchiveKind.LZip),
        (".arj", ArchiveKind.Arj), (".ace", ArchiveKind.Ace), (".arc", ArchiveKind.Arc),
    ];

    /// <summary>Dosya ilişkilendirmesi ve "Aç" penceresi için uzantılar (noktalı).</summary>
    public static readonly string[] OpenExtensions =
    [
        ".zip", ".zipx", ".7z", ".rar", ".tar", ".gz", ".tgz", ".bz2", ".tbz2", ".xz", ".txz",
        ".zst", ".tzst", ".lz", ".arj", ".ace", ".arc", ".jar", ".war", ".apk", ".epub", ".cbz", ".cbr",
        ".nupkg", ".vsix",
    ];

    public static ArchiveKind KindOf(string path)
    {
        var name = Path.GetFileName(path).ToLowerInvariant();
        foreach (var (ext, kind) in Map)
            if (name.EndsWith(ext, StringComparison.Ordinal)) return kind;

        // Bölünmüş arşivler: .001 (7z/zip), .r00, .part1.rar zaten .rar ile biter.
        var last = Path.GetExtension(name);
        if (last.Length == 4 && last[1] == 'r' && char.IsDigit(last[2]) && char.IsDigit(last[3])) return ArchiveKind.Rar;
        if (last == ".001")
        {
            var inner = Path.GetFileNameWithoutExtension(name);
            if (inner.EndsWith(".7z", StringComparison.Ordinal)) return ArchiveKind.SevenZip;
            if (inner.EndsWith(".zip", StringComparison.Ordinal)) return ArchiveKind.Zip;
        }
        return ArchiveKind.Unknown;
    }

    public static bool IsArchivePath(string path) => KindOf(path) != ArchiveKind.Unknown;

    /// <summary>"rapor.tar.gz" → "rapor"; arşiv değilse yalnızca son uzantı atılır.</summary>
    public static string StripExtension(string fileName)
    {
        var lower = fileName.ToLowerInvariant();
        foreach (var (ext, _) in Map)
            if (lower.EndsWith(ext, StringComparison.Ordinal) && fileName.Length > ext.Length)
                return fileName[..^ext.Length];
        var stripped = Path.GetFileNameWithoutExtension(fileName);
        // "arsiv.part1" → "arsiv"
        var lowerStripped = stripped.ToLowerInvariant();
        int part = lowerStripped.LastIndexOf(".part", StringComparison.Ordinal);
        if (part > 0 && lowerStripped[(part + 5)..].All(char.IsDigit) && lowerStripped.Length > part + 5)
            stripped = stripped[..part];
        return stripped.Length == 0 ? fileName : stripped;
    }

    public static string DisplayName(ArchiveKind kind) => kind switch
    {
        ArchiveKind.Zip => "ZIP",
        ArchiveKind.SevenZip => "7Z",
        ArchiveKind.Rar => "RAR",
        ArchiveKind.Tar => "TAR",
        ArchiveKind.TarGz => "TAR.GZ",
        ArchiveKind.TarBz2 => "TAR.BZ2",
        ArchiveKind.TarXz => "TAR.XZ",
        ArchiveKind.TarZst => "TAR.ZST",
        ArchiveKind.TarLz => "TAR.LZ",
        ArchiveKind.TarZ => "TAR.Z",
        ArchiveKind.GZip => "GZIP",
        ArchiveKind.BZip2 => "BZIP2",
        ArchiveKind.Xz => "XZ",
        ArchiveKind.Zstd => "ZSTD",
        ArchiveKind.LZip => "LZIP",
        ArchiveKind.Arj => "ARJ",
        ArchiveKind.Ace => "ACE",
        ArchiveKind.Arc => "ARC",
        _ => "?",
    };

    public static string Extension(CreateFormat format) => format switch
    {
        CreateFormat.Zip => ".zip",
        CreateFormat.SevenZip => ".7z",
        CreateFormat.Tar => ".tar",
        CreateFormat.TarGz => ".tar.gz",
        CreateFormat.TarBz2 => ".tar.bz2",
        CreateFormat.TarZst => ".tar.zst",
        CreateFormat.Rar => ".rar",
        _ => ".zip",
    };

    public static string DisplayName(CreateFormat format) => Extension(format)[1..].ToUpperInvariant();

    /// <summary>Biçim parola ile şifrelemeyi destekliyor mu?</summary>
    public static bool SupportsPassword(CreateFormat format) => format is CreateFormat.Zip or CreateFormat.Rar;

    /// <summary>Biçimde sıkıştırma düzeyi seçilebiliyor mu?</summary>
    public static bool SupportsLevel(CreateFormat format) => format is not (CreateFormat.Tar or CreateFormat.TarBz2);

    /// <summary>Biçim "Depola" (sıkıştırmasız) düzeyini destekliyor mu?</summary>
    public static bool SupportsStore(CreateFormat format) => format is CreateFormat.Zip or CreateFormat.Rar;

    /// <summary>Zaten sıkıştırılmış olduğu için küçülmeyen dosya türleri (boyut tahmini için).</summary>
    public static readonly HashSet<string> IncompressibleExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".zip", ".7z", ".rar", ".gz", ".bz2", ".xz", ".zst", ".tgz", ".cab", ".jar", ".apk", ".msix", ".appx",
        ".jpg", ".jpeg", ".png", ".gif", ".webp", ".heic", ".avif",
        ".mp3", ".aac", ".m4a", ".ogg", ".opus", ".flac", ".wma",
        ".mp4", ".mkv", ".mov", ".avi", ".webm", ".wmv", ".m4v",
        ".pdf", ".docx", ".xlsx", ".pptx", ".epub", ".pbix",
    };

    /// <summary>Metin gibi çok iyi sıkışan türler.</summary>
    public static readonly HashSet<string> TextLikeExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".txt", ".log", ".csv", ".tsv", ".json", ".xml", ".html", ".htm", ".css", ".js", ".ts", ".md",
        ".cs", ".py", ".java", ".c", ".cpp", ".h", ".sql", ".yml", ".yaml", ".ini", ".svg", ".xaml", ".ps1", ".bat",
    };
}
