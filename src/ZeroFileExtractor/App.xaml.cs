using Microsoft.UI.Xaml;
using Microsoft.Windows.AppLifecycle;
using Windows.ApplicationModel.Activation;
using ZeroFileExtractor.Services;
using ZeroFileExtractor.Views;

namespace ZeroFileExtractor;

public partial class App : Application
{
    public App()
    {
        InitializeComponent();

        // Beklenmeyen hatalar uygulamayı kapatmasın; günlüğe yazılsın.
        UnhandledException += (_, e) =>
        {
            CrashLog.Write("UI", e.Exception);
            e.Handled = true;
        };
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            CrashLog.Write("Task", e.Exception);
            e.SetObserved();
        };
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            CrashLog.Write("AppDomain", e.ExceptionObject as Exception);
    }

    protected override void OnLaunched(Microsoft.UI.Xaml.LaunchActivatedEventArgs args)
    {
        // ANSI/OEM kod sayfaları (eski ZIP'lerdeki Türkçe adlar) için.
        System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);
        TempFiles.CleanStale();
        Loc.Initialize();

        var request = LaunchRequest.Parse(LaunchArguments());
        if (request.Action == LaunchAction.Browse)
        {
            new MainWindow(request.Paths.FirstOrDefault()).Activate();
        }
        else
        {
            JobWindow.FromLaunch(request);
        }
    }

    /// <summary>Çift tıklanan dosyalar (dosya ilişkilendirmesi) ya da komut satırı bağımsız değişkenleri.</summary>
    private static List<string> LaunchArguments()
    {
        try
        {
            var activation = AppInstance.GetCurrent().GetActivatedEventArgs();
            if (activation.Kind == ExtendedActivationKind.File && activation.Data is IFileActivatedEventArgs files)
            {
                var paths = files.Files.Select(f => f.Path).Where(p => !string.IsNullOrEmpty(p)).ToList();
                if (paths.Count > 0) return paths;
            }
        }
        catch (Exception ex)
        {
            CrashLog.Write("Etkinleştirme", ex);
        }
        return Environment.GetCommandLineArgs().Skip(1).ToList();
    }
}
