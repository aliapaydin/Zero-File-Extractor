using System.Globalization;
using System.Text;
using SharpCompress.Archives;
using SharpCompress.Common;
using SharpCompress.Compressors;
using SharpCompress.Compressors.BZip2;
using SharpCompress.Compressors.Xz;
using SharpCompress.Readers;

namespace ZeroFileExtractor.Engine;

/// <summary>Arşivdeki tek bir kayıt (dosya ya da klasör).</summary>
public sealed class ArchiveEntryInfo
{
    /// <summary>Arşivdeki ham anahtar (eşleştirme için).</summary>
    public required string Key { get; init; }

    /// <summary>"/" ile ayrılmış, baştaki/sondaki ayırıcılar atılmış yol.</summary>
    public required string Path { get; init; }

    public bool IsDirectory { get; init; }
    public long Size { get; init; }
    public long CompressedSize { get; init; }
    public DateTime? Modified { get; init; }
    public long Crc { get; init; }
    public bool IsEncrypted { get; init; }
    public string Method { get; init; } = string.Empty;

    public string Name
    {
        get
        {
            int slash = Path.LastIndexOf('/');
            return slash < 0 ? Path : Path[(slash + 1)..];
        }
    }

    /// <summary>Üst klasörün yolu ("" = kök).</summary>
    public string Parent
    {
        get
        {
            int slash = Path.LastIndexOf('/');
            return slash < 0 ? string.Empty : Path[..slash];
        }
    }
}

public sealed class ExtractOptions
{
    public ConflictMode Conflict { get; set; } = ConflictMode.Ask;

    /// <summary>Çıkarılan yolların başından atılacak klasör ("a/b"); arşiv içinde bir klasörden seçim çıkarırken.</summary>
    public string StripPrefix { get; set; } = string.Empty;

    /// <summary>Klasör yapısını yok say, tüm dosyaları tek klasöre çıkar.</summary>
    public bool Flatten { get; set; }
}

/// <summary>
/// Açılmış bir arşivin içerik listesi ve üzerindeki işlemler. Dosya yalnızca işlem süresince açık tutulur;
/// böylece gezinirken arşiv kilitlenmez.
/// </summary>
public sealed class ArchiveSession
{
    private enum Mode { Archive, Reader, Single }

    private Mode _mode;

    private ArchiveSession(string filePath) => FilePath = filePath;

    public string FilePath { get; }
    public ArchiveKind Kind { get; private set; }
    public string FormatName { get; private set; } = "?";
    public IReadOnlyList<ArchiveEntryInfo> Entries { get; private set; } = [];
    public bool IsSolid { get; private set; }
    public bool HasEncryptedEntries { get; private set; }
    public bool IsMultiVolume { get; private set; }
    public string? Comment { get; private set; }
    public long TotalSize { get; private set; }
    public long FileLength { get; private set; }
    public int FileCount { get; private set; }
    public int FolderCount { get; private set; }

    /// <summary>Bu oturumda doğrulanmış parola (varsa).</summary>
    public string? Password { get; private set; }

    /// <summary>İçeriği değiştirilebilir mi (dosya ekle/sil)? Şimdilik yalnızca şifresiz, tek parça ZIP.</summary>
    public bool CanModify => Kind == ArchiveKind.Zip && !HasEncryptedEntries && !IsMultiVolume &&
                             FilePath.EndsWith(".zip", StringComparison.OrdinalIgnoreCase);

    // ------------------------------------------------------------------ açma

