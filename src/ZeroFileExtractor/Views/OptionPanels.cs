using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.Storage.Pickers;
using ZeroFileExtractor.Engine;
using ZeroFileExtractor.Services;

namespace ZeroFileExtractor.Views;

internal static class PanelHelpers
{
    public static TextBlock Label(string text) => new()
    {
        Text = text,
        Style = (Style)Application.Current.Resources["BodyStrongTextBlockStyle"],
        Margin = new Thickness(0, 6, 0, 0),
    };

    public static TextBlock Hint(string text = "") => new()
    {
        Text = text,
        TextWrapping = TextWrapping.Wrap,
        Style = (Style)Application.Current.Resources["CaptionTextBlockStyle"],
        Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"],
    };

    public static Border Card(UIElement child) => new()
    {
        Child = child,
        Padding = new Thickness(14, 10, 14, 12),
        CornerRadius = new CornerRadius(8),
        Background = (Brush)Application.Current.Resources["CardBackgroundFillColorDefaultBrush"],
        BorderBrush = (Brush)Application.Current.Resources["CardStrokeColorDefaultBrush"],
        BorderThickness = new Thickness(1),
    };

    public static async Task<string?> PickFolderAsync(IntPtr hwnd)
    {
        var picker = new FolderPicker { SuggestedStartLocation = PickerLocationId.ComputerFolder };
        picker.FileTypeFilter.Add("*");
        WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);
        var folder = await picker.PickSingleFolderAsync();
        return folder?.Path;
    }

    public static string LevelName(int level) => level switch
    {
        <= 0 => Loc.T("Depola (sıkıştırma yok)"),
        1 => Loc.T("En hızlı"),
        2 => Loc.T("Hızlı"),
        3 => Loc.T("Normal"),
        4 => Loc.T("İyi"),
        _ => Loc.T("En iyi (en küçük)"),
    };
}

/// <summary>Yeni arşiv seçenekleri: ad, konum, biçim, düzey, parola ve süre/boyut tahmini.</summary>
public sealed partial class CompressOptionsPanel : StackPanel
{
    private readonly IReadOnlyList<string> _sources;
    private readonly IntPtr _hwnd;
    private readonly TextBox _name = new() { HorizontalAlignment = HorizontalAlignment.Stretch };
    private readonly TextBlock _extension = new() { VerticalAlignment = VerticalAlignment.Center, MinWidth = 56 };
    private readonly TextBox _folder = new() { IsReadOnly = true, HorizontalAlignment = HorizontalAlignment.Stretch };
    private readonly ComboBox _format = new() { HorizontalAlignment = HorizontalAlignment.Stretch };
    private readonly Slider _level = new() { Minimum = 0, Maximum = 5, StepFrequency = 1, TickFrequency = 1, TickPlacement = Microsoft.UI.Xaml.Controls.Primitives.TickPlacement.BottomRight, IsThumbToolTipEnabled = false };
    private readonly TextBlock _levelName = new() { VerticalAlignment = VerticalAlignment.Center, MinWidth = 150 };
    private readonly StackPanel _levelRow = new() { Spacing = 4 };
    private readonly StackPanel _passwordRow = new() { Spacing = 6 };
    private readonly PasswordBox _password = new() { PlaceholderText = Loc.T("Parola (isteğe bağlı)") };
    private readonly PasswordBox _passwordAgain = new() { PlaceholderText = Loc.T("Parolayı yinele") };
    private readonly CheckBox _deleteSources = new() { Content = Loc.T("Bitince kaynak dosyaları Geri Dönüşüm Kutusu'na gönder") };
    private readonly TextBlock _estimateTitle = new() { Style = (Style)Application.Current.Resources["BodyStrongTextBlockStyle"] };
    private readonly TextBlock _estimateTime = new();
    private readonly TextBlock _estimateSize = new();
    private readonly TextBlock _estimateNote = PanelHelpers.Hint();
    private readonly ProgressRing _scanning = new() { Width = 16, Height = 16, IsActive = true };
    private readonly TextBlock _error = new()
    {
        TextWrapping = TextWrapping.Wrap,
        Visibility = Visibility.Collapsed,
        Foreground = (Brush)Application.Current.Resources["SystemFillColorCriticalBrush"],
    };
    private readonly TextBlock _formatNote = PanelHelpers.Hint();
    private readonly List<CreateFormat> _formats = new();
    private List<SourceItem>? _items;
    private long _bytes;
    private int _files;

