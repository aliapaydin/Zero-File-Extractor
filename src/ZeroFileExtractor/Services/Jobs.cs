using ZeroFileExtractor.Engine;

namespace ZeroFileExtractor.Services;

/// <summary>Kullanıcıya olduğu gibi gösterilecek hata.</summary>
public sealed class UserMessageException(string message) : Exception(message);

/// <summary>İş penceresinde sırayla çalışan tek bir iş.</summary>
public abstract class Job
{
    /// <summary>"Çıkarılıyor: rapor.zip" gibi başlık.</summary>
    public abstract string Title { get; }

    /// <summary>Başlamadan önceki süre tahmini (varsa).</summary>
    public virtual TimeSpan? Estimate => null;

    /// <summary>Arka plan iş parçacığında çalışır.</summary>
    public abstract void Run(JobContext context);

    /// <summary>Bitince gösterilecek özet.</summary>
    public string Summary { get; protected set; } = string.Empty;

    /// <summary>"Klasörü aç" düğmesi için.</summary>
    public string? ResultFolder { get; protected set; }

    /// <summary>Oluşan arşiv ("Arşivi aç" düğmesi için).</summary>
    public string? ResultFile { get; protected set; }

    /// <summary>Başarıyla bitince sonuç klasörü Dosya Gezgini'nde açılsın.</summary>
    public bool OpenFolderWhenDone { get; init; }

    /// <summary>Parola sorarak arşivi açar.</summary>
    protected static ArchiveSession OpenWithPrompt(string path, JobContext context)
    {
        string? password = null;
        while (true)
        {
            try
            {
                return ArchiveSession.Open(path, password, context.Cancellation);
            }
            catch (PasswordRequiredException ex)
            {
                password = context.RequestPassword?.Invoke(ex.Wrong) ?? throw new OperationCanceledException();
            }
        }
    }
}

public sealed class ExtractJob : Job
{
    public required string ArchivePath { get; init; }

    /// <summary>Zaten açık oturum (ana pencereden); yoksa iş kendisi açar.</summary>
    public ArchiveSession? Session { get; set; }

    /// <summary>null = tüm arşiv.</summary>
    public IReadOnlyCollection<ArchiveEntryInfo>? Selection { get; init; }

    public required string Destination { get; init; }
    public ExtractFolderMode FolderMode { get; init; } = ExtractFolderMode.Direct;
    public ExtractOptions Options { get; init; } = new();
    public bool DeleteArchive { get; init; }

    public override string Title => Loc.F("Çıkarılıyor: {0}", Path.GetFileName(ArchivePath));

    public override void Run(JobContext context)
    {
        context.SetCurrent(Loc.T("Arşiv okunuyor…"));
        var session = Session ??= OpenWithPrompt(ArchivePath, context);

        var destination = Destination;
        if (Selection is null)
        {
            bool named = FolderMode == ExtractFolderMode.NamedFolder ||
                         (FolderMode == ExtractFolderMode.Smart && session.RootNames().Count != 1);
            if (named)
                destination = Path.Combine(destination, ArchiveSession.SanitizeRelative(ArchiveFormats.StripExtension(Path.GetFileName(session.FilePath))));
        }

        var started = DateTime.UtcNow;
        session.Extract(Selection, destination, Options, context);
        var elapsed = DateTime.UtcNow - started;
        var progress = context.Snapshot();
        Estimator.LearnExtract(session.Kind, progress.BytesDone, elapsed);

        ResultFolder = destination;
        Summary = Loc.F("{0} dosya çıkarıldı ({1})", progress.FilesDone - context.Skipped, Format.Size(progress.BytesDone));
        if (context.Skipped > 0) Summary += " · " + Loc.F("{0} öğe atlandı", context.Skipped);

        if (DeleteArchive && Selection is null && context.Skipped == 0 && context.Warnings.Count == 0 && !session.IsMultiVolume)
            RecycleBin.Send([session.FilePath]);
    }
}

public sealed class TestJob : Job
{
    public required string ArchivePath { get; init; }
    public ArchiveSession? Session { get; set; }

    public override string Title => Loc.F("Test ediliyor: {0}", Path.GetFileName(ArchivePath));

    public override void Run(JobContext context)
    {
        context.SetCurrent(Loc.T("Arşiv okunuyor…"));
        var session = Session ??= OpenWithPrompt(ArchivePath, context);
        int count = session.Test(context);
        Summary = Loc.F("Arşiv sağlam: {0} dosya doğrulandı", count);
    }
}

public sealed class CompressJob : Job
{
    public required IReadOnlyList<string> Sources { get; init; }
    public required CreateOptions Options { get; init; }
    public bool DeleteSources { get; init; }

    /// <summary>Pencere açılmadan önce hesaplanmış toplam (tahmin için).</summary>
    public long KnownBytes { get; init; }
    public int KnownFiles { get; init; }

    public override string Title => Loc.F("Sıkıştırılıyor: {0}", Path.GetFileName(Options.OutputPath));

    public override TimeSpan? Estimate =>
        KnownBytes > 0 ? Estimator.CompressTime(Options.Format, Options.Level, KnownBytes, KnownFiles) : null;

    public override void Run(JobContext context)
    {
        context.SetCurrent(Loc.T("Dosyalar sayılıyor…"));
        var items = ArchiveWriter.Enumerate(Sources, context, Options.OutputPath);
        if (items.Count == 0) throw new UserMessageException(Loc.T("Sıkıştırılacak dosya bulunamadı."));

        var started = DateTime.UtcNow;
        ArchiveWriter.Create(items, Options, context);
        var elapsed = DateTime.UtcNow - started;
        long total = context.BytesTotal;
        Estimator.LearnCompress(Options.Format, Options.Level, total, elapsed);

        ResultFile = Options.OutputPath;
        ResultFolder = Path.GetDirectoryName(Options.OutputPath);
        long packed = new FileInfo(Options.OutputPath).Length;
        Summary = Loc.F("{0} → {1} ({2})", Format.Size(total), Format.Size(packed), Format.Ratio(packed, total));
        if (context.Skipped > 0) Summary += " · " + Loc.F("{0} öğe atlandı", context.Skipped);

        if (DeleteSources && context.Skipped == 0 && context.Warnings.Count == 0)
            RecycleBin.Send(Sources);
    }
}

public sealed class ZipAddJob : Job
{
    public required string ZipPath { get; init; }
    public required IReadOnlyList<string> Sources { get; init; }
    public string TargetFolder { get; init; } = string.Empty;
    public int Level { get; init; } = 3;

    public override string Title => Loc.F("Ekleniyor: {0}", Path.GetFileName(ZipPath));

    public override void Run(JobContext context)
    {
        context.SetCurrent(Loc.T("Dosyalar sayılıyor…"));
        var items = ArchiveWriter.Enumerate(Sources, context, ZipPath);
        ZipEditor.Add(ZipPath, items, TargetFolder, Level, context);
        Summary = Loc.F("{0} dosya eklendi", context.Snapshot().FilesDone);
    }
}

public sealed class ZipDeleteJob : Job
{
    public required string ZipPath { get; init; }
    public required IReadOnlyCollection<string> Files { get; init; }
    public required IReadOnlyCollection<string> Folders { get; init; }

    public override string Title => Loc.F("Siliniyor: {0}", Path.GetFileName(ZipPath));

    public override void Run(JobContext context)
    {
        ZipEditor.Delete(ZipPath, Files, Folders, context);
        Summary = Loc.F("{0} öğe silindi", context.Snapshot().FilesDone);
    }
}