    /// <summary>Arşivi açıp içeriğini listeler. Başlıklar şifreliyse <see cref="PasswordRequiredException"/> fırlatır.</summary>
    public static ArchiveSession Open(string path, string? password = null, CancellationToken cancellation = default)
    {
        path = FirstVolume(System.IO.Path.GetFullPath(path));
        var session = new ArchiveSession(path)
        {
            Kind = ArchiveFormats.KindOf(path),
            FileLength = new FileInfo(path).Length,
        };
        session._mode = session.Kind switch
        {
            ArchiveKind.Zip or ArchiveKind.SevenZip or ArchiveKind.Rar or ArchiveKind.Tar or ArchiveKind.GZip or ArchiveKind.Unknown => Mode.Archive,
            ArchiveKind.BZip2 or ArchiveKind.Xz or ArchiveKind.Zstd => Mode.Single,
            _ => Mode.Reader,
        };

        try
        {
            try
            {
                session.Load(password, cancellation);
            }
            catch (Exception ex) when (session._mode == Mode.Archive && session.Kind == ArchiveKind.Unknown &&
                                       ex is not OperationCanceledException && !IsPasswordError(ex, false))
            {
                // Uzantısı tanınmayan dosya: ileri-yönlü okuyucuyla bir daha dene.
                session._mode = Mode.Reader;
                session.Load(password, cancellation);
            }
        }
        catch (Exception ex) when (IsPasswordError(ex, password is not null))
        {
            throw new PasswordRequiredException(password is not null);
        }

        session.Password = password;
        return session;
    }

    private void Load(string? password, CancellationToken cancellation)
    {
        var list = new List<ArchiveEntryInfo>();
        switch (_mode)
        {
            case Mode.Archive:
            {
                using var archive = ArchiveFactory.OpenArchive(FilePath, Options(password));
                IsSolid = archive.IsSolid;
                FormatName = TypeName(archive.Type);
                if (Kind == ArchiveKind.Unknown) Kind = KindFromType(archive.Type);
                foreach (var entry in archive.Entries)
                {
                    cancellation.ThrowIfCancellationRequested();
                    Add(list, entry);
                }
                try
                {
                    IsMultiVolume = archive.Volumes.Skip(1).Any();
                }
                catch (Exception)
                {
                    IsMultiVolume = false;
                }
                break;
            }
            case Mode.Reader:
            {
                using var stream = File.OpenRead(FilePath);
                using var reader = ReaderFactory.OpenReader(stream, Options(password));
                FormatName = Kind == ArchiveKind.Unknown ? TypeName(reader.Type) : ArchiveFormats.DisplayName(Kind);
                while (reader.MoveToNextEntry())
                {
                    cancellation.ThrowIfCancellationRequested();
                    Add(list, reader.Entry);
                }
                IsSolid = true; // yalnızca baştan sona okunabilir
                break;
            }
            default:
            {
                FormatName = ArchiveFormats.DisplayName(Kind);
                var name = ArchiveFormats.StripExtension(System.IO.Path.GetFileName(FilePath));
                list.Add(new ArchiveEntryInfo
                {
                    Key = name,
                    Path = name,
                    Size = 0,
                    CompressedSize = FileLength,
                    Modified = File.GetLastWriteTime(FilePath),
                    Method = FormatName,
                });
                IsSolid = true;
                break;
            }
        }

        if (_mode != Mode.Single && Kind != ArchiveKind.Unknown) FormatName = ArchiveFormats.DisplayName(Kind);
        Entries = list;
        FileCount = list.Count(e => !e.IsDirectory);
        FolderCount = list.Count(e => e.IsDirectory);
        TotalSize = list.Sum(e => e.Size);
        HasEncryptedEntries = list.Any(e => e.IsEncrypted);

        if (Kind == ArchiveKind.Zip)
        {
            try
            {
                using var zip = System.IO.Compression.ZipFile.OpenRead(FilePath);
                Comment = string.IsNullOrWhiteSpace(zip.Comment) ? null : zip.Comment;
            }
            catch (Exception)
            {
                Comment = null;
            }
        }
    }