    public CompressOptionsPanel(IReadOnlyList<string> sources, IntPtr hwnd, CreateFormat? presetFormat = null)
    {
        _sources = sources;
        _hwnd = hwnd;
        Spacing = 6;
        MinWidth = 440;
        var settings = AppSettings.Current;

        var (folder, name) = ArchiveWriter.SuggestName(sources);
        _name.Text = name;
        _folder.Text = folder;

        // Ad + uzantı
        Children.Add(PanelHelpers.Label(Loc.T("Arşiv adı")));
        var nameRow = new Grid { ColumnSpacing = 8 };
        nameRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        nameRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        nameRow.Children.Add(_name);
        Grid.SetColumn(_extension, 1);
        nameRow.Children.Add(_extension);
        Children.Add(nameRow);

        // Konum
        Children.Add(PanelHelpers.Label(Loc.T("Kaydedilecek klasör")));
        var folderRow = new Grid { ColumnSpacing = 8 };
        folderRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        folderRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        folderRow.Children.Add(_folder);
        var browse = new Button { Content = Loc.T("Gözat…") };
        browse.Click += async (_, _) =>
        {
            if (await PanelHelpers.PickFolderAsync(_hwnd) is { } picked) _folder.Text = picked;
        };
        Grid.SetColumn(browse, 1);
        folderRow.Children.Add(browse);
        Children.Add(folderRow);

        // Biçim
        Children.Add(PanelHelpers.Label(Loc.T("Biçim")));
        foreach (var format in new[] { CreateFormat.Zip, CreateFormat.SevenZip, CreateFormat.Tar, CreateFormat.TarGz, CreateFormat.TarBz2, CreateFormat.TarZst })
            AddFormat(format, ArchiveFormats.DisplayName(format) + FormatSuffix(format));
        if (RarExe.IsAvailable) AddFormat(CreateFormat.Rar, "RAR" + Loc.T(" · WinRAR ile"));
        Children.Add(_format);
        Children.Add(_formatNote);

        // Düzey
        _levelRow.Children.Add(PanelHelpers.Label(Loc.T("Sıkıştırma düzeyi")));
        var levelGrid = new Grid { ColumnSpacing = 12 };
        levelGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        levelGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        levelGrid.Children.Add(_level);
        Grid.SetColumn(_levelName, 1);
        levelGrid.Children.Add(_levelName);
        _levelRow.Children.Add(levelGrid);
        Children.Add(_levelRow);

        // Parola
        _passwordRow.Children.Add(PanelHelpers.Label(Loc.T("Parola ile şifrele")));
        _passwordRow.Children.Add(_password);
        _passwordRow.Children.Add(_passwordAgain);
        Children.Add(_passwordRow);

        _deleteSources.IsChecked = settings.DeleteSourcesAfterCompress;
        Children.Add(_deleteSources);

        // Tahmin kartı
        var estimate = new StackPanel { Spacing = 2 };
        var estimateHeader = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        estimateHeader.Children.Add(_estimateTitle);
        estimateHeader.Children.Add(_scanning);
        estimate.Children.Add(estimateHeader);
        estimate.Children.Add(_estimateTime);
        estimate.Children.Add(_estimateSize);
        estimate.Children.Add(_estimateNote);
        var card = PanelHelpers.Card(estimate);
        card.Margin = new Thickness(0, 8, 0, 0);
        Children.Add(card);
        Children.Add(_error);

        _estimateTitle.Text = Loc.T("Dosyalar sayılıyor…");

        var initial = presetFormat ?? settings.DefaultFormat;
        int index = _formats.IndexOf(initial);
        _format.SelectedIndex = index < 0 ? 0 : index;
        _level.Value = Math.Clamp(settings.DefaultLevel, 0, 5);

        _format.SelectionChanged += (_, _) => FormatChanged();
        _level.ValueChanged += (_, _) => UpdateEstimate();
        FormatChanged();
    }

