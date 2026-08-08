using System.Threading;
using System.Diagnostics;
using System.Windows.Markup;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using TiHiY.StreamControlCenter.Services;

namespace TiHiY.StreamControlCenter;

public partial class App : Application
{
    private static Mutex? _singleInstanceMutex;
    private static bool _ownsMutex;
    private IDisposable? _mainWindowVisualTuner;
    public static AppServices Services { get; private set; } = null!;

    protected override async void OnStartup(StartupEventArgs e)
    {
        var screenshotArg = e.Args.FirstOrDefault(x => x.StartsWith("--ci-screenshot=", StringComparison.OrdinalIgnoreCase));
        var screenshotPath = screenshotArg is null ? null : screenshotArg[(screenshotArg.IndexOf('=') + 1)..].Trim('"');
        var shortcutArg = e.Args.FirstOrDefault(x => x.StartsWith("--ci-shortcut=", StringComparison.OrdinalIgnoreCase));
        var shortcutPath = shortcutArg is null ? null : shortcutArg[(shortcutArg.IndexOf('=') + 1)..].Trim('"');
        var languageArg = e.Args.FirstOrDefault(x => x.StartsWith("--ci-language=", StringComparison.OrdinalIgnoreCase));
        var requestedLanguage = languageArg is null ? null : languageArg[(languageArg.IndexOf('=') + 1)..].Trim('"');
        var ciMode = !string.IsNullOrWhiteSpace(screenshotPath);
        var openSettingsInCi = e.Args.Any(x => string.Equals(x, "--ci-open-settings", StringComparison.OrdinalIgnoreCase));
        var showThemePreviewInCi = e.Args.Any(x => string.Equals(x, "--ci-settings-theme-preview", StringComparison.OrdinalIgnoreCase));

        _singleInstanceMutex = new Mutex(true, "Local\\TiHiY.StreamControlMini.SingleInstance", out _ownsMutex);
        if (!_ownsMutex)
        {
            if (!ciMode)
                MessageBox.Show("TiHiY StreamControl MINI вже запущено.", "TiHiY StreamControl MINI", MessageBoxButton.OK, MessageBoxImage.Information);
            Shutdown(0);
            return;
        }

        base.OnStartup(e);
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnDomainUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
        try
        {
            WriteStartupStage("01 MINI services construction");
            Services = new AppServices();
            Services.Theme.Apply("Україна", save: false);
            Services.Settings.Value.UiTheme = "Україна";
            if (!string.IsNullOrWhiteSpace(requestedLanguage))
                Services.Language.Apply(requestedLanguage, save: false);
            if (ciMode)
            {
                Services.Settings.Value.AutoConnectObs = false;
                Services.Settings.Value.TwitchAutoConnect = false;
                Services.Settings.Value.YouTubeAutoConnect = false;
                Services.Settings.Value.NotificationBotAutoStart = false;
                Services.Settings.Value.DonatelloAutoStart = false;
                Services.Settings.Value.LocalChatOverlayAutoStart = false;
            }
            WriteStartupStage("02 MINI services initialized in memory");
            await Services.InitializeAsync();
            WriteStartupStage("03 MINI background services initialized");
            var main = new MainWindow();
            WriteStartupStage("04 MINI MainWindow constructed");
            MainWindow = main;
            main.Show();
            UiTextLocalizer.Apply(main, Services.Language.CurrentLanguage);
            ButtonIconService.Apply(main);
            _mainWindowVisualTuner = MainWindowVisualTuner.Attach(main);
            if (!ciMode)
                ShortcutService.EnsureDesktopShortcut(Services.Logger);
            WriteStartupStage("05 MINI MainWindow shown");

            if (ciMode)
            {
                main.Width = 1320;
                main.Height = 820;
                main.WindowState = WindowState.Normal;
                main.Left = 0;
                main.Top = 0;
                Services.Theme.Apply("Україна", save: false);

                Window captureWindow = main;
                TiHiY.StreamControlCenter.Windows.SettingsWindow? settingsWindow = null;
                if (openSettingsInCi)
                {
                    settingsWindow = new TiHiY.StreamControlCenter.Windows.SettingsWindow
                    {
                        Owner = main,
                        WindowState = WindowState.Normal,
                        Left = 0,
                        Top = 0
                    };
                    settingsWindow.Show();
                    captureWindow = settingsWindow;
                    WriteStartupStage("06 MINI SettingsWindow shown in CI");
                }

                await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
                await Task.Delay(850);

                if (settingsWindow is not null && showThemePreviewInCi && FindVisualDescendant<TabControl>(settingsWindow) is { } tabs)
                    tabs.SelectedIndex = 1;

                UiTextLocalizer.Apply(captureWindow, Services.Language.CurrentLanguage);
                ButtonIconService.Apply(captureWindow);
                if (settingsWindow is not null)
                    _ = SettingsWindowVisualTuner.Attach(settingsWindow);

                if (!string.IsNullOrWhiteSpace(shortcutPath) && !ShortcutService.EnsureShortcut(shortcutPath, Services.Logger))
                    throw new InvalidOperationException($"CI shortcut was not created: {shortcutPath}");

                await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
                await Task.Delay(220);

                main.ApplyCiDemoState();
                MainWindowVisualTuner.ApplyNow(main);
                await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
                await Task.Delay(220);
                await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Render);

                SaveWindowScreenshot(captureWindow, screenshotPath!);
                if (!ReferenceEquals(captureWindow, main)) captureWindow.Close();
                Shutdown(0);
            }
        }
        catch (Exception ex)
        {
            try { Services?.Logger.Error("Запуск MINI", ex); } catch { }
            var crashFile = WriteStartupCrashFile(ex);
            if (!ciMode)
                MessageBox.Show(
                    BuildStartupErrorMessage(ex, crashFile),
                    "TiHiY StreamControl MINI",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            else if (!string.IsNullOrWhiteSpace(screenshotPath))
                try
                {
                    var errorPath = Path.ChangeExtension(screenshotPath, ".error.txt");
                    if (!string.IsNullOrWhiteSpace(errorPath))
                        File.WriteAllText(errorPath, ex.ToString());
                }
                catch { }
            Shutdown(1);
        }
    }

