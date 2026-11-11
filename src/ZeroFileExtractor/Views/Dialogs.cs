using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using ZeroFileExtractor.Engine;
using ZeroFileExtractor.Services;

namespace ZeroFileExtractor.Views;

/// <summary>Kod ile kurulan küçük diyaloglar.</summary>
public static class Dialogs
{
    public static ContentDialog Create(XamlRoot root, ElementTheme theme, string title, object content,
        string? primary = null, string? close = null, ContentDialogButton defaultButton = ContentDialogButton.Close)
    {
        var dialog = new ContentDialog
        {
            XamlRoot = root,
            RequestedTheme = theme,
            Title = title,
            Content = content,
            CloseButtonText = close ?? Loc.T("Kapat"),
            DefaultButton = defaultButton,
        };
        if (primary is not null) dialog.PrimaryButtonText = primary;
        return dialog;
    }

    public static async Task MessageAsync(XamlRoot root, ElementTheme theme, string title, string message) =>
        await Create(root, theme, title, new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true }, close: Loc.T("Tamam")).ShowAsync();

    public static async Task<bool> ConfirmAsync(XamlRoot root, ElementTheme theme, string title, string message, string confirm)
    {
        var dialog = Create(root, theme, title, new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap }, confirm, Loc.T("Vazgeç"), ContentDialogButton.Primary);
        return await dialog.ShowAsync() == ContentDialogResult.Primary;
    }

    /// <summary>Parola sorar; vazgeçilirse null.</summary>
    public static async Task<string?> PasswordAsync(XamlRoot root, ElementTheme theme, string archiveName, bool wrong)
    {
        var panel = new StackPanel { Spacing = 10, MinWidth = 340 };
        panel.Children.Add(new TextBlock
        {
            Text = Loc.F("\"{0}\" parola ile korunuyor.", archiveName),
            TextWrapping = TextWrapping.Wrap,
        });
        var box = new PasswordBox { PlaceholderText = Loc.T("Parola") };
        panel.Children.Add(box);
        if (wrong)
        {
            panel.Children.Add(new TextBlock
            {
                Text = Loc.T("Parola yanlış ya da arşiv bozuk. Yeniden dene."),
                TextWrapping = TextWrapping.Wrap,
                Foreground = (Brush)Application.Current.Resources["SystemFillColorCriticalBrush"],
            });
        }
        var dialog = Create(root, theme, Loc.T("Parola gerekli"), panel, Loc.T("Tamam"), Loc.T("Vazgeç"), ContentDialogButton.Primary);
        box.Loaded += (_, _) => box.Focus(FocusState.Programmatic);
        var result = await dialog.ShowAsync();
        return result == ContentDialogResult.Primary && box.Password.Length > 0 ? box.Password : null;
    }

    /// <summary>Hedefte aynı adlı dosya var: ne yapılsın?</summary>
    public static async Task<ConflictDecision> ConflictAsync(XamlRoot root, ElementTheme theme, ConflictInfo info)
    {
        var panel = new StackPanel { Spacing = 8, MinWidth = 400 };
        panel.Children.Add(new TextBlock
        {
            Text = Loc.F("\"{0}\" hedef klasörde zaten var.", Path.GetFileName(info.TargetPath)),
            TextWrapping = TextWrapping.Wrap,
        });
        panel.Children.Add(PanelHelpers.Hint(Path.GetDirectoryName(info.TargetPath) ?? string.Empty));
        panel.Children.Add(PanelHelpers.Card(new TextBlock
        {
            Text = Loc.F("Var olan: {0} · {1}\nArşivdeki: {2} · {3}",
                Format.Size(info.ExistingSize), Format.Date(info.ExistingModified),
                Format.Size(info.NewSize), Format.Date(info.NewModified)),
        }));
        var choice = new RadioButtons { SelectedIndex = 0 };
        choice.Items.Add(Loc.T("Üzerine yaz"));
        choice.Items.Add(Loc.T("Atla"));
        choice.Items.Add(Loc.T("İkisini de tut (yeniden adlandır)"));
        panel.Children.Add(choice);
        var all = new CheckBox { Content = Loc.T("Kalan tüm çakışmalar için uygula") };
        panel.Children.Add(all);

        var dialog = Create(root, theme, Loc.T("Dosya zaten var"), panel, Loc.T("Devam"), Loc.T("İşi iptal et"), ContentDialogButton.Primary);
        var result = await dialog.ShowAsync();
        if (result != ContentDialogResult.Primary) return new ConflictDecision(ConflictMode.Ask, false); // Ask = iptal işareti
        var mode = choice.SelectedIndex switch { 1 => ConflictMode.Skip, 2 => ConflictMode.Rename, _ => ConflictMode.Overwrite };
        return new ConflictDecision(mode, all.IsChecked == true);
    }

    /// <summary>Arşiv özellikleri.</summary>
    public static async Task PropertiesAsync(XamlRoot root, ElementTheme theme, ArchiveSession session)
    {
        var grid = new Grid { ColumnSpacing = 18, RowSpacing = 6, MinWidth = 420 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        int row = 0;
        void Add(string label, string value)
        {
            if (string.IsNullOrEmpty(value)) return;
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var left = new TextBlock { Text = label, Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"] };
            var right = new TextBlock { Text = value, TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true };
            Grid.SetRow(left, row);
            Grid.SetRow(right, row);
            Grid.SetColumn(right, 1);
            grid.Children.Add(left);
            grid.Children.Add(right);
            row++;
        }

        var file = new FileInfo(session.FilePath);
        Add(Loc.T("Ad"), file.Name);
        Add(Loc.T("Konum"), file.DirectoryName ?? string.Empty);
        Add(Loc.T("Biçim"), session.FormatName);
        Add(Loc.T("Arşiv boyutu"), Format.Size(session.FileLength));
        Add(Loc.T("Açılmış boyut"), Format.Size(session.TotalSize));
        Add(Loc.T("Sıkıştırma oranı"), Format.Ratio(session.FileLength, session.TotalSize));
        Add(Loc.T("İçerik"), Loc.F("{0} dosya, {1} klasör", session.FileCount.ToString("N0"), session.FolderCount.ToString("N0")));
        Add(Loc.T("Şifreli"), session.HasEncryptedEntries ? Loc.T("Evet") : Loc.T("Hayır"));
        Add(Loc.T("Solid (tek blok)"), session.IsSolid ? Loc.T("Evet") : Loc.T("Hayır"));
        Add(Loc.T("Çok parçalı"), session.IsMultiVolume ? Loc.T("Evet") : Loc.T("Hayır"));
        Add(Loc.T("Değiştirilme"), Format.Date(file.LastWriteTime));
        Add(Loc.T("Açıklama"), session.Comment ?? string.Empty);

        await Create(root, theme, Loc.T("Arşiv özellikleri"), new ScrollViewer { Content = grid, MaxHeight = 460 }).ShowAsync();
    }

    public static readonly (string Keys, string Action)[] Shortcuts =
    [
        ("Ctrl+O", "Arşiv aç"),
        ("Ctrl+N", "Dosyalardan yeni arşiv oluştur"),
        ("Ctrl+Shift+N", "Klasörden yeni arşiv oluştur"),
        ("Ctrl+W", "Arşivi kapat"),
        ("Ctrl+E  /  F5", "Tümünü çıkar…"),
        ("Ctrl+Shift+E  /  F6", "Seçilenleri çıkar…"),
        ("Ctrl+Shift+H", "Arşivin yanına çıkar (akıllı klasör)"),
        ("Ctrl+T", "Arşivi test et"),
        ("Enter", "Klasöre gir / dosyayı aç"),
        ("Backspace  /  Alt+↑", "Üst klasör"),
        ("F3", "Önizle"),
        ("Ctrl+Q", "Önizleme panelini aç/kapat"),
        ("Ctrl+Shift+T", "Klasör ağacını aç/kapat"),
        ("Ctrl+B", "Düz görünüm (tüm dosyalar tek listede)"),
        ("Ctrl+F", "Arşivde ara"),
        ("Esc", "Aramayı temizle"),
        ("Insert  /  Ctrl+Shift+A", "Arşive dosya ekle (ZIP)"),
        ("Delete", "Arşivden sil (ZIP)"),
        ("Ctrl+A", "Tümünü seç"),
        ("Ctrl+I", "Seçimi tersine çevir"),
        ("Ctrl+C", "Seçilenleri panoya kopyala (Gezgin'e yapıştırılabilir)"),
        ("Ctrl+Shift+C", "Arşiv içi yolu kopyala"),
        ("Alt+Enter", "Arşiv özellikleri"),
        ("Ctrl+R", "Yenile"),
        ("Ctrl+,", "Ayarlar"),
        ("F1", "Klavye kısayolları"),
    ];

    public static async Task ShortcutsAsync(XamlRoot root, ElementTheme theme)
    {
        var grid = new Grid { ColumnSpacing = 24, RowSpacing = 6 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        for (int i = 0; i < Shortcuts.Length; i++)
        {
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var keys = new TextBlock { Text = Shortcuts[i].Keys, FontFamily = new FontFamily("Cascadia Mono, Consolas") };
            var action = new TextBlock { Text = Loc.T(Shortcuts[i].Action), TextWrapping = TextWrapping.Wrap };
            Grid.SetRow(keys, i);
            Grid.SetRow(action, i);
            Grid.SetColumn(action, 1);
            grid.Children.Add(keys);
            grid.Children.Add(action);
        }
        await Create(root, theme, Loc.T("Klavye kısayolları"), new ScrollViewer { Content = grid, MaxHeight = 480, Padding = new Thickness(0, 0, 16, 0) }).ShowAsync();
    }

    public static async Task AboutAsync(XamlRoot root, ElementTheme theme)
    {
        var version = typeof(Dialogs).Assembly.GetName().Version;
        var panel = new StackPanel { Spacing = 8, MinWidth = 380 };
        panel.Children.Add(new TextBlock { Text = "Zero File Extractor " + (version is null ? string.Empty : $"{version.Major}.{version.Minor}.{version.Build}"), Style = (Style)Application.Current.Resources["SubtitleTextBlockStyle"] });
        panel.Children.Add(new TextBlock
        {
            Text = Loc.T("ZIP, RAR, 7Z, TAR ve daha fazlasını açan; ZIP, 7Z ve TAR ailesini oluşturan açık kaynak arşiv aracı."),
            TextWrapping = TextWrapping.Wrap,
        });
        panel.Children.Add(PanelHelpers.Hint(Loc.T("MIT lisansı · © Ali Apaydın\nSharpCompress (MIT) ve SharpZipLib (MIT) kullanır.\nRAR, RARLAB'in tescilli biçimidir; RAR oluşturmak için kurulu WinRAR kullanılır.")));
        panel.Children.Add(new HyperlinkButton { Content = "github.com/aliapaydin/Zero-File-Extractor", NavigateUri = new Uri("https://github.com/aliapaydin/Zero-File-Extractor"), Padding = new Thickness(0) });
        await Create(root, theme, Loc.T("Hakkında"), panel).ShowAsync();
    }

    /// <summary>Ayarlar: tema, dil, varsayılanlar.</summary>
    public static async Task SettingsAsync(XamlRoot root, ElementTheme theme)
    {
        var settings = AppSettings.Current;
        var panel = new StackPanel { Spacing = 6, MinWidth = 420 };

        panel.Children.Add(PanelHelpers.Label(Loc.T("Tema")));
        var themeBox = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch };
        themeBox.Items.Add(Loc.T("Sisteme göre"));
        themeBox.Items.Add(Loc.T("Aydınlık"));
        themeBox.Items.Add(Loc.T("Karanlık"));
        themeBox.SelectedIndex = (int)settings.Theme;
        panel.Children.Add(themeBox);

        panel.Children.Add(PanelHelpers.Label(Loc.T("Dil")));
        var language = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch };
        language.Items.Add(Loc.T("Windows'a göre"));
        language.Items.Add("Türkçe");
        language.Items.Add("English");
        language.SelectedIndex = settings.UiLanguage switch { "tr" => 1, "en" => 2, _ => 0 };
        panel.Children.Add(language);
        panel.Children.Add(PanelHelpers.Hint(Loc.T("Dil değişikliği uygulama yeniden açılınca geçerli olur.")));

        panel.Children.Add(PanelHelpers.Label(Loc.T("Varsayılan çıkarma klasörü kuralı")));
        var mode = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch };
        mode.Items.Add(Loc.T("Akıllı: arşivde tek klasör varsa doğrudan, yoksa yeni klasöre"));
        mode.Items.Add(Loc.T("Her arşiv kendi adında yeni klasöre"));
        mode.Items.Add(Loc.T("Doğrudan hedef klasöre"));
        mode.SelectedIndex = (int)settings.ExtractFolder;
        panel.Children.Add(mode);

        panel.Children.Add(PanelHelpers.Label(Loc.T("Aynı adlı dosya varsa")));
        var conflict = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch };
        conflict.Items.Add(Loc.T("Bana sor"));
        conflict.Items.Add(Loc.T("Üzerine yaz"));
        conflict.Items.Add(Loc.T("Atla"));
        conflict.Items.Add(Loc.T("İkisini de tut (yeniden adlandır)"));
        conflict.SelectedIndex = (int)settings.ExtractConflict;
        panel.Children.Add(conflict);

        var open = new CheckBox { Content = Loc.T("Çıkarma bitince klasörü aç"), IsChecked = settings.OpenFolderAfterExtract, Margin = new Thickness(0, 6, 0, 0) };
        var close = new CheckBox { Content = Loc.T("İş başarıyla bitince ilerleme penceresini kapat"), IsChecked = settings.CloseWhenDone };
        panel.Children.Add(open);
        panel.Children.Add(close);

        var dialog = Create(root, theme, Loc.T("Ayarlar"), new ScrollViewer { Content = panel, MaxHeight = 520, Padding = new Thickness(0, 0, 16, 0) },
            Loc.T("Kaydet"), Loc.T("Vazgeç"), ContentDialogButton.Primary);
        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;

        settings.UiLanguage = language.SelectedIndex switch { 1 => "tr", 2 => "en", _ => string.Empty };
        settings.ExtractFolder = (ExtractFolderMode)Math.Max(0, mode.SelectedIndex);
        settings.ExtractConflict = (ConflictMode)Math.Max(0, conflict.SelectedIndex);
        settings.OpenFolderAfterExtract = open.IsChecked == true;
        settings.CloseWhenDone = close.IsChecked == true;
        settings.Theme = (AppTheme)Math.Max(0, themeBox.SelectedIndex); // en son: pencereler temayı yeniden uygular
    }
}
