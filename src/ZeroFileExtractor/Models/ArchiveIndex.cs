using ZeroFileExtractor.Engine;

namespace ZeroFileExtractor.Models;

/// <summary>Arşivdeki bir klasör (kayıt olarak yer almasa bile yollardan türetilir).</summary>
public sealed class FolderNode
{
    public required string Name { get; init; }

    /// <summary>"a/b" biçiminde tam yol; kök için "".</summary>
    public required string Path { get; init; }

    public FolderNode? Parent { get; init; }
    public List<FolderNode> Folders { get; } = new();
    public List<ArchiveEntryInfo> Files { get; } = new();

    /// <summary>Klasörün arşivdeki kendi kaydı (varsa).</summary>
    public ArchiveEntryInfo? Entry { get; set; }

    public long TotalSize { get; set; }
    public long TotalPacked { get; set; }
    public int TotalFiles { get; set; }
    public DateTime? Modified { get; set; }

    public override string ToString() => Name;
}

/// <summary>Düz kayıt listesinden klasör ağacı kurar.</summary>
public sealed class ArchiveIndex
{
    private readonly Dictionary<string, FolderNode> _folders = new(StringComparer.Ordinal);

    public FolderNode Root { get; }

    public ArchiveIndex(ArchiveSession session)
    {
        Root = new FolderNode { Name = System.IO.Path.GetFileName(session.FilePath), Path = string.Empty };
        _folders[string.Empty] = Root;

        foreach (var entry in session.Entries)
        {
            if (entry.IsDirectory)
            {
                var node = Ensure(entry.Path);
                node.Entry = entry;
                node.Modified ??= entry.Modified;
            }
            else
            {
                Ensure(entry.Parent).Files.Add(entry);
            }
        }
        Summarize(Root);
    }

    public FolderNode? Find(string path) => _folders.GetValueOrDefault(path.Trim('/'));

    public IEnumerable<FolderNode> AllFolders => _folders.Values;

    private FolderNode Ensure(string path)
    {
        if (_folders.TryGetValue(path, out var existing)) return existing;
        int slash = path.LastIndexOf('/');
        var parent = Ensure(slash < 0 ? string.Empty : path[..slash]);
        var node = new FolderNode { Name = slash < 0 ? path : path[(slash + 1)..], Path = path, Parent = parent };
        parent.Folders.Add(node);
        _folders[path] = node;
        return node;
    }

    private static void Summarize(FolderNode node)
    {
        long size = 0, packed = 0;
        int files = node.Files.Count;
        DateTime? newest = node.Modified;
        foreach (var file in node.Files)
        {
            size += file.Size;
            packed += file.CompressedSize;
            if (file.Modified is { } time && (newest is null || time > newest)) newest = time;
        }
        foreach (var child in node.Folders)
        {
            Summarize(child);
            size += child.TotalSize;
            packed += child.TotalPacked;
            files += child.TotalFiles;
            if (child.Modified is { } time && (newest is null || time > newest)) newest = time;
        }
        node.TotalSize = size;
        node.TotalPacked = packed;
        node.TotalFiles = files;
        node.Modified ??= newest;
    }
}
