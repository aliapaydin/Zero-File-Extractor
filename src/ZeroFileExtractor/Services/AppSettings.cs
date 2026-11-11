using System.Runtime.CompilerServices;
using Windows.Storage;
using ZeroFileExtractor.Engine;

namespace ZeroFileExtractor.Services;

public enum AppTheme
{
    System = 0,
    Light = 1,
    Dark = 2,
}

/// <summary>Arşivi çıkarırken hedef klasörün nasıl seçileceği.</summary>
public enum ExtractFolderMode
{
    /// <summary>Arşivde tek kök öğe varsa doğrudan, yoksa arşiv adında klasöre.</summary>
    Smart = 0,
    /// <summary>Her zaman arşiv adında yeni klasöre.</summary>
    NamedFolder = 1,
    /// <summary>Doğrudan seçilen klasöre.</summary>
    Direct = 2,
}

/// <summary>Kalıcı kullanıcı ayarları (paketli uygulamanın LocalSettings deposu).</summary>
public sealed class AppSettings
{
    public static AppSettings Current { get; } = new();

    private readonly ApplicationDataContainer? _store;
    private readonly Dictionary<string, object> _memory = new();

    /// <summary>Değişen ayarın adını taşır.</summary>
    public event EventHandler<string>? Changed;

    private AppSettings()
    {
        try
        {
            _store = ApplicationData.Current.LocalSettings;
        }
        catch (Exception)
        {
            _store = null; // paketsiz çalıştırma: ayarlar yalnızca bellekte
        }
    }

    // ---- Görünüm ----
    public AppTheme Theme { get => (AppTheme)Get(0); set => Set((int)value); }
    public bool ShowRibbon { get => Get(true); set => Set(value); }
    public bool ShowFolderTree { get => Get(true); set => Set(value); }
    public bool ShowPreviewPane { get => Get(true); set => Set(value); }
    public bool ShowStatusBar { get => Get(true); set => Set(value); }
    /// <summary>Klasör yapısını yok sayıp tüm dosyaları tek listede göster.</summary>
    public bool FlatView { get => Get(false); set => Set(value); }
    /// <summary>Arayüz dili: "" (Windows'a göre), "tr" ya da "en". Yeniden başlatınca uygulanır.</summary>
    public string UiLanguage { get => Get(string.Empty); set => Set(value); }

    // ---- Çıkarma ----
    public ExtractFolderMode ExtractFolder { get => (ExtractFolderMode)Get(0); set => Set((int)value); }
    public ConflictMode ExtractConflict { get => (ConflictMode)Get(0); set => Set((int)value); }
    public bool OpenFolderAfterExtract { get => Get(true); set => Set(value); }
    public bool DeleteArchiveAfterExtract { get => Get(false); set => Set(value); }
    public string LastExtractFolder { get => Get(string.Empty); set => Set(value); }

    // ---- Sıkıştırma ----
    public CreateFormat DefaultFormat { get => (CreateFormat)Get(0); set => Set((int)value); }
    public int DefaultLevel { get => Get(3); set => Set(value); }
    public bool DeleteSourcesAfterCompress { get => Get(false); set => Set(value); }

    // ---- İş penceresi ----
    /// <summary>İş başarıyla bitince ilerleme penceresi kendiliğinden kapansın.</summary>
    public bool CloseWhenDone { get => Get(false); set => Set(value); }

    // ---- Yerleşim (hatırlanır) ----
    public string MainWindowBounds { get => Get(string.Empty); set => Set(value); }
    public double TreeWidth { get => Get(240.0); set => Set(value); }
    public double PreviewWidth { get => Get(340.0); set => Set(value); }
    public string SortColumn { get => Get("Name"); set => Set(value); }
    public bool SortDescending { get => Get(false); set => Set(value); }

    // ---- Son açılanlar ----
    public IReadOnlyList<string> RecentArchives
    {
        get => Get(string.Empty).Split('|', StringSplitOptions.RemoveEmptyEntries);
        set => Set(string.Join('|', value.Take(12)));
    }

    public void AddRecent(string path)
    {
        var list = RecentArchives.Where(p => !string.Equals(p, path, StringComparison.OrdinalIgnoreCase)).ToList();
        list.Insert(0, path);
        RecentArchives = list;
    }

    // ---- Serbest anahtarlar (öğrenilen hızlar vb.) ----
    public double GetDouble(string key, double fallback = 0) => Get(fallback, key);
    public void SetDouble(string key, double value) => Set(value, key);

    /// <summary>Tüm ayarları siler.</summary>
    public void ResetAll()
    {
        _store?.Values.Clear();
        _memory.Clear();
        Changed?.Invoke(this, "All");
    }

    private T Get<T>(T fallback, [CallerMemberName] string key = "")
    {
        try
        {
            if (_store is not null)
                return _store.Values.TryGetValue(key, out var value) && value is T typed ? typed : fallback;
            return _memory.TryGetValue(key, out var stored) && stored is T mem ? mem : fallback;
        }
        catch (Exception)
        {
            return fallback;
        }
    }

    private void Set<T>(T value, [CallerMemberName] string key = "") where T : notnull
    {
        try
        {
            if (_store is not null) _store.Values[key] = value;
            else _memory[key] = value;
        }
        catch (Exception ex)
        {
            CrashLog.Write("Ayar", ex);
        }
        Changed?.Invoke(this, key);
    }
}

public static class ThemeHelper
{
    public static Microsoft.UI.Xaml.ElementTheme ToElementTheme(AppTheme theme) => theme switch
    {
        AppTheme.Light => Microsoft.UI.Xaml.ElementTheme.Light,
        AppTheme.Dark => Microsoft.UI.Xaml.ElementTheme.Dark,
        _ => Microsoft.UI.Xaml.ElementTheme.Default,
    };
}
