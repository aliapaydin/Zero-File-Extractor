using System.Security.Cryptography;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;
using Windows.Storage.Pickers;
using Windows.System;
using ZeroFileExtractor.Engine;
using ZeroFileExtractor.Models;
using ZeroFileExtractor.Services;
using ZeroFileExtractor.Views;

namespace ZeroFileExtractor;

/// <summary>Ana pencere: arşivin içinde gezinme, önizleme, çıkarma ve yeni arşiv oluşturma.</summary>
public sealed partial class MainWindow : Window
{
    private const int MaxListed = 20000;
    private const int TextPreviewBytes = 256 * 1024;
    private const long ImagePreviewLimit = 24 * 1024 * 1024;

    private static readonly List<MainWindow> Windows = new();
    private static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".png", ".jpg", ".jpeg", ".gif", ".bmp", ".webp", ".ico", ".tif", ".tiff",
    };

    private readonly AppSettings _settings = AppSettings.Current;
    private readonly IntPtr _hwnd;
    private readonly WindowPlacement _placement;
    private ArchiveSession? _session;
    private ArchiveIndex? _index;
    private FolderNode? _folder;
    private string _sortColumn;
    private bool _sortDescending;
    private string _search = string.Empty;
    private CancellationTokenSource? _previewCancel;
    private ArchiveItem? _previewItem;
    private bool _busy;

    public MainWindow(string? archivePath = null)
    {
        InitializeComponent();
        Loc.Apply(this); // XAML metinleri seçili dile
        WindowHelper.Track(this);
        Windows.Add(this);
        _hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);

        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
        AppWindow.SetIcon(WindowHelper.IconPath);
        _placement = Windows.Count == 1
            ? new WindowPlacement(AppWindow, DispatcherQueue, () => _settings.MainWindowBounds, value => _settings.MainWindowBounds = value)
            : new WindowPlacement(AppWindow, DispatcherQueue, () => string.Empty, _ => { });
        _placement.Restore(new global::Windows.Graphics.SizeInt32(1240, 800));

        _sortColumn = _settings.SortColumn;
        _sortDescending = _settings.SortDescending;

        TreeSplitter.Dragged += (_, dx) => TreeColumn.Width = new GridLength(Math.Clamp(TreeColumn.Width.Value + dx, 150, 560));
        TreeSplitter.DragCompleted += (_, _) => _settings.TreeWidth = TreeColumn.Width.Value;
        PreviewSplitter.Dragged += (_, dx) => PreviewColumn.Width = new GridLength(Math.Clamp(PreviewColumn.Width.Value - dx, 220, 900));
        PreviewSplitter.DragCompleted += (_, _) => _settings.PreviewWidth = PreviewColumn.Width.Value;

        _settings.Changed += Settings_Changed;
        Closed += (_, _) =>
        {
            _settings.Changed -= Settings_Changed;
            _previewCancel?.Cancel();
            _placement.Save();
            Windows.Remove(this);
        };

        ApplySettings();
        ShowArchiveState();
        if (archivePath is not null) _ = OpenArchiveAsync(archivePath);
    }

    private ElementTheme Theme => RootGrid.ActualTheme;

    /// <summary>Arşivi zaten açık olan pencereyi öne getirir; yoksa boş bir pencerede ya da yenisinde açar.</summary>
    public static void OpenOrActivate(string archivePath)
    {
        var full = Path.GetFullPath(archivePath);
        var existing = Windows.FirstOrDefault(w => w._session is not null && string.Equals(w._session.FilePath, full, StringComparison.OrdinalIgnoreCase));
        if (existing is not null)
        {
            _ = existing.ReloadAsync();
            NativeWindow.BringToFront(existing._hwnd);
            return;
        }
        var empty = Windows.FirstOrDefault(w => w._session is null && !w._busy);
        if (empty is not null)
        {
            _ = empty.OpenArchiveAsync(full);
            NativeWindow.BringToFront(empty._hwnd);
            return;
        }
        new MainWindow(full).Activate();
    }

    // ================================================================== arşiv açma / kapama

    private async Task OpenArchiveAsync(string path, string? keepFolder = null)
    {
        if (_busy) return;
        if (!File.Exists(path))
        {
            await Dialogs.MessageAsync(Content.XamlRoot, Theme, Loc.T("Dosya bulunamadı"), path);
            return;
        }

        SetBusy(true, Loc.T("Arşiv okunuyor…"));
        ArchiveSession? session = null;
        string? password = _session is not null && string.Equals(_session.FilePath, Path.GetFullPath(path), StringComparison.OrdinalIgnoreCase) ? _session.Password : null;
        try
        {
            while (session is null)
            {
                try
                {
                    var attempt = password;
                    session = await Task.Run(() => ArchiveSession.Open(path, attempt));
                }
                catch (PasswordRequiredException ex)
                {
                    password = await Dialogs.PasswordAsync(Content.XamlRoot, Theme, Path.GetFileName(path), ex.Wrong);
                    if (password is null) return;
                }
            }
        }
        catch (Exception ex)
        {
            CrashLog.Write("Arşiv açma", ex);
            await Dialogs.MessageAsync(Content.XamlRoot, Theme, Loc.T("Arşiv açılamadı"),
                Loc.F("\"{0}\" desteklenen bir arşiv değil ya da bozuk.", Path.GetFileName(path)) + "\n\n" + ex.Message);
            return;
        }
        finally
        {
            SetBusy(false);
        }

        _session = session;
        _index = new ArchiveIndex(session);
        _search = string.Empty;
        SearchBox.Text = string.Empty;
        _settings.AddRecent(session.FilePath);
        BuildTree();
        ShowArchiveState();
        Navigate(keepFolder is null ? _index.Root : _index.Find(keepFolder) ?? _index.Root);
        FileList.Focus(FocusState.Programmatic);
    }

    private Task ReloadAsync() =>
        _session is null ? Task.CompletedTask : OpenArchiveAsync(_session.FilePath, _folder?.Path);

    private void CloseArchive()
    {
        _previewCancel?.Cancel();
        _session = null;
        _index = null;
        _folder = null;
        _search = string.Empty;
        SearchBox.Text = string.Empty;
        FileList.ItemsSource = null;
        FolderTree.RootNodes.Clear();
        Crumbs.ItemsSource = null;
        ClearPreview();
        ShowArchiveState();
    }

    private void SetBusy(bool busy, string? text = null)
    {
        _busy = busy;
        if (text is not null) LoadingText.Text = text;
        LoadingPanel.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
        if (busy) WelcomePanel.Visibility = Visibility.Collapsed;
        else WelcomePanel.Visibility = _session is null ? Visibility.Visible : Visibility.Collapsed;
        FileList.IsEnabled = !busy;
        Ribbon.IsEnabled = !busy;
    }

    /// <summary>Arşiv açık/kapalı durumuna göre başlık, rozetler, karşılama ekranı ve komutlar.</summary>
    private void ShowArchiveState()
    {
        bool open = _session is not null;
        WelcomePanel.Visibility = open ? Visibility.Collapsed : Visibility.Visible;
        HeaderRow.Visibility = open ? Visibility.Visible : Visibility.Collapsed;
        PathBar.Visibility = open ? Visibility.Visible : Visibility.Collapsed;
        ContextTag.Visibility = open ? Visibility.Visible : Visibility.Collapsed;
        EmptyText.Visibility = Visibility.Collapsed;

        if (_session is { } session)
        {
            Title = Path.GetFileName(session.FilePath) + " — Zero File Extractor";
            ContextTagText.Text = session.FormatName;
            var info = Loc.F("{0} → {1}", Format.Size(session.TotalSize), Format.Size(session.FileLength));
            var ratio = Format.Ratio(session.FileLength, session.TotalSize);
            if (ratio.Length > 0) info += " (" + ratio + ")";
            info += " · " + Loc.F("{0} dosya", session.FileCount.ToString("N0"));
            if (session.IsSolid && session.Kind is ArchiveKind.Rar or ArchiveKind.SevenZip) info += " · Solid";
            ArchiveInfoText.Text = info;
            EncryptedBadge.Visibility = session.HasEncryptedEntries ? Visibility.Visible : Visibility.Collapsed;
        }
        else
        {
            Title = "Zero File Extractor";
            ArchiveInfoText.Text = string.Empty;
            EncryptedBadge.Visibility = Visibility.Collapsed;
            StatusText.Text = Loc.T("F1: klavye kısayolları");
            BuildRecent();
        }
        TitleText.Text = Title;
        UpdateCommands();
    }

    private void BuildRecent()
    {
        var recent = _settings.RecentArchives.Where(File.Exists).Take(8).ToList();
        RecentList.Children.Clear();
        MenuRecent.Items.Clear();
        foreach (var path in recent)
        {
            var captured = path;
            var button = new HyperlinkButton { Content = Path.GetFileName(path), Padding = new Thickness(4, 2, 4, 2) };
            ToolTipService.SetToolTip(button, path);
            button.Click += (_, _) => _ = OpenArchiveAsync(captured);
            RecentList.Children.Add(button);

            var item = new MenuFlyoutItem { Text = path };
            item.Click += (_, _) => _ = OpenArchiveAsync(captured);
            MenuRecent.Items.Add(item);
        }
        RecentPanel.Visibility = recent.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        MenuRecent.IsEnabled = recent.Count > 0;
    }

    // ================================================================== gezinme ve liste

    private void Navigate(FolderNode folder)
    {
        _folder = folder;
        if (_search.Length > 0)
        {
            _search = string.Empty;
            SearchBox.Text = string.Empty;
        }
        RefreshList();
        SelectTreeNode(folder);
    }

    private void RefreshList()
    {
        if (_session is null || _index is null || _folder is null) return;

        IEnumerable<ArchiveItem> items;
        bool truncated = false;
        if (_search.Length > 0)
        {
            var folders = _index.AllFolders.Where(f => f.Path.Length > 0 && f.Name.Contains(_search, StringComparison.CurrentCultureIgnoreCase))
                .Select(f => new ArchiveItem(f));
            var files = _session.Entries.Where(e => !e.IsDirectory && e.Name.Contains(_search, StringComparison.CurrentCultureIgnoreCase))
                .Select(e => new ArchiveItem(e, showFullPath: true));
            items = folders.Concat(files);
        }
        else if (_settings.FlatView)
        {
            items = _session.Entries.Where(e => !e.IsDirectory).Select(e => new ArchiveItem(e, showFullPath: true));
        }
        else
        {
            items = _folder.Folders.Select(f => new ArchiveItem(f)).Concat(_folder.Files.Select(e => new ArchiveItem(e)));
        }

        var list = Sort(items).ToList();
        if (list.Count > MaxListed)
        {
            list = list.Take(MaxListed).ToList();
            truncated = true;
        }
        FileList.ItemsSource = list;

        EmptyText.Text = _search.Length > 0 ? Loc.T("Aramayla eşleşen öğe yok.") : Loc.T("Bu klasör boş.");
        EmptyText.Visibility = list.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

        // Ekmek kırıntıları
        var crumbs = new List<FolderNode>();
        for (var node = _folder; node is not null; node = node.Parent) crumbs.Insert(0, node);
        Crumbs.ItemsSource = crumbs;

        UpdateHeaders();
        UpdateStatus(truncated);
        UpdateCommands();
        ClearPreview();
    }

    private IEnumerable<ArchiveItem> Sort(IEnumerable<ArchiveItem> items)
    {
        Func<ArchiveItem, IComparable> key = _sortColumn switch
        {
            "Size" => i => i.Size,
            "Packed" => i => i.Packed,
            "Ratio" => i => i.Size <= 0 ? 0d : (double)i.Packed / i.Size,
            "Type" => i => i.TypeText,
            "Modified" => i => i.Modified ?? DateTime.MinValue,
            _ => i => i.Name,
        };
        // Klasörler her zaman üstte; eşitlikte ada göre.
        var byFolder = items.OrderByDescending(i => i.IsDirectory);
        var ordered = _sortColumn == "Name" || _sortColumn == "Type"
            ? (_sortDescending
                ? byFolder.ThenByDescending(i => (string)key(i), StringComparer.CurrentCultureIgnoreCase)
                : byFolder.ThenBy(i => (string)key(i), StringComparer.CurrentCultureIgnoreCase))
            : (_sortDescending ? byFolder.ThenByDescending(key) : byFolder.ThenBy(key));
        return ordered.ThenBy(i => i.Name, StringComparer.CurrentCultureIgnoreCase);
    }

    private void UpdateHeaders()
    {
        void Set(Button button, string column, string text) =>
            button.Content = _sortColumn == column ? text + (_sortDescending ? "  ▼" : "  ▲") : text;
        Set(HeadName, "Name", Loc.T("Ad"));
        Set(HeadSize, "Size", Loc.T("Boyut"));
        Set(HeadPacked, "Packed", Loc.T("Sıkıştırılmış"));
        Set(HeadRatio, "Ratio", Loc.T("Oran"));
        Set(HeadType, "Type", Loc.T("Tür"));
        Set(HeadModified, "Modified", Loc.T("Değiştirilme"));
    }

    private void Header_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string column }) return;
        if (_sortColumn == column) _sortDescending = !_sortDescending;
        else
        {
            _sortColumn = column;
            _sortDescending = column is "Size" or "Packed" or "Modified";
        }
        _settings.SortColumn = _sortColumn;
        _settings.SortDescending = _sortDescending;
        RefreshList();
    }

    private void UpdateStatus(bool truncated = false)
    {
        if (_session is null) return;
        int count = (FileList.ItemsSource as List<ArchiveItem>)?.Count ?? 0;
        var selected = FileList.SelectedItems.OfType<ArchiveItem>().ToList();
        var text = Loc.F("{0} öğe", count.ToString("N0"));
        if (truncated) text += " · " + Loc.F("ilk {0} gösteriliyor", MaxListed.ToString("N0"));
        if (selected.Count > 0)
            text += " · " + Loc.F("{0} seçili ({1})", selected.Count.ToString("N0"), Format.Size(selected.Sum(i => i.Size)));
        if (_search.Length > 0) text += " · " + Loc.F("arama: \"{0}\"", _search);
        else if (_settings.FlatView) text += " · " + Loc.T("düz görünüm");
        StatusText.Text = text;
    }

    private void UpdateCommands()
    {
        bool open = _session is not null;
        bool any = open && FileList.SelectedItems.Count > 0;
        bool modify = open && _session!.CanModify;
        bool canUp = open && _folder?.Parent is not null;

        foreach (var control in new Control[] { RbExtractAll, RbExtractHere, RbTest, RbProperties, MenuExtractAll, MenuExtractHere, MenuTest, MenuChecksum, MenuProperties, MenuCloseArchive, MenuShowInExplorer })
            control.IsEnabled = open;
        foreach (var control in new Control[] { RbExtractSelected, RbPreview, MenuExtractSelected, MenuPreviewItem, MenuOpenItem, MenuCopy, MenuCopyPath })
            control.IsEnabled = any;
        foreach (var control in new Control[] { RbAdd, MenuAdd, MenuAddFolder })
            control.IsEnabled = modify;
        RbDelete.IsEnabled = MenuDelete.IsEnabled = modify && any;
        UpButton.IsEnabled = MenuUp.IsEnabled = canUp;
    }

    private void FileList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        UpdateStatus();
        UpdateCommands();
        if (_settings.ShowPreviewPane) _ = UpdatePreviewAsync(force: false);
    }

    private void Crumbs_ItemClicked(BreadcrumbBar sender, BreadcrumbBarItemClickedEventArgs args)
    {
        if (args.Item is FolderNode node) Navigate(node);
    }

    private void SearchBox_TextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
    {
        var text = sender.Text.Trim();
        if (text == _search) return;
        _search = text;
        RefreshList();
    }

    private void SearchBox_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Escape)
        {
            SearchBox.Text = string.Empty;
            FileList.Focus(FocusState.Programmatic);
            e.Handled = true;
        }
        else if (e.Key is VirtualKey.Down or VirtualKey.Enter)
        {
            FileList.Focus(FocusState.Programmatic);
            if (FileList.Items.Count > 0 && FileList.SelectedIndex < 0) FileList.SelectedIndex = 0;
            e.Handled = true;
        }
    }

    // ------------------------------------------------------------------ klasör ağacı

    private void BuildTree()
    {
        FolderTree.RootNodes.Clear();
        if (_index is null) return;
        var root = MakeTreeNode(_index.Root);
        FolderTree.RootNodes.Add(root);
        FillTreeNode(root);
        root.IsExpanded = true;
    }

    private static TreeViewNode MakeTreeNode(FolderNode folder) =>
        new() { Content = folder, HasUnrealizedChildren = folder.Folders.Count > 0 };

    private static void FillTreeNode(TreeViewNode node)
    {
        if (!node.HasUnrealizedChildren || node.Content is not FolderNode folder) return;
        foreach (var child in folder.Folders.OrderBy(f => f.Name, StringComparer.CurrentCultureIgnoreCase))
            node.Children.Add(MakeTreeNode(child));
        node.HasUnrealizedChildren = false;
    }

    private void FolderTree_Expanding(TreeView sender, TreeViewExpandingEventArgs args) => FillTreeNode(args.Node);

    private void FolderTree_ItemInvoked(TreeView sender, TreeViewItemInvokedEventArgs args)
    {
        if (args.InvokedItem is TreeViewNode { Content: FolderNode folder }) Navigate(folder);
    }

    /// <summary>Ağaçta geçerli klasörü görünür kılıp seçer.</summary>
    private void SelectTreeNode(FolderNode folder)
    {
        if (FolderTree.RootNodes.Count == 0 || TreeHost.Visibility != Visibility.Visible) return;
        var chain = new List<FolderNode>();
        for (var node = folder; node is not null; node = node.Parent) chain.Insert(0, node);
        var current = FolderTree.RootNodes[0];
        for (int i = 1; i < chain.Count; i++)
        {
            FillTreeNode(current);
            current.IsExpanded = true;
            var next = current.Children.FirstOrDefault(c => ReferenceEquals(c.Content, chain[i]));
            if (next is null) return;
            current = next;
        }
        FolderTree.SelectedNode = current;
    }

    // ================================================================== komutlar

    private void Command_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string tag }) _ = RunAsync(tag);
    }

    private void Accel_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        bool ctrl = sender.Modifiers.HasFlag(VirtualKeyModifiers.Control);
        bool shift = sender.Modifiers.HasFlag(VirtualKeyModifiers.Shift);
        bool alt = sender.Modifiers.HasFlag(VirtualKeyModifiers.Menu);
        string? tag = (sender.Key, ctrl, shift, alt) switch
        {
            (VirtualKey.O, true, false, false) => "Open",
            (VirtualKey.N, true, false, false) => "NewFromFiles",
            (VirtualKey.N, true, true, false) => "NewFromFolder",
            (VirtualKey.W, true, false, false) => "CloseArchive",
            (VirtualKey.E, true, false, false) or (VirtualKey.F5, false, false, false) => "ExtractAll",
            (VirtualKey.E, true, true, false) or (VirtualKey.F6, false, false, false) => "ExtractSelected",
            (VirtualKey.H, true, true, false) => "ExtractHere",
            (VirtualKey.T, true, false, false) => "Test",
            (VirtualKey.Q, true, false, false) => "TogglePreview",
            (VirtualKey.T, true, true, false) => "ToggleTree",
            (VirtualKey.B, true, false, false) => "ToggleFlat",
            (VirtualKey.F, true, false, false) => "Search",
            (VirtualKey.A, true, true, false) => "AddFiles",
            (VirtualKey.I, true, false, false) => "InvertSelection",
            (VirtualKey.C, true, true, false) => "CopyPath",
            (VirtualKey.R, true, false, false) => "Refresh",
            (VirtualKey.Enter, false, false, true) => "Properties",
            (VirtualKey.Up, false, false, true) => "Up",
            (VirtualKey.F1, false, false, false) => "Shortcuts",
            (VirtualKey.F3, false, false, false) => "Preview",
            _ => null,
        };
        if (tag is null) return;
        args.Handled = true;
        _ = RunAsync(tag);
    }

    private void FileList_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        bool ctrl = Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Control).HasFlag(global::Windows.UI.Core.CoreVirtualKeyStates.Down);
        string? tag = e.Key switch
        {
            VirtualKey.Enter => "OpenItem",
            VirtualKey.Back => "Up",
            VirtualKey.Delete => "Delete",
            VirtualKey.Insert => "AddFiles",
            VirtualKey.C when ctrl => "Copy",
            (VirtualKey)188 when ctrl => "Settings", // Ctrl+,
            VirtualKey.Escape when _search.Length > 0 => "ClearSearch",
            _ => null,
        };
        if (tag is null) return;
        e.Handled = true;
        _ = RunAsync(tag);
    }

    private void FileList_DoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        // Yalnızca bir satıra çift tıklanınca (boş alana değil).
        if ((e.OriginalSource as FrameworkElement)?.DataContext is ArchiveItem) _ = RunAsync("OpenItem");
    }

    private void ItemMenu_Opening(object sender, object e)
    {
        ItemMenuDelete.IsEnabled = _session?.CanModify == true && FileList.SelectedItems.Count > 0;
    }

    private async Task RunAsync(string tag)
    {
        try
        {
            switch (tag)
            {
                case "Open": await PickAndOpenAsync(); break;
                case "NewFromFiles": await NewArchiveAsync(folder: false); break;
                case "NewFromFolder": await NewArchiveAsync(folder: true); break;
                case "CloseArchive": CloseArchive(); break;
                case "ShowInExplorer": if (_session is not null) ShellActions.ShowInExplorer(_session.FilePath); break;
                case "Properties": if (_session is not null) await Dialogs.PropertiesAsync(Content.XamlRoot, Theme, _session); break;
                case "Settings": await Dialogs.SettingsAsync(Content.XamlRoot, Theme); break;
                case "Exit": Close(); break;

                case "ExtractAll": await ExtractAsync(selectedOnly: false); break;
                case "ExtractSelected": await ExtractAsync(selectedOnly: true); break;
                case "ExtractHere": ExtractNextToArchive(); break;
                case "Test": if (_session is not null) JobWindow.Run(new Job[] { new TestJob { ArchivePath = _session.FilePath, Session = _session } }); break;
                case "Checksum": await ChecksumAsync(); break;

                case "OpenItem": await OpenSelectedAsync(); break;
                case "Preview": await ShowPreviewAsync(); break;
                case "Up": if (_folder?.Parent is { } parent) Navigate(parent); break;

                case "AddFiles": await AddToArchiveAsync(folder: false); break;
                case "AddFolder": await AddToArchiveAsync(folder: true); break;
                case "Delete": await DeleteSelectedAsync(); break;
                case "Copy": await CopySelectedAsync(); break;
                case "CopyPath": CopyPaths(); break;
                case "SelectAll": FileList.SelectAll(); break;
                case "InvertSelection": InvertSelection(); break;
                case "Search": SearchBox.Focus(FocusState.Programmatic); break;
                case "ClearSearch": SearchBox.Text = string.Empty; break;

                case "ToggleRibbon": _settings.ShowRibbon = !_settings.ShowRibbon; break;
                case "ToggleTree": _settings.ShowFolderTree = !_settings.ShowFolderTree; break;
                case "TogglePreview": _settings.ShowPreviewPane = !_settings.ShowPreviewPane; break;
                case "ToggleStatus": _settings.ShowStatusBar = !_settings.ShowStatusBar; break;
                case "ToggleFlat": _settings.FlatView = !_settings.FlatView; break;
                case "Refresh": await ReloadAsync(); break;
                case "Theme0": _settings.Theme = AppTheme.System; break;
                case "Theme1": _settings.Theme = AppTheme.Light; break;
                case "Theme2": _settings.Theme = AppTheme.Dark; break;

                case "Shortcuts": await Dialogs.ShortcutsAsync(Content.XamlRoot, Theme); break;
                case "About": await Dialogs.AboutAsync(Content.XamlRoot, Theme); break;
                case "GitHub": ShellActions.Open("https://github.com/aliapaydin/Zero-File-Extractor"); break;
                case "OpenLog":
                    if (File.Exists(CrashLog.FilePath)) ShellActions.Open(CrashLog.FilePath);
                    else await Dialogs.MessageAsync(Content.XamlRoot, Theme, Loc.T("Hata günlüğü"), Loc.T("Kayıtlı hata yok."));
                    break;
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            CrashLog.Write("Komut " + tag, ex);
            try
            {
                await Dialogs.MessageAsync(Content.XamlRoot, Theme, Loc.T("İşlem tamamlanamadı"), ex.Message);
            }
            catch (Exception)
            {
                // başka bir diyalog açıkken ikinci diyalog gösterilemez
            }
        }
        catch (OperationCanceledException)
        {
            // kullanıcı vazgeçti
        }
    }

    // ------------------------------------------------------------------ aç / yeni arşiv

    private async Task PickAndOpenAsync()
    {
        var picker = new FileOpenPicker { SuggestedStartLocation = PickerLocationId.Downloads };
        foreach (var ext in ArchiveFormats.OpenExtensions) picker.FileTypeFilter.Add(ext);
        picker.FileTypeFilter.Add("*");
        WinRT.Interop.InitializeWithWindow.Initialize(picker, _hwnd);
        var file = await picker.PickSingleFileAsync();
        if (file is not null) await OpenArchiveAsync(file.Path);
    }

    private async Task<List<string>> PickSourcesAsync(bool folder)
    {
        if (folder)
        {
            var picked = await PanelHelpers.PickFolderAsync(_hwnd);
            return picked is null ? new List<string>() : new List<string> { picked };
        }
        var picker = new FileOpenPicker { SuggestedStartLocation = PickerLocationId.DocumentsLibrary };
        picker.FileTypeFilter.Add("*");
        WinRT.Interop.InitializeWithWindow.Initialize(picker, _hwnd);
        var files = await picker.PickMultipleFilesAsync();
        return files.Select(f => f.Path).Where(p => !string.IsNullOrEmpty(p)).ToList();
    }

    private async Task NewArchiveAsync(bool folder)
    {
        var sources = await PickSourcesAsync(folder);
        if (sources.Count > 0) await CompressDialogAsync(sources);
    }

    private async Task CompressDialogAsync(IReadOnlyList<string> sources)
    {
        var panel = new CompressOptionsPanel(sources, _hwnd);
        using var scanCancel = new CancellationTokenSource();
        _ = panel.ScanAsync(scanCancel.Token);
        var dialog = Dialogs.Create(Content.XamlRoot, Theme, Loc.T("Yeni arşiv"),
            new ScrollViewer { Content = panel, MaxHeight = 560, Padding = new Thickness(0, 0, 16, 0) },
            Loc.T("Sıkıştır"), Loc.T("Vazgeç"), ContentDialogButton.Primary);
        CompressJob? job = null;
        dialog.PrimaryButtonClick += (_, args) =>
        {
            job = panel.BuildJob();
            args.Cancel = job is null;
        };
        var result = await dialog.ShowAsync();
        scanCancel.Cancel();
        if (result != ContentDialogResult.Primary || job is null) return;
        JobWindow.Run(new Job[] { job });
    }

    // ------------------------------------------------------------------ çıkarma

    private (List<string> Folders, List<ArchiveEntryInfo> Files) SelectedParts()
    {
        var folders = new List<string>();
        var files = new List<ArchiveEntryInfo>();
        foreach (var item in FileList.SelectedItems.OfType<ArchiveItem>())
        {
            if (item.Folder is not null) folders.Add(item.Folder.Path);
            else if (item.Entry is not null) files.Add(item.Entry);
        }
        return (folders, files);
    }

    /// <summary>Seçim tek bir klasörün içinden mi yapıldı (yollar o klasöre göre kısaltılabilir mi)?</summary>
    private string SelectionBase => _search.Length > 0 || _settings.FlatView ? string.Empty : _folder?.Path ?? string.Empty;

    private async Task ExtractAsync(bool selectedOnly)
    {
        if (_session is not { } session) return;
        List<ArchiveEntryInfo>? selection = null;
        if (selectedOnly)
        {
            var (folders, files) = SelectedParts();
            if (folders.Count == 0 && files.Count == 0) return;
            selection = session.Collect(folders, files);
        }

        long bytes = selection?.Sum(e => e.Size) ?? session.TotalSize;
        int count = selection?.Count(e => !e.IsDirectory) ?? session.FileCount;
        var summary = Loc.F("{0} · {1} dosya · yaklaşık {2}", Format.Size(bytes), count.ToString("N0"),
            Format.Duration(Estimator.ExtractTime(session.Kind, bytes, count)));

        var panel = new ExtractOptionsPanel(_hwnd, Path.GetDirectoryName(session.FilePath)!, wholeArchive: !selectedOnly,
            ArchiveFormats.StripExtension(Path.GetFileName(session.FilePath)), summary);
        var dialog = Dialogs.Create(Content.XamlRoot, Theme,
            selectedOnly ? Loc.T("Seçilenleri çıkar") : Loc.T("Tümünü çıkar"),
            new ScrollViewer { Content = panel, MaxHeight = 560, Padding = new Thickness(0, 0, 16, 0) },
            Loc.T("Çıkar"), Loc.T("Vazgeç"), ContentDialogButton.Primary);
        dialog.PrimaryButtonClick += (_, args) => args.Cancel = !panel.Validate();
        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;

        JobWindow.Run(new Job[]
        {
            new ExtractJob
            {
                ArchivePath = session.FilePath,
                Session = session,
                Selection = selection,
                Destination = panel.Destination,
                FolderMode = panel.FolderMode,
                Options = new ExtractOptions { Conflict = panel.Conflict, Flatten = panel.Flatten, StripPrefix = selectedOnly ? SelectionBase : string.Empty },
                DeleteArchive = panel.DeleteArchive,
                OpenFolderWhenDone = panel.OpenFolder,
            },
        });
    }

    /// <summary>Soru sormadan, arşivin bulunduğu klasöre (akıllı klasör kuralıyla) çıkarır.</summary>
    private void ExtractNextToArchive()
    {
        if (_session is not { } session) return;
        JobWindow.Run(new Job[]
        {
            new ExtractJob
            {
                ArchivePath = session.FilePath,
                Session = session,
                Destination = Path.GetDirectoryName(session.FilePath)!,
                FolderMode = ExtractFolderMode.Smart,
                Options = new ExtractOptions { Conflict = _settings.ExtractConflict },
                OpenFolderWhenDone = _settings.OpenFolderAfterExtract,
            },
        });
    }

    /// <summary>İş penceresi açmadan çalışan kısa işler (önizleme, geçici çıkarma) için bağlam.</summary>
    private JobContext InlineContext(CancellationToken cancellation) => new()
    {
        Cancellation = cancellation,
        RequestPassword = wrong =>
        {
            var completion = new TaskCompletionSource<string?>();
            bool queued = DispatcherQueue.TryEnqueue(async () =>
            {
                try
                {
                    completion.SetResult(await Dialogs.PasswordAsync(Content.XamlRoot, Theme, Path.GetFileName(_session?.FilePath ?? string.Empty), wrong));
                }
                catch (Exception)
                {
                    completion.SetResult(null); // başka diyalog açık
                }
            });
            return queued ? completion.Task.GetAwaiter().GetResult() : null;
        },
    };

    /// <summary>Seçimi geçici bir klasöre çıkarır; kökteki öğelerin yollarını döner.</summary>
    private async Task<List<string>> ExtractToTempAsync(List<string> folders, List<ArchiveEntryInfo> files, string basePath)
    {
        if (_session is not { } session) return new List<string>();
        var selection = session.Collect(folders, files);
        var temp = TempFiles.NewFolder();
        var context = InlineContext(CancellationToken.None);
        SetBusy(true, Loc.T("Dosyalar hazırlanıyor…"));
        try
        {
            await Task.Run(() => session.Extract(selection, temp, new ExtractOptions { Conflict = ConflictMode.Overwrite, StripPrefix = basePath }, context));
        }
        finally
        {
            SetBusy(false);
        }
        return Directory.EnumerateFileSystemEntries(temp).ToList();
    }

    private async Task OpenSelectedAsync()
    {
        if (_session is null || FileList.SelectedItem is not ArchiveItem item) return;
        if (item.Folder is not null)
        {
            Navigate(item.Folder);
            FileList.Focus(FocusState.Programmatic);
            return;
        }
        if (item.Entry is null) return;

        var extracted = await ExtractToTempAsync(new List<string>(), new List<ArchiveEntryInfo> { item.Entry }, item.Entry.Parent);
        var path = extracted.FirstOrDefault(File.Exists);
        if (path is null) return;
        // Arşiv içindeki arşiv: yeni pencerede aç.
        if (ArchiveFormats.IsArchivePath(path)) new MainWindow(path).Activate();
        else ShellActions.Open(path);
    }

    private async Task CopySelectedAsync()
    {
        var (folders, files) = SelectedParts();
        if (folders.Count == 0 && files.Count == 0) return;
        var paths = await ExtractToTempAsync(folders, files, SelectionBase);
        var items = await ToStorageItemsAsync(paths);
        if (items.Count == 0) return;
        var package = new DataPackage { RequestedOperation = DataPackageOperation.Copy };
        package.SetStorageItems(items);
        Clipboard.SetContent(package);
        Clipboard.Flush();
        StatusText.Text = Loc.F("{0} öğe panoya kopyalandı; Dosya Gezgini'nde yapıştırabilirsin.", items.Count);
    }

    private static async Task<List<IStorageItem>> ToStorageItemsAsync(IEnumerable<string> paths)
    {
        var items = new List<IStorageItem>();
        foreach (var path in paths)
        {
            try
            {
                if (Directory.Exists(path)) items.Add(await StorageFolder.GetFolderFromPathAsync(path));
                else if (File.Exists(path)) items.Add(await StorageFile.GetFileFromPathAsync(path));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
            {
                CrashLog.Write("Depolama öğesi", ex);
            }
        }
        return items;
    }

    private void CopyPaths()
    {
        var paths = FileList.SelectedItems.OfType<ArchiveItem>().Select(i => i.PathInArchive).ToList();
        if (paths.Count == 0) return;
        var package = new DataPackage();
        package.SetText(string.Join(Environment.NewLine, paths));
        Clipboard.SetContent(package);
    }

    private void InvertSelection()
    {
        var selected = new HashSet<object>(FileList.SelectedItems);
        FileList.SelectedItems.Clear();
        foreach (var item in FileList.Items)
            if (!selected.Contains(item)) FileList.SelectedItems.Add(item);
    }

    // ------------------------------------------------------------------ arşivi değiştirme (ZIP)

    private async Task AddToArchiveAsync(bool folder)
    {
        if (_session is not { CanModify: true }) return;
        var sources = await PickSourcesAsync(folder);
        if (sources.Count > 0) StartAdd(sources);
    }

    private void StartAdd(IReadOnlyList<string> sources)
    {
        if (_session is not { CanModify: true } session) return;
        var window = JobWindow.Run(new Job[]
        {
            new ZipAddJob { ZipPath = session.FilePath, Sources = sources, TargetFolder = _folder?.Path ?? string.Empty, Level = _settings.DefaultLevel },
        });
        window.JobSucceeded += (_, _) => _ = ReloadAsync();
    }

    private async Task DeleteSelectedAsync()
    {
        if (_session is not { CanModify: true } session) return;
        var (folders, files) = SelectedParts();
        int count = folders.Count + files.Count;
        if (count == 0) return;
        var message = count == 1
            ? Loc.F("\"{0}\" arşivden silinsin mi?", FileList.SelectedItems.OfType<ArchiveItem>().First().Name)
            : Loc.F("{0} öğe arşivden silinsin mi?", count);
        message += "\n\n" + Loc.T("Bu işlem arşiv dosyasını değiştirir ve geri alınamaz.");
        if (!await Dialogs.ConfirmAsync(Content.XamlRoot, Theme, Loc.T("Arşivden sil"), message, Loc.T("Sil"))) return;

        var window = JobWindow.Run(new Job[]
        {
            new ZipDeleteJob { ZipPath = session.FilePath, Files = files.Select(f => f.Path).ToList(), Folders = folders },
        });
        window.JobSucceeded += (_, _) => _ = ReloadAsync();
    }

    private async Task ChecksumAsync()
    {
        if (_session is not { } session) return;
        SetBusy(true, Loc.T("SHA-256 hesaplanıyor…"));
        string hash;
        try
        {
            hash = await Task.Run(() =>
            {
                using var stream = File.OpenRead(session.FilePath);
                return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
            });
        }
        finally
        {
            SetBusy(false);
        }
        var dialog = Dialogs.Create(Content.XamlRoot, Theme, "SHA-256 · " + Path.GetFileName(session.FilePath),
            new TextBlock { Text = hash, TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true, FontFamily = new FontFamily("Cascadia Mono, Consolas") },
            Loc.T("Kopyala"), Loc.T("Kapat"), ContentDialogButton.Primary);
        if (await dialog.ShowAsync() == ContentDialogResult.Primary)
        {
            var package = new DataPackage();
            package.SetText(hash);
            Clipboard.SetContent(package);
        }
    }

    // ================================================================== önizleme

    private async Task ShowPreviewAsync()
    {
        if (!_settings.ShowPreviewPane) _settings.ShowPreviewPane = true;
        await UpdatePreviewAsync(force: true);
    }

    private void ClearPreview()
    {
        _previewCancel?.Cancel();
        _previewItem = null;
        PreviewTitle.Text = Loc.T("Önizleme");
        PreviewImage.Visibility = Visibility.Collapsed;
        PreviewImage.Source = null;
        PreviewText.Visibility = Visibility.Collapsed;
        PreviewText.Text = string.Empty;
        PreviewInfo.Children.Clear();
        PreviewLoadButton.Visibility = Visibility.Collapsed;
        PreviewNote.Visibility = Visibility.Collapsed;
        PreviewBusy.IsActive = false;
        PreviewBusy.Visibility = Visibility.Collapsed;
        if (_session is not null)
        {
            PreviewNote.Text = Loc.T("Önizlemek için bir dosya seç.");
            PreviewNote.Visibility = Visibility.Visible;
        }
    }

    private void PreviewLoad_Click(object sender, RoutedEventArgs e) => _ = UpdatePreviewAsync(force: true);

    /// <param name="force">Solid arşivlerde ve büyük dosyalarda içerik yalnızca istenince yüklenir.</param>
    private async Task UpdatePreviewAsync(bool force)
    {
        if (_session is not { } session || PreviewHost.Visibility != Visibility.Visible) return;
        if (FileList.SelectedItems.Count != 1 || FileList.SelectedItem is not ArchiveItem item)
        {
            ClearPreview();
            if (FileList.SelectedItems.Count > 1)
                PreviewNote.Text = Loc.F("{0} öğe seçili", FileList.SelectedItems.Count);
            return;
        }
        if (ReferenceEquals(item, _previewItem) && !force) return;

        ClearPreview();
        _previewItem = item;
        PreviewNote.Visibility = Visibility.Collapsed;
        PreviewTitle.Text = item.Name;

        void Info(string label, string value)
        {
            if (value.Length == 0) return;
            var row = new Grid { ColumnSpacing = 12 };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(110) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.Children.Add(new TextBlock { Text = label, Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"] });
            var text = new TextBlock { Text = value, TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true };
            Grid.SetColumn(text, 1);
            row.Children.Add(text);
            PreviewInfo.Children.Add(row);
        }

        Info(Loc.T("Tür"), item.TypeText);
        Info(Loc.T("Boyut"), item.SizeText);
        if (item.Folder is { } folder)
        {
            Info(Loc.T("İçerik"), Loc.F("{0} dosya, {1} alt klasör", folder.TotalFiles.ToString("N0"), folder.Folders.Count.ToString("N0")));
            Info(Loc.T("Değiştirilme"), item.ModifiedText);
            return;
        }
        if (item.Entry is not { } entry) return;
        Info(Loc.T("Sıkıştırılmış"), item.PackedText);
        Info(Loc.T("Oran"), item.RatioText);
        Info(Loc.T("Yöntem"), entry.Method);
        Info(Loc.T("Değiştirilme"), item.ModifiedText);
        if (entry.Crc != 0) Info("CRC", ((uint)entry.Crc).ToString("X8"));
        Info(Loc.T("Şifreli"), entry.IsEncrypted ? Loc.T("Evet") : string.Empty);
        Info(Loc.T("Yol"), entry.Path);

        var extension = Path.GetExtension(entry.Name);
        bool image = ImageExtensions.Contains(extension);
        if (image && entry.Size > ImagePreviewLimit)
        {
            PreviewNote.Text = Loc.T("Görsel önizleme için çok büyük.");
            PreviewNote.Visibility = Visibility.Visible;
            return;
        }
        if (!image && ArchiveFormats.IncompressibleExtensions.Contains(extension)) return; // ikili içerik: metin önizlemesi anlamsız

        // Solid arşivlerde tek dosyayı okumak için öncekilerin de açılması gerekir; yavaş olabilir.
        bool costly = session.IsSolid && session.FileLength > 8 * 1024 * 1024;
        if ((costly || entry.IsEncrypted && session.Password is null) && !force)
        {
            PreviewLoadButton.Visibility = Visibility.Visible;
            PreviewNote.Text = costly
                ? Loc.T("Bu arşiv tek blok (solid) olarak sıkıştırılmış; önizleme için arşivin bir kısmının açılması gerekir.")
                : Loc.T("Bu dosya şifreli; önizleme için parola gerekir.");
            PreviewNote.Visibility = Visibility.Visible;
            return;
        }

        var cancel = new CancellationTokenSource();
        _previewCancel = cancel;
        PreviewBusy.IsActive = true;
        PreviewBusy.Visibility = Visibility.Visible;
        try
        {
            await Task.Delay(120, cancel.Token); // hızlı gezinirken her satırı okuma
            var context = InlineContext(cancel.Token);
            int limit = image ? (int)Math.Min(entry.Size + 1, ImagePreviewLimit) : TextPreviewBytes;
            var data = await Task.Run(() => session.ReadHead(entry, Math.Max(limit, 1), context), cancel.Token);
            if (cancel.IsCancellationRequested || !ReferenceEquals(_previewItem, item)) return;

            if (image)
            {
                var bitmap = new BitmapImage();
                using var stream = new global::Windows.Storage.Streams.InMemoryRandomAccessStream();
                using (var writer = new global::Windows.Storage.Streams.DataWriter(stream))
                {
                    writer.WriteBytes(data);
                    await writer.StoreAsync();
                    writer.DetachStream();
                }
                stream.Seek(0);
                await bitmap.SetSourceAsync(stream);
                PreviewImage.Source = bitmap;
                PreviewImage.Visibility = Visibility.Visible;
                Info(Loc.T("Boyutlar"), $"{bitmap.PixelWidth} × {bitmap.PixelHeight}");
            }
            else if (LooksLikeText(data))
            {
                var text = DecodeText(data);
                if (text.Length > 60000) text = text[..60000];
                PreviewText.Text = text;
                PreviewText.Visibility = Visibility.Visible;
                if (entry.Size > data.Length)
                {
                    PreviewNote.Text = Loc.F("İlk {0} gösteriliyor.", Format.Size(data.Length));
                    PreviewNote.Visibility = Visibility.Visible;
                }
            }
            else
            {
                PreviewText.Text = HexDump(data, 512);
                PreviewText.Visibility = Visibility.Visible;
                PreviewNote.Text = Loc.T("İkili dosya: ilk baytlar gösteriliyor.");
                PreviewNote.Visibility = Visibility.Visible;
            }
        }
        catch (OperationCanceledException)
        {
            // başka bir öğe seçildi
        }
        catch (Exception ex)
        {
            if (ReferenceEquals(_previewItem, item))
            {
                PreviewNote.Text = Loc.T("Önizleme yüklenemedi.") + " " + ex.Message;
                PreviewNote.Visibility = Visibility.Visible;
            }
        }
        finally
        {
            if (ReferenceEquals(_previewCancel, cancel))
            {
                PreviewBusy.IsActive = false;
                PreviewBusy.Visibility = Visibility.Collapsed;
                _previewCancel = null;
            }
            cancel.Dispose();
        }
    }

    private static bool LooksLikeText(byte[] data)
    {
        if (data.Length == 0) return true;
        if (data.Length >= 2 && ((data[0] == 0xFF && data[1] == 0xFE) || (data[0] == 0xFE && data[1] == 0xFF))) return true; // UTF-16 BOM
        int sample = Math.Min(data.Length, 4096), control = 0;
        for (int i = 0; i < sample; i++)
        {
            byte b = data[i];
            if (b == 0) return false;
            if (b < 32 && b != 9 && b != 10 && b != 13 && b != 12 && b != 27) control++;
        }
        return control * 50 < sample;
    }

    private static string DecodeText(byte[] data)
    {
        if (data.Length >= 2 && data[0] == 0xFF && data[1] == 0xFE) return System.Text.Encoding.Unicode.GetString(data, 2, data.Length - 2);
        if (data.Length >= 2 && data[0] == 0xFE && data[1] == 0xFF) return System.Text.Encoding.BigEndianUnicode.GetString(data, 2, data.Length - 2);
        if (data.Length >= 3 && data[0] == 0xEF && data[1] == 0xBB && data[2] == 0xBF) return System.Text.Encoding.UTF8.GetString(data, 3, data.Length - 3);
        try
        {
            return new System.Text.UTF8Encoding(false, true).GetString(data);
        }
        catch (System.Text.DecoderFallbackException)
        {
            // UTF-8 değil (ya da son karakter yarıda kesilmiş): sistemin ANSI kod sayfasıyla dene.
            try
            {
                return System.Text.Encoding.GetEncoding(System.Globalization.CultureInfo.CurrentCulture.TextInfo.ANSICodePage).GetString(data);
            }
            catch (Exception)
            {
                return System.Text.Encoding.UTF8.GetString(data);
            }
        }
    }

    private static string HexDump(byte[] data, int max)
    {
        var builder = new System.Text.StringBuilder();
        int length = Math.Min(data.Length, max);
        for (int offset = 0; offset < length; offset += 16)
        {
            builder.Append(offset.ToString("X6")).Append("  ");
            for (int i = 0; i < 16; i++)
                builder.Append(offset + i < length ? data[offset + i].ToString("X2") + " " : "   ");
            builder.Append(' ');
            for (int i = 0; i < 16 && offset + i < length; i++)
            {
                byte b = data[offset + i];
                builder.Append(b is >= 32 and < 127 ? (char)b : '.');
            }
            builder.AppendLine();
        }
        return builder.ToString();
    }

    // ================================================================== sürükle-bırak

    private void Root_DragOver(object sender, DragEventArgs e)
    {
        // Kendi listemizden başlayan sürükleme pencereye geri bırakılamaz.
        if (e.DataView.Properties.ContainsKey("zfx-internal") || !e.DataView.Contains(StandardDataFormats.StorageItems) || _busy)
        {
            e.AcceptedOperation = DataPackageOperation.None;
            return;
        }
        e.AcceptedOperation = DataPackageOperation.Copy;
        string hint = _session is { CanModify: true } && !e.Modifiers.HasFlag(global::Windows.ApplicationModel.DataTransfer.DragDrop.DragDropModifiers.Shift)
            ? Loc.T("Arşive eklemek için bırak · Shift: yeni arşiv")
            : Loc.T("Açmak ya da sıkıştırmak için bırak");
        e.DragUIOverride.Caption = hint;
        e.DragUIOverride.IsCaptionVisible = true;
        DropHintText.Text = hint;
        DropHint.Visibility = Visibility.Visible;
    }

    private void Root_DragLeave(object sender, DragEventArgs e) => DropHint.Visibility = Visibility.Collapsed;

    private async void Root_Drop(object sender, DragEventArgs e)
    {
        DropHint.Visibility = Visibility.Collapsed;
        if (!e.DataView.Contains(StandardDataFormats.StorageItems)) return;
        bool shift = e.Modifiers.HasFlag(global::Windows.ApplicationModel.DataTransfer.DragDrop.DragDropModifiers.Shift);
        var deferral = e.GetDeferral();
        List<string> paths;
        try
        {
            var items = await e.DataView.GetStorageItemsAsync();
            paths = items.Select(i => i.Path).Where(p => !string.IsNullOrEmpty(p)).ToList();
        }
        catch (Exception ex)
        {
            CrashLog.Write("Bırakma", ex);
            return;
        }
        finally
        {
            deferral.Complete();
        }
        if (paths.Count == 0) return;

        try
        {
            // Tek bir arşiv bırakıldıysa aç; aksi halde açık ZIP'e ekle ya da yeni arşiv oluştur.
            if (!shift && paths.Count == 1 && File.Exists(paths[0]) && ArchiveFormats.IsArchivePath(paths[0]))
                await OpenArchiveAsync(paths[0]);
            else if (!shift && _session is { CanModify: true })
                StartAdd(paths);
            else
                await CompressDialogAsync(paths);
        }
        catch (Exception ex)
        {
            CrashLog.Write("Bırakma", ex);
        }
    }

    /// <summary>Listeden Dosya Gezgini'ne (ya da başka uygulamaya) sürükleme: bırakıldığında geçici klasöre çıkarılıp verilir.</summary>
    private void FileList_DragItemsStarting(object sender, DragItemsStartingEventArgs e)
    {
        if (_session is null)
        {
            e.Cancel = true;
            return;
        }
        var folders = new List<string>();
        var files = new List<ArchiveEntryInfo>();
        foreach (var item in e.Items.OfType<ArchiveItem>())
        {
            if (item.Folder is not null) folders.Add(item.Folder.Path);
            else if (item.Entry is not null) files.Add(item.Entry);
        }
        if (folders.Count == 0 && files.Count == 0)
        {
            e.Cancel = true;
            return;
        }
        var basePath = SelectionBase;
        e.Data.Properties.Add("zfx-internal", true);
        e.Data.RequestedOperation = DataPackageOperation.Copy;
        e.Data.SetDataProvider(StandardDataFormats.StorageItems, async request =>
        {
            var deferral = request.GetDeferral();
            try
            {
                var paths = await ExtractToTempAsync(folders, files, basePath);
                request.SetData(await ToStorageItemsAsync(paths));
            }
            catch (Exception ex)
            {
                CrashLog.Write("Dışarı sürükleme", ex);
            }
            finally
            {
                deferral.Complete();
            }
        });
    }

    // ================================================================== ayarlar ve tema

    private void Settings_Changed(object? sender, string key)
    {
        if (!DispatcherQueue.HasThreadAccess)
        {
            DispatcherQueue.TryEnqueue(() => Settings_Changed(sender, key));
            return;
        }
        switch (key)
        {
            case nameof(AppSettings.FlatView):
                MenuFlat.IsChecked = _settings.FlatView;
                RefreshList();
                break;
            case nameof(AppSettings.Theme):
            case nameof(AppSettings.ShowRibbon):
            case nameof(AppSettings.ShowFolderTree):
            case nameof(AppSettings.ShowPreviewPane):
            case nameof(AppSettings.ShowStatusBar):
            case "All":
                ApplySettings();
                break;
            case nameof(AppSettings.RecentArchives):
                if (_session is null) BuildRecent();
                break;
        }
    }

    private void ApplySettings()
    {
        var theme = ThemeHelper.ToElementTheme(_settings.Theme);
        if (RootGrid.RequestedTheme != theme) RootGrid.RequestedTheme = theme;
        MenuThemeSystem.IsChecked = _settings.Theme == AppTheme.System;
        MenuThemeLight.IsChecked = _settings.Theme == AppTheme.Light;
        MenuThemeDark.IsChecked = _settings.Theme == AppTheme.Dark;
        WindowHelper.ApplyTitleBarColors(AppWindow, RootGrid.ActualTheme == ElementTheme.Dark);

        RibbonArea.Visibility = _settings.ShowRibbon ? Visibility.Visible : Visibility.Collapsed;
        StatusBar.Visibility = _settings.ShowStatusBar ? Visibility.Visible : Visibility.Collapsed;
        MenuShowRibbon.IsChecked = _settings.ShowRibbon;
        MenuShowStatus.IsChecked = _settings.ShowStatusBar;
        MenuFlat.IsChecked = _settings.FlatView;

        bool tree = _settings.ShowFolderTree;
        TreeHost.Visibility = tree ? Visibility.Visible : Visibility.Collapsed;
        TreeSplitter.Visibility = tree ? Visibility.Visible : Visibility.Collapsed;
        TreeColumn.Width = tree ? new GridLength(Math.Clamp(_settings.TreeWidth, 150, 560)) : new GridLength(0);
        TreeSplitColumn.Width = tree ? new GridLength(8) : new GridLength(0);
        MenuShowTree.IsChecked = tree;
        if (tree && _folder is not null) SelectTreeNode(_folder);

        bool preview = _settings.ShowPreviewPane;
        PreviewHost.Visibility = preview ? Visibility.Visible : Visibility.Collapsed;
        PreviewSplitter.Visibility = preview ? Visibility.Visible : Visibility.Collapsed;
        PreviewColumn.Width = preview ? new GridLength(Math.Clamp(_settings.PreviewWidth, 220, 900)) : new GridLength(0);
        PreviewSplitColumn.Width = preview ? new GridLength(8) : new GridLength(0);
        MenuShowPreview.IsChecked = preview;
        if (preview) _ = UpdatePreviewAsync(force: false);
        else _previewCancel?.Cancel();
    }

    private void RootGrid_ActualThemeChanged(FrameworkElement sender, object args) =>
        WindowHelper.ApplyTitleBarColors(AppWindow, RootGrid.ActualTheme == ElementTheme.Dark);
}