    private static T? FindVisualDescendant<T>(DependencyObject root) where T : DependencyObject
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);
            if (child is T match) return match;
            if (FindVisualDescendant<T>(child) is { } descendant) return descendant;
        }
        return null;
    }

    private static string BuildStartupErrorMessage(Exception ex, string crashFile)
    {
        var root = ex.GetBaseException();
        var location = ex is XamlParseException xaml
            ? $"\nXAML line: {xaml.LineNumber}, position: {xaml.LinePosition}"
            : string.Empty;
        return $"Не вдалося запустити TiHiY StreamControl MINI.\n\n{ex.GetType().Name}: {ex.Message}{location}\n\nRoot cause: {root.GetType().Name}: {root.Message}\n\nЖурнал помилки:\n{crashFile}";
    }

    private static void WriteStartupStage(string stage)
    {
        try
        {
            var folder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "TiHiY", "StreamControlMini", "Logs");
            Directory.CreateDirectory(folder);
            File.AppendAllText(Path.Combine(folder, "startup-stage-latest.txt"),
                $"{DateTime.Now:O} {stage}{Environment.NewLine}");
        }
        catch { }
    }

    private static string WriteStartupCrashFile(Exception ex)
    {
        try
        {
            var folder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "TiHiY", "StreamControlMini", "Logs");
            Directory.CreateDirectory(folder);
            var file = Path.Combine(folder, "startup-crash-latest.txt");
            File.WriteAllText(file, $"{DateTime.Now:O}{Environment.NewLine}{ex}");
            return file;
        }
        catch
        {
            try
            {
                var file = Path.Combine(Path.GetTempPath(), "TiHiY-StreamControlMini-startup-crash.txt");
                File.WriteAllText(file, ex.ToString());
                return file;
            }
            catch { return "журнал створити не вдалося"; }
        }
    }

    private static void SaveWindowScreenshot(Window window, string path)
    {
        window.UpdateLayout();
        var width = Math.Max(1, (int)Math.Ceiling(window.ActualWidth));
        var height = Math.Max(1, (int)Math.Ceiling(window.ActualHeight));
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(window);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        using var stream = File.Create(path);
        encoder.Save(stream);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        try
        {
            _mainWindowVisualTuner?.Dispose();
            _mainWindowVisualTuner = null;
            if (Services is not null)
            {
                var cleanupTask = Task.Run(async () => await Services.DisposeAsync().ConfigureAwait(false));
                if (!cleanupTask.Wait(TimeSpan.FromSeconds(5)))
                    Services.Logger.Info("Завершення MINI: фонове очищення перевищило 5 секунд.");
            }
        }
        catch (Exception ex)
        {
            try { Services?.Logger.Error("Завершення MINI", ex); } catch { }
        }
        finally
        {
            try { if (_ownsMutex) _singleInstanceMutex?.ReleaseMutex(); } catch { }
            _singleInstanceMutex?.Dispose();
        }
        base.OnExit(e);
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        Services?.Logger.Error("Необроблена помилка MINI", e.Exception);
        e.Handled = true;
        if (!Environment.GetCommandLineArgs().Any(x => x.StartsWith("--ci-screenshot=", StringComparison.OrdinalIgnoreCase)))
            MessageBox.Show($"Модуль повідомив про помилку, але MINI продовжує роботу.\n\n{e.Exception.Message}\n\nПодробиці записані в журнал.", "TiHiY StreamControl MINI", MessageBoxButton.OK, MessageBoxImage.Error);
    }

    private void OnDomainUnhandledException(object? sender, UnhandledExceptionEventArgs e) =>
        Services?.Logger.Error("Критична помилка MINI", e.ExceptionObject as Exception);

    private void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        Services?.Logger.Error("Помилка фонової операції MINI", e.Exception);
        e.SetObserved();
    }
}
