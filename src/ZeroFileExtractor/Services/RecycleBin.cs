using System.Runtime.InteropServices;

namespace ZeroFileExtractor.Services;

/// <summary>Dosya ve klasörleri Geri Dönüşüm Kutusu'na gönderir (kalıcı silmez).</summary>
public static class RecycleBin
{
    private const uint FO_DELETE = 0x0003;
    private const ushort FOF_SILENT = 0x0004, FOF_NOCONFIRMATION = 0x0010, FOF_ALLOWUNDO = 0x0040, FOF_NOERRORUI = 0x0400;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct SHFILEOPSTRUCT
    {
        public IntPtr hwnd;
        public uint wFunc;
        public string pFrom;
        public string? pTo;
        public ushort fFlags;
        public int fAnyOperationsAborted;
        public IntPtr hNameMappings;
        public string? lpszProgressTitle;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHFileOperation(ref SHFILEOPSTRUCT lpFileOp);

    public static bool Send(IEnumerable<string> paths)
    {
        var list = paths.Where(p => File.Exists(p) || Directory.Exists(p)).ToList();
        if (list.Count == 0) return true;
        var operation = new SHFILEOPSTRUCT
        {
            wFunc = FO_DELETE,
            pFrom = string.Join('\0', list) + "\0", // çift null ile biter (ikincisini sıralama ekler)
            fFlags = (ushort)(FOF_ALLOWUNDO | FOF_NOCONFIRMATION | FOF_SILENT | FOF_NOERRORUI),
        };
        return SHFileOperation(ref operation) == 0 && operation.fAnyOperationsAborted == 0;
    }
}
