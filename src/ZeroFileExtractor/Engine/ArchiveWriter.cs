using ICSharpCode.SharpZipLib.Zip;
using SharpCompress.Common;
using SharpCompress.Compressors.LZMA;
using SharpCompress.Writers.SevenZip;
using SharpCompress.Writers.Tar;

namespace ZeroFileExtractor.Engine;

public sealed class CreateOptions
{
    public CreateFormat Format { get; set; } = CreateFormat.Zip;

    /// <summary>0 Depola · 1 En hızlı · 2 Hızlı · 3 Normal · 4 İyi · 5 En iyi.</summary>
    public int Level { get; set; } = 3;

    public string? Password { get; set; }
    public required string OutputPath { get; set; }
}

/// <summary>Arşive girecek tek bir kaynak.</summary>
public sealed record SourceItem(string FullPath, string EntryName, bool IsDirectory, long Size, DateTime Modified);

/// <summary>Yeni arşiv oluşturur (ZIP, 7Z, TAR ailesi; WinRAR kuruluysa RAR).</summary>
public static class ArchiveWriter
{
    /// <summary>Kaynakları (dosya ve klasörler) alt öğeleriyle birlikte listeler.</summary>
    public static List<SourceItem> Enumerate(IReadOnlyList<string> sources, JobContext? context = null, string? exclude = null)
    {
        var items = new List<SourceItem>();
        var usedRoots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var excluded = exclude is null ? null : Path.GetFullPath(exclude);

        foreach (var raw in sources)
        {
            context?.Checkpoint();
            var source = Path.GetFullPath(raw).TrimEnd(Path.DirectorySeparatorChar);
            var rootName = Path.GetFileName(source);
            if (string.IsNullOrEmpty(rootName)) rootName = source.Replace(":", string.Empty).Trim(Path.DirectorySeparatorChar); // sürücü kökü
            var unique = rootName;
            for (int i = 2; !usedRoots.Add(unique); i++) unique = $"{rootName} ({i})";

            if (File.Exists(source))
            {
                if (excluded is not null && string.Equals(source, excluded, StringComparison.OrdinalIgnoreCase)) continue;
                var info = new FileInfo(source);
                items.Add(new SourceItem(source, unique, false, info.Length, info.LastWriteTime));
            }
            else if (Directory.Exists(source))
            {
                AddDirectory(items, new DirectoryInfo(source), unique, context, excluded);
            }
        }
        return items;
    }

    private static void AddDirectory(List<SourceItem> items, DirectoryInfo directory, string entryName, JobContext? context, string? excluded)
    {
        context?.Checkpoint();
        items.Add(new SourceItem(directory.FullName, entryName, true, 0, directory.LastWriteTime));
        IEnumerable<FileSystemInfo> children;
        try
        {
            children = directory.EnumerateFileSystemInfos().ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            context?.Warnings.Add(directory.FullName);
            return;
        }
        foreach (var child in children)
        {
            // Bağlantı noktalarını izleme (sonsuz döngü ve yinelenen içerik riski).
            if ((child.Attributes & FileAttributes.ReparsePoint) != 0 && child is DirectoryInfo) continue;
            if (child is DirectoryInfo sub)
            {
                AddDirectory(items, sub, entryName + "/" + sub.Name, context, excluded);
            }
            else if (child is FileInfo file)
            {
                if (excluded is not null && string.Equals(file.FullName, excluded, StringComparison.OrdinalIgnoreCase)) continue;
                long length;
                try
                {
                    length = file.Length;
                }
                catch (IOException)
                {
                    continue;
                }
                items.Add(new SourceItem(file.FullName, entryName + "/" + file.Name, false, length, file.LastWriteTime));
            }
        }
    }

    /// <summary>Arşivi geçici bir dosyaya yazar, başarıyla bitince hedef ada taşır.</summary>
    public static void Create(IReadOnlyList<SourceItem> items, CreateOptions options, JobContext context)
    {
        context.BytesTotal = items.Sum(i => i.Size);
        context.FilesTotal = items.Count(i => !i.IsDirectory);

        var output = Path.GetFullPath(options.OutputPath);
        Directory.CreateDirectory(Path.GetDirectoryName(output)!);

        if (options.Format == CreateFormat.Rar)
        {
            RarExe.Create(items, options, context);
            return;
        }

        var temp = output + ".zfxtmp";
        bool complete = false;
        try
        {
            using (var stream = new FileStream(temp, FileMode.Create, FileAccess.ReadWrite, FileShare.None, 1 << 16))
            {
                switch (options.Format)
                {
                    case CreateFormat.Zip:
                        WriteZip(stream, items, options, context);
                        break;
                    case CreateFormat.SevenZip:
                        WriteSevenZip(stream, items, options, context);
                        break;
                    default:
                        WriteTar(stream, items, options, context);
                        break;
                }
            }
            context.Checkpoint();
            File.Move(temp, output, overwrite: true);
            complete = true;
        }
        finally
        {
            if (!complete)
            {
                try
                {
                    if (File.Exists(temp)) File.Delete(temp);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    // yarım dosya silinemedi
                }
            }
        }
    }

    private static Stream? OpenSource(SourceItem item, JobContext context)
    {
        try
        {
            var file = new FileStream(item.FullPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 1 << 16, FileOptions.SequentialScan);
            return new CountingReadStream(file, context);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Kilitli ya da erişilemeyen dosya: atla, sonunda bildir.
            context.Warnings.Add(item.FullPath);
            context.Skipped++;
            context.AddBytes(item.Size);
            return null;
        }
    }

