using System.Diagnostics;
using System.Text;
using Microsoft.Win32;

namespace ZeroFileExtractor.Engine;

/// <summary>
/// RAR oluşturma. RAR sıkıştırma algoritması RARLAB'e ait ve kapalı olduğundan hiçbir açık kaynak
/// uygulama RAR üretemez; bilgisayarda WinRAR kuruluysa onun komut satırı aracı (Rar.exe) kullanılır.
/// RAR açmak için WinRAR gerekmez.
/// </summary>
public static class RarExe
{
    private static string? _cached;
    private static bool _searched;

    /// <summary>Kurulu Rar.exe'nin yolu; yoksa null.</summary>
    public static string? Find()
    {
        if (_searched) return _cached;
        _searched = true;
        var candidates = new List<string>();
        try
        {
            foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
            {
                using var root = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view);
                using var key = root.OpenSubKey(@"SOFTWARE\WinRAR");
                foreach (var valueName in new[] { "exe64", "exe32" })
                {
                    if (key?.GetValue(valueName) is string exe && Path.GetDirectoryName(exe) is { } dir)
                        candidates.Add(Path.Combine(dir, "Rar.exe"));
                }
            }
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
            // kayıt defteri okunamadı: varsayılan konumlara bak
        }
        candidates.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "WinRAR", "Rar.exe"));
        candidates.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "WinRAR", "Rar.exe"));
        _cached = candidates.FirstOrDefault(File.Exists);
        return _cached;
    }

    public static bool IsAvailable => Find() is not null;

    public static void Create(IReadOnlyList<SourceItem> items, CreateOptions options, JobContext context)
    {
        var rar = Find() ?? throw new FileNotFoundException("Rar.exe");
        var output = Path.GetFullPath(options.OutputPath);

        // Yalnızca kök öğeler verilir; alt klasörleri Rar kendisi dolaşır (-r).
        var roots = items.Where(i => !i.EntryName.Contains('/')).Select(i => i.FullPath).ToList();
        var listFile = Path.Combine(Path.GetTempPath(), "zfx-" + Guid.NewGuid().ToString("N") + ".lst");
        File.WriteAllLines(listFile, roots, new UTF8Encoding(false));

        var start = new ProcessStartInfo(rar)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        start.ArgumentList.Add("a");
        start.ArgumentList.Add("-m" + Math.Clamp(options.Level, 0, 5));
        start.ArgumentList.Add("-ep1");   // adlardan üst klasör yolunu at
        start.ArgumentList.Add("-r");     // alt klasörler
        start.ArgumentList.Add("-y");     // sorulara evet
        start.ArgumentList.Add("-o+");    // var olanın üzerine yaz
        start.ArgumentList.Add("-scfl");  // liste dosyası UTF-8
        if (!string.IsNullOrEmpty(options.Password)) start.ArgumentList.Add("-hp" + options.Password);
        start.ArgumentList.Add("--");
        start.ArgumentList.Add(output);
        start.ArgumentList.Add("@" + listFile);

        bool complete = false;
        try
        {
            using var process = Process.Start(start) ?? throw new InvalidOperationException("Rar.exe");
            var errors = new StringBuilder();
            process.ErrorDataReceived += (_, e) =>
            {
                if (e.Data is not null) errors.AppendLine(e.Data);
            };
            process.BeginErrorReadLine();

            // Rar ilerlemeyi "  45%" biçiminde, geri silme karakterleriyle yazar.
            var reader = process.StandardOutput;
            var digits = new StringBuilder();
            var buffer = new char[256];
            int read;
            while ((read = reader.Read(buffer, 0, buffer.Length)) > 0)
            {
                if (context.Cancellation.IsCancellationRequested)
                {
                    try
                    {
                        process.Kill(true);
                    }
                    catch (InvalidOperationException)
                    {
                        // zaten bitmiş
                    }
                    context.Cancellation.ThrowIfCancellationRequested();
                }
                for (int i = 0; i < read; i++)
                {
                    char c = buffer[i];
                    if (char.IsDigit(c))
                    {
                        digits.Append(c);
                    }
                    else
                    {
                        if (c == '%' && digits.Length is > 0 and <= 3 && int.TryParse(digits.ToString(), out int percent))
                            context.SetBytes(context.BytesTotal * Math.Clamp(percent, 0, 100) / 100);
                        digits.Clear();
                    }
                }
            }
            process.WaitForExit();
            context.Cancellation.ThrowIfCancellationRequested();
            // 0: başarılı, 1: uyarı (ör. kilitli dosya atlandı)
            if (process.ExitCode > 1)
                throw new IOException($"Rar.exe ({process.ExitCode}): {errors.ToString().Trim()}");
            if (process.ExitCode == 1) context.Warnings.Add(errors.ToString().Trim());
            context.SetBytes(context.BytesTotal);
            complete = true;
        }
        finally
        {
            try
            {
                File.Delete(listFile);
                if (!complete && File.Exists(output)) File.Delete(output);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // temizlik başarısız
            }
        }
    }
}
