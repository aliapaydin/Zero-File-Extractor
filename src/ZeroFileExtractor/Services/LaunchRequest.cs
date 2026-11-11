using ZeroFileExtractor.Engine;

namespace ZeroFileExtractor.Services;

public enum LaunchAction
{
    /// <summary>Ana pencere (isteğe bağlı bir arşivle).</summary>
    Browse,
    ExtractHere,
    ExtractToFolder,
    ExtractAsk,
    Test,
    CompressZip,
    CompressSevenZip,
    CompressAsk,
}

/// <summary>
/// Uygulamanın nasıl başlatıldığı: çift tıklanan arşiv, sağ tık menüsü komutu ya da komut satırı.
/// Sağ tık menüsü (ZfxShell.dll) seçili yolları bir liste dosyasına yazıp <c>--komut @liste</c> ile çağırır.
/// </summary>
public sealed class LaunchRequest
{
    public LaunchAction Action { get; private set; } = LaunchAction.Browse;
    public List<string> Paths { get; } = new();

    public static LaunchRequest Parse(IEnumerable<string> args)
    {
        var request = new LaunchRequest();
        bool explicitAction = false;
        foreach (var arg in args)
        {
            if (arg.StartsWith("--", StringComparison.Ordinal))
            {
                LaunchAction? action = arg.ToLowerInvariant() switch
                {
                    "--open" => LaunchAction.Browse,
                    "--extract-here" => LaunchAction.ExtractHere,
                    "--extract-to-folder" => LaunchAction.ExtractToFolder,
                    "--extract-ask" => LaunchAction.ExtractAsk,
                    "--test" => LaunchAction.Test,
                    "--zip" => LaunchAction.CompressZip,
                    "--7z" => LaunchAction.CompressSevenZip,
                    "--compress-ask" => LaunchAction.CompressAsk,
                    _ => null,
                };
                if (action is { } value)
                {
                    request.Action = value;
                    explicitAction = true;
                }
            }
            else if (arg.StartsWith('@') && arg.Length > 1 && File.Exists(arg[1..]))
            {
                try
                {
                    request.Paths.AddRange(File.ReadAllLines(arg[1..]).Select(l => l.Trim()).Where(l => l.Length > 0));
                    File.Delete(arg[1..]);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    CrashLog.Write("Liste dosyası", ex);
                }
            }
            else if (arg.Length > 0)
            {
                request.Paths.Add(arg);
            }
        }

        request.Paths.RemoveAll(p => !File.Exists(p) && !Directory.Exists(p));

        // Komutsuz yollar: tek arşivse aç, değilse sıkıştırmayı öner.
        if (!explicitAction && request.Paths.Count > 0)
        {
            bool singleArchive = request.Paths.Count == 1 && File.Exists(request.Paths[0]) && ArchiveFormats.IsArchivePath(request.Paths[0]);
            request.Action = singleArchive ? LaunchAction.Browse : LaunchAction.CompressAsk;
        }
        if (request.Action != LaunchAction.Browse && request.Paths.Count == 0) request.Action = LaunchAction.Browse;
        return request;
    }
}
