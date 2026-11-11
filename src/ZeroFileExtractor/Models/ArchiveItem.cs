using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using ZeroFileExtractor.Engine;
using ZeroFileExtractor.Services;

namespace ZeroFileExtractor.Models;

/// <summary>Dosya listesindeki bir satır: klasör ya da dosya.</summary>
public sealed class ArchiveItem
{
    public ArchiveItem(FolderNode folder)
    {
        Folder = folder;
        Name = folder.Name;
        PathInArchive = folder.Path;
        IsDirectory = true;
        Size = folder.TotalSize;
        Packed = folder.TotalPacked;
        Modified = folder.Modified;
        TypeText = Loc.T("Klasör");
        SizeText = folder.TotalFiles == 0 ? string.Empty : Format.Size(folder.TotalSize);
        PackedText = string.Empty;
        RatioText = string.Empty;
        Detail = Loc.F("{0} dosya", folder.TotalFiles);
    }

    public ArchiveItem(ArchiveEntryInfo entry, bool showFullPath = false)
    {
        Entry = entry;
        Name = showFullPath ? entry.Path : entry.Name;
        PathInArchive = entry.Path;
        Size = entry.Size;
        Packed = entry.CompressedSize;
        Modified = entry.Modified;
        IsEncrypted = entry.IsEncrypted;
        var ext = System.IO.Path.GetExtension(entry.Name);
        TypeText = ext.Length > 1 ? Loc.F("{0} dosyası", ext[1..].ToUpperInvariant()) : Loc.T("Dosya");
        SizeText = Format.Size(entry.Size);
        PackedText = entry.CompressedSize > 0 ? Format.Size(entry.CompressedSize) : string.Empty;
        RatioText = Format.Ratio(entry.CompressedSize, entry.Size);
        Detail = entry.Method;
    }

    public FolderNode? Folder { get; }
    public ArchiveEntryInfo? Entry { get; }
    public string Name { get; }
    public string PathInArchive { get; }
    public bool IsDirectory { get; }
    public bool IsEncrypted { get; }
    public long Size { get; }
    public long Packed { get; }
    public DateTime? Modified { get; }

    public string SizeText { get; }
    public string PackedText { get; }
    public string RatioText { get; }
    public string TypeText { get; }
    public string Detail { get; }
    public string ModifiedText => Format.Date(Modified);
    public Visibility LockVisibility => IsEncrypted ? Visibility.Visible : Visibility.Collapsed;

    private ImageSource? _icon;
    private bool _iconLoaded;

    /// <summary>Kabuk simgesi (UI iş parçacığında, ilk istendiğinde yüklenir).</summary>
    public ImageSource? Icon
    {
        get
        {
            if (!_iconLoaded)
            {
                _iconLoaded = true;
                _icon = ShellIcons.Get(Name, IsDirectory);
            }
            return _icon;
        }
    }
}