    private static void Add(List<ArchiveEntryInfo> list, IEntry entry)
    {
        var key = entry.Key;
        if (string.IsNullOrEmpty(key)) return;
        var path = Normalize(key);
        if (path.Length == 0) return;
        list.Add(new ArchiveEntryInfo
        {
            Key = key,
            Path = path,
            IsDirectory = entry.IsDirectory,
            Size = entry.IsDirectory ? 0 : Math.Max(0, entry.Size),
            CompressedSize = entry.IsDirectory ? 0 : Math.Max(0, entry.CompressedSize),
            Modified = entry.LastModifiedTime,
            Crc = entry.Crc,
            IsEncrypted = entry.IsEncrypted,
            Method = entry.IsDirectory ? string.Empty : entry.CompressionType.ToString(),
        });
    }

    public static string Normalize(string key)
    {
        var parts = key.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries)
            .Where(p => p != "." && p != "..");
        return string.Join('/', parts);
    }

    private static ReaderOptions Options(string? password) => new()
    {
        Password = password,
        LeaveStreamOpen = false,
        ArchiveEncoding = new ArchiveEncoding { Default = OemEncoding() },
    };

    private static Encoding OemEncoding()
    {
        try
        {
            return Encoding.GetEncoding(CultureInfo.CurrentCulture.TextInfo.OEMCodePage);
        }
        catch (Exception)
        {
            return Encoding.UTF8;
        }
    }

    private static string TypeName(ArchiveType type) => type switch
    {
        ArchiveType.Zip => "ZIP",
        ArchiveType.Rar => "RAR",
        ArchiveType.SevenZip => "7Z",
        ArchiveType.Tar => "TAR",
        ArchiveType.GZip => "GZIP",
        _ => type.ToString().ToUpperInvariant(),
    };

    private static ArchiveKind KindFromType(ArchiveType type) => type switch
    {
        ArchiveType.Zip => ArchiveKind.Zip,
        ArchiveType.Rar => ArchiveKind.Rar,
        ArchiveType.SevenZip => ArchiveKind.SevenZip,
        ArchiveType.Tar => ArchiveKind.Tar,
        ArchiveType.GZip => ArchiveKind.GZip,
        _ => ArchiveKind.Unknown,
    };

    /// <summary>"x.part03.rar" verilirse ilk parçayı bulur.</summary>
    private static string FirstVolume(string path)
    {
        var name = System.IO.Path.GetFileName(path);
        var lower = name.ToLowerInvariant();
        if (!lower.EndsWith(".rar", StringComparison.Ordinal)) return path;
        int part = lower.LastIndexOf(".part", StringComparison.Ordinal);
        if (part < 0) return path;
        var digits = lower[(part + 5)..^4];
        if (digits.Length == 0 || !digits.All(char.IsDigit) || int.Parse(digits, CultureInfo.InvariantCulture) == 1) return path;
        var first = name[..(part + 5)] + "1".PadLeft(digits.Length, '0') + name[^4..];
        var candidate = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(path) ?? string.Empty, first);
        return File.Exists(candidate) ? candidate : path;
    }

    public static bool IsPasswordError(Exception ex, bool encryptedAndTried)
    {
        if (ex is OperationCanceledException or PasswordRequiredException) return false;
        if (ex is SharpCompress.Common.CryptographicException or System.Security.Cryptography.CryptographicException) return true;
        if (ex is SharpCompressException or InvalidDataException)
        {
            if (ex.Message.Contains("password", StringComparison.OrdinalIgnoreCase)) return true;
            if (ex.Message.Contains("encrypted", StringComparison.OrdinalIgnoreCase)) return true;
            return encryptedAndTried;
        }
        return false;
    }

    // ------------------------------------------------------------------ gezinme yardımcıları

    /// <summary>Kökteki benzersiz adlar (akıllı çıkarma kararı için).</summary>
    public IReadOnlyList<string> RootNames() =>
        Entries.Select(e => e.Path.Split('/')[0]).Distinct(StringComparer.OrdinalIgnoreCase).Take(3).ToList();

    /// <summary>Verilen klasör öneklerinin altındaki tüm kayıtlar ile tek tek seçilen dosyaların birleşimi.</summary>
    public List<ArchiveEntryInfo> Collect(IEnumerable<string> folderPaths, IEnumerable<ArchiveEntryInfo> files)
    {
        var prefixes = folderPaths.Select(p => p.TrimEnd('/') + "/").ToList();
        var set = new HashSet<ArchiveEntryInfo>(files);
        if (prefixes.Count > 0)
        {
            foreach (var entry in Entries)
            {
                foreach (var prefix in prefixes)
                {
                    if (entry.Path.StartsWith(prefix, StringComparison.Ordinal) || entry.Path + "/" == prefix)
                    {
                        set.Add(entry);
                        break;
                    }
                }
            }
        }
        return set.ToList();
    }

    // ------------------------------------------------------------------ çıkarma

    /// <summary>
    /// Seçilen kayıtları (null = tümü) hedef klasöre çıkarır. İlerleme, duraklatma, iptal ve sorular
    /// <paramref name="context"/> üzerinden yürür.
    /// </summary>
    public void Extract(IReadOnlyCollection<ArchiveEntryInfo>? selection, string destination, ExtractOptions options, JobContext context)
    {
        var wanted = selection is null ? null : new HashSet<string>(selection.Select(e => e.Key), StringComparer.Ordinal);
        var scope = selection ?? Entries;
        context.BytesTotal = scope.Sum(e => e.Size);
        context.FilesTotal = scope.Count(e => !e.IsDirectory);

        var root = System.IO.Path.GetFullPath(destination);
        Directory.CreateDirectory(root);
        var rootWithSep = root.EndsWith(System.IO.Path.DirectorySeparatorChar) ? root : root + System.IO.Path.DirectorySeparatorChar;
        var strip = options.StripPrefix.Trim('/');
        var conflict = options.Conflict;
        var done = new HashSet<string>(StringComparer.Ordinal);

        WithPassword(context, password => Walk(password, wanted, done, context, (key, isDirectory, modified, size, open) =>
        {
            var relative = Normalize(key);
            if (strip.Length > 0)
            {
                if (relative.Equals(strip, StringComparison.Ordinal)) return;
                if (relative.StartsWith(strip + "/", StringComparison.Ordinal)) relative = relative[(strip.Length + 1)..];
            }
            if (options.Flatten)
            {
                if (isDirectory) return;
                relative = relative[(relative.LastIndexOf('/') + 1)..];
            }
            relative = SanitizeRelative(relative);
            if (relative.Length == 0) return;

            var target = System.IO.Path.GetFullPath(System.IO.Path.Combine(root, relative));
            // Zip-slip koruması: hedef, seçilen klasörün dışına çıkamaz.
            if (!target.StartsWith(rootWithSep, StringComparison.OrdinalIgnoreCase))
            {
                context.Warnings.Add(relative);
                context.Skipped++;
                return;
            }

            if (isDirectory)
            {
                Directory.CreateDirectory(target);
                return;
            }

            context.SetCurrent(relative);
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(target)!);

            if (File.Exists(target))
            {
                var mode = conflict;
                if (mode == ConflictMode.Ask)
                {
                    var existing = new FileInfo(target);
                    var decision = context.ResolveConflict?.Invoke(new ConflictInfo(target, existing.Length, existing.LastWriteTime, size, modified))
                                   ?? new ConflictDecision(ConflictMode.Rename, true);
                    if (decision.Mode == ConflictMode.Ask) throw new OperationCanceledException(); // kullanıcı işi iptal etti
                    if (decision.ApplyToAll) conflict = decision.Mode;
                    mode = decision.Mode;
                    context.Checkpoint();
                }
                switch (mode)
                {
                    case ConflictMode.Skip:
                        context.Skipped++;
                        context.AddBytes(size);
                        context.FileDone();
                        return;
                    case ConflictMode.Rename:
                        target = UniquePath(target);
                        break;
                    default:
                        File.SetAttributes(target, FileAttributes.Normal);
                        break;
                }
            }

            bool complete = false;
            try
            {
                using (var source = open())
                using (var output = new FileStream(target, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 16))
                {
                    context.Copy(source, output);
                }
                complete = true;
            }
            finally
            {
                if (!complete) TryDelete(target);
            }

            if (modified is { } time && time.Year > 1980)
            {
                try
                {
                    File.SetLastWriteTime(target, time);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentOutOfRangeException)
                {
                    // tarih yazılamadıysa dosya yine de geçerli
                }
            }
            context.FileDone();
        }));
    }

    /// <summary>Tüm dosyaları sonuna kadar okuyup bütünlüğü (CRC) doğrular. Okunan dosya sayısını döner.</summary>
    public int Test(JobContext context)
    {
        context.BytesTotal = TotalSize;
        context.FilesTotal = FileCount;
        int count = 0;
        var done = new HashSet<string>(StringComparer.Ordinal);
        WithPassword(context, password => Walk(password, null, done, context, (key, isDirectory, _, _, open) =>
        {
            if (isDirectory) return;
            context.SetCurrent(Normalize(key));
            using (var source = open())
            {
                context.Copy(source, Stream.Null);
            }
            context.FileDone();
            count++;
        }));
        return count;
    }

    /// <summary>Tek dosyanın içeriğinin ilk <paramref name="maxBytes"/> baytını okur (önizleme için).</summary>
    public byte[] ReadHead(ArchiveEntryInfo entry, int maxBytes, JobContext context)
    {
        byte[] result = [];
        var wanted = new HashSet<string>(StringComparer.Ordinal) { entry.Key };
        var done = new HashSet<string>(StringComparer.Ordinal);
        WithPassword(context, password => Walk(password, wanted, done, context, (_, isDirectory, _, _, open) =>
        {
            if (isDirectory) return;
            using var source = open();
            using var memory = new MemoryStream();
            var buffer = new byte[81920];
            int read;
            while (memory.Length < maxBytes && (read = source.Read(buffer, 0, (int)Math.Min(buffer.Length, maxBytes - memory.Length))) > 0)
            {
                context.Checkpoint();
                memory.Write(buffer, 0, read);
            }
            result = memory.ToArray();
        }));
        return result;
    }

    private delegate void EntryHandler(string key, bool isDirectory, DateTime? modified, long size, Func<Stream> open);

    private void WithPassword(JobContext context, Action<string?> action)
    {
        var password = Password;
        while (true)
        {
            try
            {
                action(password);
                Password = password;
                return;
            }
            catch (Exception ex) when (IsPasswordError(ex, HasEncryptedEntries && password is not null))
            {
                var next = context.RequestPassword?.Invoke(password is not null);
                if (next is null)
                {
                    if (context.RequestPassword is null) throw new PasswordRequiredException(password is not null);
                    throw new OperationCanceledException();
                }
                password = next;
            }
        }
    }

    /// <summary>İstenen kayıtları sırayla dolaşır. <paramref name="done"/> parola yeniden denemelerinde tekrar işlemeyi önler.</summary>
    private void Walk(string? password, HashSet<string>? wanted, HashSet<string> done, JobContext context, EntryHandler handler)
    {
        bool Want(IEntry entry) =>
            !string.IsNullOrEmpty(entry.Key) && (wanted is null || wanted.Contains(entry.Key)) && !done.Contains(entry.Key);

        int remaining = wanted?.Count ?? int.MaxValue;

        switch (_mode)
        {
            case Mode.Archive:
            {
                using var archive = ArchiveFactory.OpenArchive(FilePath, Options(password));
                if (archive.IsSolid || archive.Type == ArchiveType.SevenZip)
                {
                    using var reader = archive.ExtractAllEntries();
                    WalkReader(reader, Want, done, context, handler, remaining);
                }
                else
                {
                    foreach (var entry in archive.Entries)
                    {
                        context.Checkpoint();
                        if (!Want(entry)) continue;
                        var current = entry;
                        handler(current.Key!, current.IsDirectory, current.LastModifiedTime, Math.Max(0, current.Size), current.OpenEntryStream);
                        done.Add(current.Key!);
                    }
                }
                break;
            }
            case Mode.Reader:
            {
                using var stream = File.OpenRead(FilePath);
                using var reader = ReaderFactory.OpenReader(stream, Options(password));
                WalkReader(reader, Want, done, context, handler, remaining);
                break;
            }
            default:
            {
                var entry = Entries[0];
                if (wanted is not null && !wanted.Contains(entry.Key)) return;
                if (done.Contains(entry.Key)) return;
                using var file = File.OpenRead(FilePath);
                handler(entry.Key, false, entry.Modified, 0, () => OpenSingle(file));
                done.Add(entry.Key);
                break;
            }
        }
    }

    private static void WalkReader(IReader reader, Func<IEntry, bool> want, HashSet<string> done, JobContext context, EntryHandler handler, int remaining)
    {
        while (remaining > 0 && reader.MoveToNextEntry())
        {
            context.Checkpoint();
            var entry = reader.Entry;
            if (!want(entry)) continue;
            handler(entry.Key!, entry.IsDirectory, entry.LastModifiedTime, Math.Max(0, entry.Size), () => reader.OpenEntryStream());
            done.Add(entry.Key!);
            remaining--;
        }
    }

    private Stream OpenSingle(Stream file) => Kind switch
    {
        ArchiveKind.BZip2 => BZip2Stream.Create(file, CompressionMode.Decompress, true),
        ArchiveKind.Xz => new XZStream(file),
        ArchiveKind.Zstd => new SharpCompress.Compressors.ZStandard.DecompressionStream(file),
        _ => throw new NotSupportedException(Kind.ToString()),
    };

    // ------------------------------------------------------------------ yol yardımcıları

    private static readonly char[] InvalidNameChars = ['<', '>', ':', '"', '|', '?', '*'];

    /// <summary>Windows'ta geçersiz karakterleri "_" yapar, sondaki nokta/boşlukları atar.</summary>
    public static string SanitizeRelative(string relative)
    {
        var parts = relative.Split('/', StringSplitOptions.RemoveEmptyEntries);
        for (int i = 0; i < parts.Length; i++)
        {
            var part = parts[i];
            if (part.IndexOfAny(InvalidNameChars) >= 0 || part.Any(c => c < 32))
            {
                var chars = part.ToCharArray();
                for (int c = 0; c < chars.Length; c++)
                    if (chars[c] < 32 || Array.IndexOf(InvalidNameChars, chars[c]) >= 0) chars[c] = '_';
                part = new string(chars);
            }
            part = part.TrimEnd(' ', '.');
            if (part.Length == 0) part = "_";
            parts[i] = part;
        }
        return string.Join(System.IO.Path.DirectorySeparatorChar, parts);
    }

    /// <summary>"ad.txt" doluysa "ad (2).txt", "ad (3).txt"…</summary>
    public static string UniquePath(string path)
    {
        if (!File.Exists(path) && !Directory.Exists(path)) return path;
        var folder = System.IO.Path.GetDirectoryName(path) ?? string.Empty;
        var fileName = System.IO.Path.GetFileName(path);
        var kind = ArchiveFormats.KindOf(fileName);
        string stem, extension;
        if (kind != ArchiveKind.Unknown && !Directory.Exists(path))
        {
            stem = ArchiveFormats.StripExtension(fileName);
            extension = fileName[stem.Length..];
        }
        else if (Directory.Exists(path))
        {
            stem = fileName;
            extension = string.Empty;
        }
        else
        {
            stem = System.IO.Path.GetFileNameWithoutExtension(fileName);
            extension = System.IO.Path.GetExtension(fileName);
        }
        for (int i = 2; i < 10000; i++)
        {
            var candidate = System.IO.Path.Combine(folder, $"{stem} ({i}){extension}");
            if (!File.Exists(candidate) && !Directory.Exists(candidate)) return candidate;
        }
        return System.IO.Path.Combine(folder, $"{stem} ({Guid.NewGuid():N}){extension}");
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // yarım dosya silinemedi
        }
    }
}
