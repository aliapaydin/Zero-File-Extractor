using System.Diagnostics;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using ZeroFileExtractor.Engine;
using ZeroFileExtractor.Services;

namespace ZeroFileExtractor.Views;

/// <summary>
/// Küçük iş penceresi: çıkarma, sıkıştırma ve test işlerini sırayla çalıştırır; ilerleme, hız,
/// kalan süre, duraklat/iptal gösterir. Sağ tık menüsünden gelen komutlar da burada çalışır.
/// </summary>
public sealed partial class JobWindow : Window
{
    private const int ProgressWidth = 580, ProgressHeight = 300, DialogHeight = 560;

    private readonly IntPtr _hwnd;
    private readonly Queue<Job> _queue = new();
    private readonly List<Job> _succeeded = new();
    private readonly List<string> _errors = new();
    private readonly List<string> _warnings = new();
    private readonly DispatcherQueueTimer _timer;
    private readonly TaskbarProgress _taskbar;
    private readonly Stopwatch _watch = new();
    private readonly Queue<(double Seconds, long Bytes)> _samples = new();
    private Func<List<Job>?>? _buildFromSetup;
    private CancellationTokenSource? _cancel;
    private JobContext? _context;
    private Job? _current;
    private int _total, _index;
    private bool _running, _closeRequested, _cancelled;
    private double _smoothedSpeed;

    /// <summary>Bir iş başarıyla bitince (UI iş parçacığında).</summary>
    public event EventHandler<Job>? JobSucceeded;

    public JobWindow()
    {
        InitializeComponent();
        Loc.Apply(this);
        WindowHelper.Track(this);
        _hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);

        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
        AppWindow.SetIcon(WindowHelper.IconPath);
        if (AppWindow.Presenter is Microsoft.UI.Windowing.OverlappedPresenter presenter) presenter.IsMaximizable = false;
        RootGrid.RequestedTheme = ThemeHelper.ToElementTheme(AppSettings.Current.Theme);
        WindowHelper.ApplyTitleBarColors(AppWindow, RootGrid.ActualTheme == ElementTheme.Dark);
        WindowHelper.Resize(AppWindow, _hwnd, ProgressWidth, ProgressHeight, center: true);

        _taskbar = new TaskbarProgress(_hwnd);
        _timer = DispatcherQueue.CreateTimer();
        _timer.Interval = TimeSpan.FromMilliseconds(120);
        _timer.Tick += (_, _) => Tick();
        CloseWhenDone.IsChecked = AppSettings.Current.CloseWhenDone;