    private void AddFormat(CreateFormat format, string text)
    {
        _formats.Add(format);
        _format.Items.Add(text);
    }

    private static string FormatSuffix(CreateFormat format) => format switch
    {
        CreateFormat.Zip => Loc.T(" · her yerde açılır"),
        CreateFormat.SevenZip => Loc.T(" · en iyi sıkıştırma"),
        CreateFormat.TarGz => Loc.T(" · Linux/macOS için"),
        CreateFormat.TarZst => Loc.T(" · çok hızlı"),
        _ => string.Empty,
    };

    public CreateFormat SelectedFormat => _formats[Math.Max(0, _format.SelectedIndex)];

    private int SelectedLevel => (int)Math.Round(_level.Value);

    /// <summary>Kaynakları arka planda sayar; bitince tahmin gösterilir.</summary>
    public async Task ScanAsync(CancellationToken cancellation = default)
    {
        try
        {
            var items = await Task.Run(() => ArchiveWriter.Enumerate(_sources, new JobContext { Cancellation = cancellation }), cancellation);
            _items = items;
            _bytes = items.Sum(i => i.Size);
            _files = items.Count(i => !i.IsDirectory);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _items = new List<SourceItem>();
        }
        _scanning.IsActive = false;
        _scanning.Visibility = Visibility.Collapsed;
        UpdateEstimate();
    }

    private void FormatChanged()
    {
        var format = SelectedFormat;
        _extension.Text = ArchiveFormats.Extension(format);
        _levelRow.Visibility = ArchiveFormats.SupportsLevel(format) ? Visibility.Visible : Visibility.Collapsed;
        _level.Minimum = ArchiveFormats.SupportsStore(format) ? 0 : 1;
        _passwordRow.Visibility = ArchiveFormats.SupportsPassword(format) ? Visibility.Visible : Visibility.Collapsed;
        _formatNote.Text = format switch
        {
            CreateFormat.Zip => Loc.T("Parola verirsen dosyalar AES-256 ile şifrelenir."),
            CreateFormat.SevenZip => Loc.T("LZMA2 ile sıkıştırılır. Bu sürümde 7Z için parola desteklenmiyor; şifreleme gerekiyorsa ZIP seç."),
            CreateFormat.Rar => Loc.T("Bilgisayarındaki WinRAR kullanılır. Parola verirsen dosya adları da şifrelenir."),
            CreateFormat.Tar => Loc.T("Sıkıştırmaz, yalnızca tek dosyada toplar."),
            _ => string.Empty,
        };
        _formatNote.Visibility = _formatNote.Text.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
        UpdateEstimate();
    }

    private void UpdateEstimate()
    {
        _levelName.Text = PanelHelpers.LevelName(SelectedLevel);
        if (_items is null) return;
        var format = SelectedFormat;
        int level = ArchiveFormats.SupportsLevel(format) ? SelectedLevel : 3;
        _estimateTitle.Text = Loc.F("{0} · {1} dosya", Format.Size(_bytes), _files.ToString("N0"));
        if (_files == 0)
        {
            _estimateTime.Text = Loc.T("Sıkıştırılacak dosya bulunamadı.");
            _estimateSize.Text = string.Empty;
            _estimateNote.Text = string.Empty;
            return;
        }
        var time = Estimator.CompressTime(format, level, _bytes, _files);
        long size = Estimator.CompressedSize(_items, format, level);
        _estimateTime.Text = Loc.F("Tahmini süre: yaklaşık {0}", Format.Duration(time));
        _estimateSize.Text = Loc.F("Tahmini arşiv boyutu: yaklaşık {0}", Format.Size(size));
        _estimateNote.Text = Estimator.IsLearned(format, level)
            ? Loc.T("Süre, bu bilgisayarda önceki işlerde ölçülen hıza göre hesaplandı.")
            : Loc.T("Süre genel bir ortalamaya göre; ilk işten sonra bu bilgisayarın hızına göre iyileşir.");
    }

