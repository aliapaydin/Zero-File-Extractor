namespace ZeroFileExtractor.Services;

/// <summary>
/// Geçici çıkarılan dosyalar (önizleme, çift tıklayıp açma, dışarı sürükleme). Sonraki açılışta
/// bir günden eski olanlar silinir; başka bir uygulamada açık kalabilecekleri için kapanışta silinmez.
/// </summary>
public static class TempFiles
{
    private static readonly string Root = Path.Combine(Path.GetTempPath(), "ZeroFileExtractor");

    /// <summary>Her çağrıda yeni, boş bir klasör.</summary>
    public static string NewFolder()
    {
        var folder = Path.Combine(Root, Guid.NewGuid().ToString("N")[..12]);
        Directory.CreateDirectory(folder);
        return folder;
    }

    public static void CleanStale() => Task.Run(() =>
    {
        try
        {
            if (!Directory.Exists(Root)) return;
            var cutoff = DateTime.Now - TimeSpan.FromDays(1);
            foreach (var folder in Directory.EnumerateDirectories(Root))
            {
                try
                {
                    if (Directory.GetLastWriteTime(folder) > cutoff) continue;
                    foreach (var file in Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories))
                        File.SetAttributes(file, FileAttributes.Normal);
                    Directory.Delete(folder, true);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    // dışarıda açık: sonraki açılışta
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    });
}