    // ------------------------------------------------------------------ ZIP

    private static void WriteZip(Stream output, IReadOnlyList<SourceItem> items, CreateOptions options, JobContext context)
    {
        int level = options.Level switch { <= 0 => 0, 1 => 1, 2 => 3, 3 => 6, 4 => 8, _ => 9 };
        bool encrypt = !string.IsNullOrEmpty(options.Password);

        using var zip = new ZipOutputStream(output) { IsStreamOwner = false };
        zip.SetLevel(level);
        zip.UseZip64 = UseZip64.Dynamic;
        if (encrypt) zip.Password = options.Password;

        foreach (var item in items)
        {
            context.Checkpoint();
            if (item.IsDirectory)
            {
                zip.PutNextEntry(new ZipEntry(item.EntryName + "/") { DateTime = ZipTime(item.Modified), IsUnicodeText = true });
                zip.CloseEntry();
                continue;
            }

            context.SetCurrent(item.EntryName);
            using var source = OpenSource(item, context);
            if (source is null) continue;

            var entry = new ZipEntry(item.EntryName)
            {
                DateTime = ZipTime(item.Modified),
                IsUnicodeText = true,
                Size = source.Length,
            };
            if (level == 0) entry.CompressionMethod = CompressionMethod.Stored;
            if (encrypt) entry.AESKeySize = 256;
            zip.PutNextEntry(entry);
            source.CopyTo(zip, 1 << 17);
            zip.CloseEntry();
            context.FileDone();
        }
        zip.Finish();
    }

    private static DateTime ZipTime(DateTime time) =>
        time.Year < 1980 ? new DateTime(1980, 1, 1) : time.Year > 2107 ? new DateTime(2107, 12, 31) : time;

    // ------------------------------------------------------------------ 7Z

    private static void WriteSevenZip(Stream output, IReadOnlyList<SourceItem> items, CreateOptions options, JobContext context)
    {
        // Düzey → sözlük boyutu ve "fast bytes". Büyük sözlük daha iyi sıkıştırır ama daha çok bellek ister.
        var (dictionary, fastBytes) = options.Level switch
        {
            <= 1 => (1 << 18, 32),
            2 => (1 << 20, 32),
            3 => (1 << 23, 32),
            4 => (1 << 24, 64),
            _ => (1 << 25, 128),
        };
        var writerOptions = new SevenZipWriterOptions(CompressionType.LZMA2)
        {
            LzmaProperties = new LzmaEncoderProperties(false, dictionary, fastBytes),
        };

        using var writer = new SevenZipWriter(output, writerOptions);
        foreach (var item in items)
        {
            context.Checkpoint();
            if (item.IsDirectory)
            {
                writer.WriteDirectory(item.EntryName, item.Modified);
                continue;
            }
            context.SetCurrent(item.EntryName);
            using var source = OpenSource(item, context);
            if (source is null) continue;
            writer.Write(item.EntryName, source, item.Modified);
            context.FileDone();
        }
    }

    // ------------------------------------------------------------------ TAR ailesi

    private static void WriteTar(Stream output, IReadOnlyList<SourceItem> items, CreateOptions options, JobContext context)
    {
        Stream target = output;
        Stream? zstd = null;
        TarWriterOptions tarOptions;
        switch (options.Format)
        {
            case CreateFormat.TarGz:
                tarOptions = new TarWriterOptions(CompressionType.GZip, true)
                {
                    CompressionLevel = options.Level switch { <= 1 => 1, 2 => 3, 3 => 6, 4 => 8, _ => 9 },
                };
                break;
            case CreateFormat.TarBz2:
                tarOptions = new TarWriterOptions(CompressionType.BZip2, true);
                break;
            case CreateFormat.TarZst:
                int level = options.Level switch { <= 1 => 1, 2 => 3, 3 => 9, 4 => 15, _ => 19 };
                zstd = new SharpCompress.Compressors.ZStandard.CompressionStream(output, level, 0, true);
                target = zstd;
                tarOptions = new TarWriterOptions(CompressionType.None, true);
                break;
            default:
                tarOptions = new TarWriterOptions(CompressionType.None, true);
                break;
        }

        try
        {
            using var writer = new TarWriter(target, tarOptions);
            foreach (var item in items)
            {
                context.Checkpoint();
                if (item.IsDirectory)
                {
                    writer.WriteDirectory(item.EntryName, item.Modified);
                    continue;
                }
                context.SetCurrent(item.EntryName);
                using var source = OpenSource(item, context);
                if (source is null) continue;
                writer.Write(item.EntryName, source, item.Modified, source.Length);
                context.FileDone();
            }
        }
        finally
        {
            zstd?.Dispose();
        }
    }

    // ------------------------------------------------------------------ adlandırma

    /// <summary>Kaynaklara göre önerilen arşiv adı (uzantısız) ve klasörü.</summary>
    public static (string Folder, string Name) SuggestName(IReadOnlyList<string> sources)
    {
        var first = Path.GetFullPath(sources[0]).TrimEnd(Path.DirectorySeparatorChar);
        var folder = Path.GetDirectoryName(first) ?? first;
        string name;
        if (sources.Count == 1)
        {
            name = Directory.Exists(first) ? Path.GetFileName(first) : Path.GetFileNameWithoutExtension(first);
        }
        else
        {
            name = Path.GetFileName(folder.TrimEnd(Path.DirectorySeparatorChar));
        }
        if (string.IsNullOrWhiteSpace(name)) name = "Archive";
        return (folder, name);
    }
}