    /// <summary>Girdileri doğrular; geçersizse hata gösterip null döner.</summary>
    public CompressJob? BuildJob()
    {
        _error.Visibility = Visibility.Collapsed;
        var name = _name.Text.Trim();
        if (name.Length == 0 || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            return Fail(Loc.T("Geçerli bir arşiv adı gir. Ad şu karakterleri içeremez: \\ / : * ? \" < > |"));
        if (!Directory.Exists(_folder.Text)) return Fail(Loc.T("Kaydedilecek klasör bulunamadı."));

        var format = SelectedFormat;
        string? password = null;
        if (ArchiveFormats.SupportsPassword(format) && _password.Password.Length > 0)
        {
            if (_password.Password != _passwordAgain.Password) return Fail(Loc.T("Parolalar aynı değil."));
            password = _password.Password;
        }

        int level = ArchiveFormats.SupportsLevel(format) ? SelectedLevel : 3;
        var output = Path.Combine(_folder.Text, name + ArchiveFormats.Extension(format));
        // Kaynağın kendisiyle aynı adda arşiv varsa üzerine yazma: "ad (2).zip".
        output = ArchiveSession.UniquePath(output);

        var settings = AppSettings.Current;
        settings.DefaultFormat = format;
        if (ArchiveFormats.SupportsLevel(format)) settings.DefaultLevel = level;
        settings.DeleteSourcesAfterCompress = _deleteSources.IsChecked == true;

        return new CompressJob
        {
            Sources = _sources,
            Options = new CreateOptions { Format = format, Level = level, Password = password, OutputPath = output },
            DeleteSources = _deleteSources.IsChecked == true,
            KnownBytes = _bytes,
            KnownFiles = _files,
        };
    }

    private CompressJob? Fail(string message)
    {
        _error.Text = message;
        _error.Visibility = Visibility.Visible;
        return null;
    }
}

/// <summary>Çıkarma seçenekleri: hedef, klasör kuralı, çakışma davranışı.</summary>
public sealed partial class ExtractOptionsPanel : StackPanel
{
    private readonly IntPtr _hwnd;
    private readonly bool _wholeArchive;
    private readonly TextBox _folder = new() { HorizontalAlignment = HorizontalAlignment.Stretch };
    private readonly RadioButtons _mode = new();
    private readonly ComboBox _conflict = new() { HorizontalAlignment = HorizontalAlignment.Stretch };
    private readonly CheckBox _open = new() { Content = Loc.T("Bitince klasörü aç") };
    private readonly CheckBox _flatten = new() { Content = Loc.T("Klasör yapısını yok say (tüm dosyalar tek klasöre)") };
    private readonly CheckBox _delete = new() { Content = Loc.T("Bitince arşivi Geri Dönüşüm Kutusu'na gönder") };
    private readonly TextBlock _error = new()
    {
        TextWrapping = TextWrapping.Wrap,
        Visibility = Visibility.Collapsed,
        Foreground = (Brush)Application.Current.Resources["SystemFillColorCriticalBrush"],
    };

