using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace TiHiY.StreamControlCenter;

public partial class LiteApp : Application
{
    private static Mutex? _mutex;
    private static bool _owns;
    public static LiteCoreService Core { get; private set; } = null!;
    public static LiteLiveClock LiveClock { get; private set; } = null!;

    [DllImport("user32.dll")]
    private static extern bool SetCursorPos(int x, int y);

    protected override async void OnStartup(StartupEventArgs e)
    {
        _mutex = new Mutex(true, "Local\\TiHiY.StreamControlMini.Lite", out _owns);
        if (!_owns) { Shutdown(); return; }
        base.OnStartup(e);

        var screenshotArg = e.Args.FirstOrDefault(x => x.StartsWith("--ci-screenshot=", StringComparison.OrdinalIgnoreCase));
        var screenshot = screenshotArg is null ? null : screenshotArg[(screenshotArg.IndexOf('=') + 1)..].Trim('"');
        var auditArg = e.Args.FirstOrDefault(x => x.StartsWith("--ci-ui-audit=", StringComparison.OrdinalIgnoreCase));
        var auditFolder = auditArg is null ? null : auditArg[(auditArg.IndexOf('=') + 1)..].Trim('"');
        var ciScreenshot = !string.IsNullOrWhiteSpace(screenshot);
        var ciUiAudit = !string.IsNullOrWhiteSpace(auditFolder);
        var ciModules = e.Args.Any(x => x.Equals("--ci-modules", StringComparison.OrdinalIgnoreCase));
        var ciOverlayBenchmark = e.Args.Any(x => x.Equals("--ci-overlay-benchmark", StringComparison.OrdinalIgnoreCase));
        var ci = ciScreenshot || ciModules || ciOverlayBenchmark || ciUiAudit;
        try
        {
            Core = new LiteCoreService();
            LiveClock = new LiteLiveClock(Core);
            var main = new MainWindow();
            MainWindow = main;
            main.Show();
            if (!ci) LiteShortcutService.EnsureDesktopShortcut(Core.Logger);
            await Core.InitializeAsync(ci && !ciOverlayBenchmark);

            if (ciOverlayBenchmark)
            {
                main.ShowOverlay();
                await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
                return;
            }

            if (ciUiAudit)
            {
                main.ApplyCiDemo();
                main.Width = 1280;
                main.Height = 780;
                main.Left = 0;
                main.Top = 0;
                await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
                Directory.CreateDirectory(auditFolder!);
                await CaptureMainHoverAsync(main, Path.Combine(auditFolder!, "main-hover-resume.png"));
                await ShowAndCaptureAsync(new ChatBotWindow(), Path.Combine(auditFolder!, "chat-bot.png"), 980, 720);
                await ShowAndCaptureAsync(new DiscordServersWindow(), Path.Combine(auditFolder!, "discord-servers.png"), 1050, 700);
                await ShowAndCaptureAsync(new SettingsWindow(), Path.Combine(auditFolder!, "settings.png"), 900, 700);
                await ShowAndCaptureAsync(new GameOverlaySettingsWindow(), Path.Combine(auditFolder!, "game-overlay-settings.png"), 780, 690);
                await ShowAndCaptureAsync(new HudWindow(Core), Path.Combine(auditFolder!, "game-overlay.png"), 620, 420);
                Shutdown(0);
                return;
            }

            if (ciModules)
            {
                await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
                SmokeRestoredModules(main);
                Shutdown(0);
                return;
            }

            if (ciScreenshot)
            {
                main.ApplyCiDemo();
                main.Width = 1280;
                main.Height = 780;
                main.Left = 0;
                main.Top = 0;
                await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
                await Task.Delay(350);
                SaveScreenshot(main, screenshot!);
                Shutdown(0);
            }
        }
        catch (Exception ex)
        {
            try { Core?.Logger.Error("LITE startup", ex); } catch { }
            if (!ci) MessageBox.Show(ex.GetBaseException().Message, "TiHiY StreamControl MINI Lite", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
        }
    }

    private static async Task CaptureMainHoverAsync(MainWindow main, string path)
    {
        main.Activate();
        main.UpdateLayout();
        await Current.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
        var resume = FindVisual<Button>(main, b => string.Equals(b.Content?.ToString(), "RESUME", StringComparison.Ordinal));
        if (resume is not null)
        {
            var p = resume.PointToScreen(new Point(Math.Max(1, resume.ActualWidth / 2), Math.Max(1, resume.ActualHeight / 2)));
            SetCursorPos((int)Math.Round(p.X), (int)Math.Round(p.Y));
            await Task.Delay(300);
            await Current.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
        }
        SaveScreenshot(main, path);
    }

    private static T? FindVisual<T>(DependencyObject parent, Func<T, bool> predicate) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T match && predicate(match)) return match;
            var nested = FindVisual(child, predicate);
            if (nested is not null) return nested;
        }
        return null;
    }

    private static async Task ShowAndCaptureAsync(Window window, string path, double width, double height)
    {
        window.Width = width;
        window.Height = height;
        window.WindowStartupLocation = WindowStartupLocation.Manual;
        window.Left = 20;
        window.Top = 20;
        window.ShowActivated = false;
        window.Show();
        await Current.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
        await Task.Delay(180);
        SaveScreenshot(window, path);
        window.Close();
        await Current.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
    }

    private static void SmokeRestoredModules(MainWindow main)
    {
        var settings = new SettingsWindow { Owner = main };
        settings.UpdateLayout();
        settings.Close();

        var bot = new ChatBotWindow { Owner = main };
        bot.UpdateLayout();
        bot.Close();

        var discord = new DiscordBotWindow { Owner = main };
        discord.UpdateLayout();
        discord.Close();

        var servers = new DiscordServersWindow { Owner = main };
        servers.UpdateLayout();
        servers.Close();

        var profile = new DiscordProfileWindow("ci-server", "CI Server") { Owner = main };
        profile.UpdateLayout();
        profile.Close();

        var overlaySettings = new GameOverlaySettingsWindow { Owner = main };
        overlaySettings.UpdateLayout();
        overlaySettings.Close();

        var overlay = new HudWindow(Core) { Owner = main };
        overlay.Show();
        overlay.UpdateLayout();
        overlay.Close();
    }

    private static void SaveScreenshot(Window window, string path)
    {
        window.UpdateLayout();
        var bmp = new RenderTargetBitmap(Math.Max(1, (int)window.ActualWidth), Math.Max(1, (int)window.ActualHeight), 96, 96, PixelFormats.Pbgra32);
        bmp.Render(window);
        var enc = new PngBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(bmp));
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        using var fs = File.Create(path);
        enc.Save(fs);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        try { LiveClock?.Dispose(); } catch { }
        try { if (Core is not null) Task.Run(async () => await Core.DisposeAsync()).Wait(TimeSpan.FromSeconds(4)); } catch { }
        try { if (_owns) _mutex?.ReleaseMutex(); } catch { }
        _mutex?.Dispose();
        base.OnExit(e);
    }
}