        AppWindow.Closing += (_, e) =>
        {
            if (!_running) return;
            // İş sürerken kapatma: önce iptal et, iş parçacığı durunca kapan.
            e.Cancel = true;
            _closeRequested = true;
            CancelRunning();
        };
    }

    // ------------------------------------------------------------------ başlatma yolları

    /// <summary>Verilen işleri hemen çalıştırır.</summary>
    public static JobWindow Run(IEnumerable<Job> jobs)
    {
        var window = new JobWindow();
        window.Activate();
        window.Enqueue(jobs);
        return window;
    }

    /// <summary>Sağ tık menüsü / komut satırı isteğini çalıştırır.</summary>
    public static JobWindow FromLaunch(LaunchRequest request)
    {
        var window = new JobWindow();
        window.Activate();
        var settings = AppSettings.Current;
        var archives = request.Paths.Where(p => File.Exists(p) && ArchiveFormats.IsArchivePath(p)).ToList();

        switch (request.Action)
        {
            case LaunchAction.ExtractHere:
            case LaunchAction.ExtractToFolder:
                if (archives.Count == 0)
                {
                    window.ShowMessage(Loc.T("Seçilenler arasında desteklenen bir arşiv yok."));
                    break;
                }
                window.Enqueue(archives.Select(a => (Job)new ExtractJob
                {
                    ArchivePath = a,
                    Destination = Path.GetDirectoryName(a)!,
                    FolderMode = request.Action == LaunchAction.ExtractHere ? ExtractFolderMode.Direct : ExtractFolderMode.NamedFolder,
                    Options = new ExtractOptions { Conflict = settings.ExtractConflict },
                }));
                break;

            case LaunchAction.Test:
                if (archives.Count == 0)
                {
                    window.ShowMessage(Loc.T("Seçilenler arasında desteklenen bir arşiv yok."));
                    break;
                }
                window.Enqueue(archives.Select(a => (Job)new TestJob { ArchivePath = a }));
                break;

            case LaunchAction.ExtractAsk:
                if (archives.Count == 0)
                {
                    window.ShowMessage(Loc.T("Seçilenler arasında desteklenen bir arşiv yok."));
                    break;
                }
                window.ShowExtractSetup(archives);
                break;

            case LaunchAction.CompressZip:
            case LaunchAction.CompressSevenZip:
            {
                var format = request.Action == LaunchAction.CompressZip ? CreateFormat.Zip : CreateFormat.SevenZip;
                var (folder, name) = ArchiveWriter.SuggestName(request.Paths);
                int level = Math.Clamp(settings.DefaultLevel, ArchiveFormats.SupportsStore(format) ? 0 : 1, 5);
                window.Enqueue(new Job[] { new CompressJob
                {
                    Sources = request.Paths,
                    Options = new CreateOptions
                    {
                        Format = format,
                        Level = level,
                        OutputPath = ArchiveSession.UniquePath(Path.Combine(folder, name + ArchiveFormats.Extension(format))),
                    },
                } });
                break;
            }

            default:
                window.ShowCompressSetup(request.Paths);
                break;
        }
        return window;
    }

    private void ShowExtractSetup(List<string> archives)
    {
        var first = archives[0];
        var panel = new ExtractOptionsPanel(_hwnd, Path.GetDirectoryName(first)!, wholeArchive: true,
            archives.Count == 1 ? ArchiveFormats.StripExtension(Path.GetFileName(first)) : null,
            archives.Count == 1 ? null : Loc.F("{0} arşiv çıkarılacak", archives.Count));
        ShowSetup(archives.Count == 1 ? Loc.F("Çıkar: {0}", Path.GetFileName(first)) : Loc.T("Arşivleri çıkar"), panel, Loc.T("Çıkar"), () =>
        {
            if (!panel.Validate()) return null;
            return archives.Select(a => (Job)new ExtractJob
            {
                ArchivePath = a,
                Destination = panel.Destination,
                FolderMode = panel.FolderMode,
                Options = new ExtractOptions { Conflict = panel.Conflict, Flatten = panel.Flatten },
                DeleteArchive = panel.DeleteArchive,
                OpenFolderWhenDone = panel.OpenFolder && a == archives[^1],
            }).ToList();
        });
    }

    private void ShowCompressSetup(IReadOnlyList<string> sources)
    {
        var panel = new CompressOptionsPanel(sources, _hwnd);
        ShowSetup(Loc.T("Yeni arşiv"), panel, Loc.T("Sıkıştır"), () => panel.BuildJob() is { } job ? new List<Job> { job } : null);
        _ = panel.ScanAsync();
    }

    private void ShowSetup(string title, UIElement panel, string startText, Func<List<Job>?> build)
    {
        Title = title + " — Zero File Extractor";
        TitleText.Text = Title;
        SetupTitle.Text = title;
        SetupHost.Content = panel;
        StartButton.Content = startText;
        _buildFromSetup = build;
        Show(SetupPage);
        WindowHelper.Resize(AppWindow, _hwnd, ProgressWidth, 700, center: true);
    }

    private void Start_Click(object sender, RoutedEventArgs e)
    {
        var jobs = _buildFromSetup?.Invoke();
        if (jobs is null || jobs.Count == 0) return;
        _buildFromSetup = null;
        SetupHost.Content = null;
        WindowHelper.Resize(AppWindow, _hwnd, ProgressWidth, ProgressHeight, center: false);
        Enqueue(jobs);
    }

    private void ShowMessage(string message)
    {
        DoneIcon.Glyph = "";
        DoneTitle.Text = "Zero File Extractor";
        DoneText.Text = message;
        Show(DonePage);
    }

    // ------------------------------------------------------------------ kuyruk

    public void Enqueue(IEnumerable<Job> jobs)
    {
        foreach (var job in jobs)
        {
            _queue.Enqueue(job);
            _total++;
        }
        if (!_running) _ = RunQueueAsync();
    }

    private async Task RunQueueAsync()
    {
        _running = true;
        Show(ProgressPage);
        _timer.Start();

        while (_queue.Count > 0 && !_cancelled)
        {
            var job = _queue.Dequeue();
            _index++;
            _current = job;
            _cancel = new CancellationTokenSource();
            var context = new JobContext
            {
                Cancellation = _cancel.Token,
                RequestPassword = wrong => OnUi(() => Dialogs.PasswordAsync(Content.XamlRoot, RootGrid.ActualTheme, JobFileName(job), wrong)),
                ResolveConflict = info => OnUi(() => Dialogs.ConflictAsync(Content.XamlRoot, RootGrid.ActualTheme, info)),
            };
            _context = context;
            _samples.Clear();
            _smoothedSpeed = 0;
            _watch.Restart();

            Title = job.Title + " — Zero File Extractor";
            TitleText.Text = Title;
            JobTitle.Text = job.Title;
            QueueText.Text = _total > 1 ? Loc.F("İş {0} / {1}", _index, _total) : string.Empty;
            PauseButton.Content = Loc.T("Duraklat");
            PauseButton.IsEnabled = job is not CompressJob { Options.Format: CreateFormat.Rar };
            Tick();

            try
            {
                await Task.Run(() => job.Run(context));
                _succeeded.Add(job);
                foreach (var warning in context.Warnings.Where(w => w.Length > 0).Take(50))
                    _warnings.Add(warning);
                JobSucceeded?.Invoke(this, job);
            }
            catch (OperationCanceledException)
            {
                _cancelled = true;
            }
            catch (Exception ex)
            {
                CrashLog.Write(job.Title, ex);
                _errors.Add(JobFileName(job) + ": " + Friendly(ex));
            }
            finally
            {
                _watch.Stop();
                _cancel.Dispose();
                _cancel = null;
                _context = null;
            }
        }

        _timer.Stop();
        _running = false;
        _current = null;
        _taskbar.Set(null);
        Finish();
    }

    private static string JobFileName(Job job) => job switch
    {
        ExtractJob extract => Path.GetFileName(extract.ArchivePath),
        TestJob test => Path.GetFileName(test.ArchivePath),
        CompressJob compress => Path.GetFileName(compress.Options.OutputPath),
        ZipAddJob add => Path.GetFileName(add.ZipPath),
        ZipDeleteJob delete => Path.GetFileName(delete.ZipPath),
        _ => job.Title,
    };

    private static string Friendly(Exception ex) => ex switch
    {
        PasswordRequiredException => Loc.T("Parola gerekli."),
        UnauthorizedAccessException => Loc.T("Erişim reddedildi. Hedef klasöre yazma iznin olmayabilir.") + " " + ex.Message,
        FileNotFoundException when ex.Message.Contains("Rar.exe", StringComparison.Ordinal) => Loc.T("RAR oluşturmak için WinRAR kurulu olmalı."),
        NotSupportedException or SharpCompress.Common.SharpCompressException or InvalidDataException or InvalidOperationException
            => Loc.T("Bu dosya desteklenen bir arşiv değil ya da bozuk.") + " (" + ex.Message + ")",
        _ => ex.Message,
    };

    private void Finish()
    {
        if (_closeRequested || (_cancelled && _succeeded.Count == 0 && _errors.Count == 0))
        {
            Close();
            return;
        }

        var last = _succeeded.LastOrDefault();
        bool clean = _errors.Count == 0 && _warnings.Count == 0 && !_cancelled;

        foreach (var job in _succeeded.Where(j => j.OpenFolderWhenDone && j.ResultFolder is not null))
            ShellActions.ShowInExplorer(job.ResultFolder!);

        if (clean && CloseWhenDone.IsChecked == true)
        {
            Close();
            return;
        }

        var lines = new List<string>();
        foreach (var job in _succeeded)
            lines.Add(_succeeded.Count > 1 ? JobFileName(job) + ": " + job.Summary : job.Summary);
        if (last?.ResultFolder is { } folder) lines.Add(folder);
        if (_cancelled) lines.Add(Loc.T("Kalan işler iptal edildi."));
        if (_errors.Count > 0)
        {
            lines.Add(string.Empty);
            lines.Add(Loc.T("Hatalar:"));
            lines.AddRange(_errors);
        }
        if (_warnings.Count > 0)
        {
            lines.Add(string.Empty);
            lines.Add(Loc.T("Atlanan ya da sorunlu öğeler:"));
            lines.AddRange(_warnings.Take(50));
        }
        DoneText.Text = string.Join(Environment.NewLine, lines);

        if (_errors.Count > 0)
        {
            DoneIcon.Glyph = "";
            DoneIcon.Foreground = (Brush)Application.Current.Resources["SystemFillColorCriticalBrush"];
            DoneTitle.Text = _succeeded.Count > 0 ? Loc.T("Bazı işler tamamlanamadı") : Loc.T("İş tamamlanamadı");
        }
        else if (!clean)
        {
            DoneIcon.Glyph = "";
            DoneIcon.Foreground = (Brush)Application.Current.Resources["SystemFillColorCautionBrush"];
            DoneTitle.Text = Loc.T("Uyarılarla tamamlandı");
        }
        else
        {
            DoneIcon.Glyph = "";
            DoneIcon.Foreground = (Brush)Application.Current.Resources["SystemFillColorSuccessBrush"];
            DoneTitle.Text = Loc.T("Tamamlandı");
        }

        _resultFolder = last?.ResultFolder;
        _resultFile = last?.ResultFile;
        OpenFolderButton.Visibility = _resultFolder is null ? Visibility.Collapsed : Visibility.Visible;
        OpenArchiveButton.Visibility = _resultFile is null ? Visibility.Collapsed : Visibility.Visible;
        Title = DoneTitle.Text + " — Zero File Extractor";
        TitleText.Text = Title;
        Show(DonePage);
        if (lines.Count > 6) WindowHelper.Resize(AppWindow, _hwnd, ProgressWidth, 420, center: false);
        NativeWindow.Flash(_hwnd);
    }

    private string? _resultFolder, _resultFile;

    // ------------------------------------------------------------------ ilerleme

    private void Tick()
    {
        var context = _context;
        if (context is null) return;
        var progress = context.Snapshot();
        double seconds = _watch.Elapsed.TotalSeconds;

        CurrentItem.Text = progress.CurrentItem;
        bool known = progress.BytesTotal > 0;
        Bar.IsIndeterminate = !known;
        double fraction = known ? Math.Clamp((double)progress.BytesDone / progress.BytesTotal, 0, 1) : 0;
        Bar.Value = fraction * 1000;
        PercentText.Text = known ? fraction.ToString("P0") : string.Empty;
        _taskbar.Set(known ? fraction : double.NaN, context.IsPaused);

        ProgressText.Text = known
            ? Loc.F("{0} / {1}", Format.Size(progress.BytesDone), Format.Size(progress.BytesTotal))
            : progress.BytesDone > 0 ? Format.Size(progress.BytesDone) : string.Empty;
        if (progress.FilesTotal > 0)
            ProgressText.Text += "  ·  " + Loc.F("{0} / {1} dosya", progress.FilesDone.ToString("N0"), progress.FilesTotal.ToString("N0"));

        if (context.IsPaused)
        {
            SpeedText.Text = Loc.T("Duraklatıldı");
            RemainingText.Text = string.Empty;
            return;
        }

        // Son ~4 saniyenin ortalama hızı.
        _samples.Enqueue((seconds, progress.BytesDone));
        while (_samples.Count > 2 && seconds - _samples.Peek().Seconds > 4) _samples.Dequeue();
        var oldest = _samples.Peek();
        double window = seconds - oldest.Seconds;
        double speed = window > 0.4 ? (progress.BytesDone - oldest.Bytes) / window : 0;
        _smoothedSpeed = _smoothedSpeed <= 0 ? speed : _smoothedSpeed * 0.85 + speed * 0.15;

        var elapsed = Loc.F("Geçen: {0}", Format.Duration(_watch.Elapsed));
        SpeedText.Text = _smoothedSpeed > 0 ? elapsed + "  ·  " + Loc.F("{0}/sn", Format.Size((long)_smoothedSpeed)) : elapsed;

        if (known && _smoothedSpeed > 1 && seconds > 1.5)
        {
            var remaining = TimeSpan.FromSeconds((progress.BytesTotal - progress.BytesDone) / _smoothedSpeed);
            RemainingText.Text = Loc.F("Kalan: yaklaşık {0}", Format.Duration(remaining));
        }
        else if (_current?.Estimate is { } estimate && seconds <= 1.5)
        {
            RemainingText.Text = Loc.F("Tahmini süre: yaklaşık {0}", Format.Duration(estimate));
        }
        else if (!known)
        {
            RemainingText.Text = string.Empty;
        }
    }

    private void Pause_Click(object sender, RoutedEventArgs e)
    {
        var context = _context;
        if (context is null) return;
        if (context.IsPaused)
        {
            context.Resume();
            _watch.Start();
            PauseButton.Content = Loc.T("Duraklat");
        }
        else
        {
            context.Pause();
            _watch.Stop();
            _samples.Clear();
            PauseButton.Content = Loc.T("Sürdür");
        }
        Tick();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        _closeRequested = true;
        CancelRunning();
        if (!_running) Close();
    }

    private void CancelRunning()
    {
        _cancelled = true;
        _queue.Clear();
        try
        {
            _cancel?.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // iş o anda bitti
        }
        _context?.Resume(); // duraklatılmışsa iptali görebilsin
        CancelButton.IsEnabled = false;
        PauseButton.IsEnabled = false;
        CurrentItem.Text = Loc.T("İptal ediliyor…");
    }

    private void CloseWhenDone_Click(object sender, RoutedEventArgs e) =>
        AppSettings.Current.CloseWhenDone = CloseWhenDone.IsChecked == true;

    // ------------------------------------------------------------------ sonuç düğmeleri

    private void OpenFolder_Click(object sender, RoutedEventArgs e)
    {
        if (_resultFile is not null && File.Exists(_resultFile)) ShellActions.ShowInExplorer(_resultFile);
        else if (_resultFolder is not null) ShellActions.ShowInExplorer(_resultFolder);
        Close();
    }

    private void OpenArchive_Click(object sender, RoutedEventArgs e)
    {
        if (_resultFile is not null && File.Exists(_resultFile)) MainWindow.OpenOrActivate(_resultFile);
        Close();
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    // ------------------------------------------------------------------ yardımcılar

    private void Show(FrameworkElement page)
    {
        SetupPage.Visibility = page == SetupPage ? Visibility.Visible : Visibility.Collapsed;
        ProgressPage.Visibility = page == ProgressPage ? Visibility.Visible : Visibility.Collapsed;
        DonePage.Visibility = page == DonePage ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>İş parçacığından arayüzde bir soru sorar ve cevabı bekler. Diyalog sığsın diye pencere geçici olarak büyür.</summary>
    private T OnUi<T>(Func<Task<T>> ask)
    {
        var completion = new TaskCompletionSource<T>();
        bool queued = DispatcherQueue.TryEnqueue(async () =>
        {
            bool running = _watch.IsRunning;
            _watch.Stop();
            var size = AppWindow.Size;
            try
            {
                WindowHelper.Resize(AppWindow, _hwnd, ProgressWidth, DialogHeight, center: false);
                NativeWindow.BringToFront(_hwnd);
                completion.SetResult(await ask());
            }
            catch (Exception ex)
            {
                completion.SetException(ex);
            }
            finally
            {
                AppWindow.Resize(size);
                if (running) _watch.Start();
                _samples.Clear();
            }
        });
        if (!queued) throw new OperationCanceledException();
        return completion.Task.GetAwaiter().GetResult();
    }

    private void RootGrid_ActualThemeChanged(FrameworkElement sender, object args) =>
        WindowHelper.ApplyTitleBarColors(AppWindow, RootGrid.ActualTheme == ElementTheme.Dark);
}
