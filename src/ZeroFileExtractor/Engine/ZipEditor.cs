using System.IO.Compression;

namespace ZeroFileExtractor.Engine;

/// <summary>
/// Var olan bir ZIP'e dosya ekler ya da içinden siler. Değişiklik arşivin bir kopyasında yapılır,
/// başarıyla bitince asıl dosyanın yerine konur; yarıda kesilirse asıl arşiv bozulmaz.
/// </summary>
public static class ZipEditor
{
    public static void Add(string zipPath, IReadOnlyList<SourceItem> items, string targetFolder, int level, JobContext context)
    {
        context.BytesTotal = items.Sum(i => i.Size);
        context.FilesTotal = items.Count(i => !i.IsDirectory);
        var prefix = targetFolder.Trim('/');
        if (prefix.Length > 0) prefix += "/";
        var compression = level switch
        {
            <= 0 => CompressionLevel.NoCompression,
            <= 2 => CompressionLevel.Fastest,
            <= 4 => CompressionLevel.Optimal,
            _ => CompressionLevel.SmallestSize,
        };

        Edit(zipPath, context, archive =>
        {
            foreach (var item in items)
            {
                context.Checkpoint();
                var name = prefix + item.EntryName;
                if (item.IsDirectory)
                {
                    if (archive.GetEntry(name + "/") is null) archive.CreateEntry(name + "/");
                    continue;
                }
                context.SetCurrent(name);
                archive.GetEntry(name)?.Delete();
                FileStream source;
                try
                {
                    source = new FileStream(item.FullPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    context.Warnings.Add(item.FullPath);
                    context.Skipped++;
                    context.AddBytes(item.Size);
                    continue;
                }
                using (source)
                {
                    var entry = archive.CreateEntry(name, compression);
                    entry.LastWriteTime = item.Modified.Year < 1980 ? new DateTime(1980, 1, 1) : item.Modified;
                    using var target = entry.Open();
                    context.Copy(source, target);
                }
                context.FileDone();
            }
        });
    }

    /// <summary>Verilen yolları ("a/b.txt") ve klasör öneklerini ("a/c/") siler.</summary>
    public static void Delete(string zipPath, IReadOnlyCollection<string> files, IReadOnlyCollection<string> folders, JobContext context)
    {
        var fileSet = new HashSet<string>(files, StringComparer.Ordinal);
        var prefixes = folders.Select(f => f.Trim('/') + "/").ToList();
        Edit(zipPath, context, archive =>
        {
            var doomed = archive.Entries.Where(e =>
            {
                var path = ArchiveSession.Normalize(e.FullName);
                if (fileSet.Contains(path)) return true;
                var withSlash = path + "/";
                return prefixes.Any(p => withSlash.StartsWith(p, StringComparison.Ordinal));
            }).ToList();
            context.FilesTotal = doomed.Count;
            foreach (var entry in doomed)
            {
                context.Checkpoint();
                context.SetCurrent(entry.FullName);
                entry.Delete();
                context.FileDone();
            }
        });
    }

    private static void Edit(string zipPath, JobContext context, Action<ZipArchive> action)
    {
        var temp = zipPath + ".zfxtmp";
        bool complete = false;
        try
        {
            File.Copy(zipPath, temp, overwrite: true);
            using (var archive = ZipFile.Open(temp, ZipArchiveMode.Update))
            {
                action(archive);
            }
            context.Checkpoint();
            File.Move(temp, zipPath, overwrite: true);
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
                    // geçici kopya silinemedi
                }
            }
        }
    }
}