    /// <param name="archiveName">Arşivin uzantısız adı ("ad" klasörü seçeneği için); çoklu arşivde null.</param>
    /// <param name="summary">"1,2 GB · 300 dosya · yaklaşık 12 sn" gibi özet; yoksa null.</param>
    public ExtractOptionsPanel(IntPtr hwnd, string initialFolder, bool wholeArchive, string? archiveName, string? summary)
    {
        _hwnd = hwnd;
        _wholeArchive = wholeArchive;
        Spacing = 6;
        MinWidth = 440;
        var settings = AppSettings.Current;

        Children.Add(PanelHelpers.Label(Loc.T("Hedef klasör")));
        var folderRow = new Grid { ColumnSpacing = 8 };
        folderRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        folderRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        _folder.Text = initialFolder;
        folderRow.Children.Add(_folder);
        var browse = new Button { Content = Loc.T("Gözat…") };
        browse.Click += async (_, _) =>
        {
            if (await PanelHelpers.PickFolderAsync(_hwnd) is { } picked) _folder.Text = picked;
        };
        Grid.SetColumn(browse, 1);
        folderRow.Children.Add(browse);
        Children.Add(folderRow);

        if (wholeArchive)
        {
            Children.Add(PanelHelpers.Label(Loc.T("Klasör")));
            _mode.Items.Add(Loc.T("Akıllı: arşivde tek klasör varsa doğrudan, yoksa yeni klasöre"));
            _mode.Items.Add(archiveName is null ? Loc.T("Her arşiv kendi adında yeni klasöre") : Loc.F("\"{0}\" adında yeni klasöre", archiveName));
            _mode.Items.Add(Loc.T("Doğrudan hedef klasöre"));
            _mode.SelectedIndex = (int)settings.ExtractFolder;
            Children.Add(_mode);
        }

        Children.Add(PanelHelpers.Label(Loc.T("Aynı adlı dosya varsa")));
        _conflict.Items.Add(Loc.T("Bana sor"));
        _conflict.Items.Add(Loc.T("Üzerine yaz"));
        _conflict.Items.Add(Loc.T("Atla"));
        _conflict.Items.Add(Loc.T("İkisini de tut (yeniden adlandır)"));
        _conflict.SelectedIndex = (int)settings.ExtractConflict;
        Children.Add(_conflict);

        _open.IsChecked = settings.OpenFolderAfterExtract;
        _open.Margin = new Thickness(0, 6, 0, 0);
        Children.Add(_open);
        Children.Add(_flatten);
        if (wholeArchive)
        {
            _delete.IsChecked = settings.DeleteArchiveAfterExtract;
            Children.Add(_delete);
        }

        if (summary is not null)
        {
            var card = PanelHelpers.Card(new TextBlock { Text = summary, TextWrapping = TextWrapping.Wrap });
            card.Margin = new Thickness(0, 8, 0, 0);
            Children.Add(card);
        }
        Children.Add(_error);
    }

    public string Destination => _folder.Text.Trim();
    public ExtractFolderMode FolderMode => _wholeArchive ? (ExtractFolderMode)Math.Max(0, _mode.SelectedIndex) : ExtractFolderMode.Direct;
    public ConflictMode Conflict => (ConflictMode)Math.Max(0, _conflict.SelectedIndex);
    public bool OpenFolder => _open.IsChecked == true;
    public bool Flatten => _flatten.IsChecked == true;
    public bool DeleteArchive => _wholeArchive && _delete.IsChecked == true;

    /// <summary>Hedefi doğrular (yoksa oluşturur) ve seçimleri varsayılan olarak kaydeder.</summary>
    public bool Validate()
    {
        _error.Visibility = Visibility.Collapsed;
        try
        {
            if (Destination.Length == 0 || !Path.IsPathFullyQualified(Destination)) throw new IOException();
            Directory.CreateDirectory(Destination);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            _error.Text = Loc.T("Hedef klasör oluşturulamadı. Yolu kontrol et.");
            _error.Visibility = Visibility.Visible;
            return false;
        }
        var settings = AppSettings.Current;
        if (_wholeArchive)
        {
            settings.ExtractFolder = FolderMode;
            settings.DeleteArchiveAfterExtract = DeleteArchive;
        }
        settings.ExtractConflict = Conflict;
        settings.OpenFolderAfterExtract = OpenFolder;
        settings.LastExtractFolder = Destination;
        return true;
    }
}
