using System.Threading;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace TiHiY.StreamControlCenter;

public partial class LiteApp : Application
{
    private static Mutex? _mutex;
    private static bool _owns;
    public static LiteCoreService Core { get; private set; } = null!;

    protected override async void OnStartup(StartupEventArgs e)
    {
        _mutex = new Mutex(true, "Local\\TiHiY.StreamControlMini.Lite", out _owns);
        if (!_owns) { Shutdown(); return; }
        base.OnStartup(e);

        var arg = e.Args.FirstOrDefault(x => x.StartsWith("--ci-screenshot=", StringComparison.OrdinalIgnoreCase));
        var screenshot = arg is null ? null : arg[(arg.IndexOf('=') + 1)..].Trim('"');
        var ci = !string.IsNullOrWhiteSpace(screenshot);
        try
        {
            Core = new LiteCoreService();
            var main = new MainWindow();
            MainWindow = main;
            main.Show();
            if (!ci) LiteShortcutService.EnsureDesktopShortcut(Core.Logger);
            await Core.InitializeAsync(ci);
            if (ci)
            {
                main.ApplyCiDemo();
                main.Width = 1280; main.Height = 780; main.Left = 0; main.Top = 0;
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

    private static void SaveScreenshot(Window window, string path)
    {
        window.UpdateLayout();
        var bmp = new RenderTargetBitmap(Math.Max(1,(int)window.ActualWidth), Math.Max(1,(int)window.ActualHeight), 96, 96, PixelFormats.Pbgra32);
        bmp.Render(window);
        var enc = new PngBitmapEncoder(); enc.Frames.Add(BitmapFrame.Create(bmp));
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        using var fs = File.Create(path); enc.Save(fs);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        try { if (Core is not null) Task.Run(async () => await Core.DisposeAsync()).Wait(TimeSpan.FromSeconds(4)); } catch { }
        try { if (_owns) _mutex?.ReleaseMutex(); } catch { }
        _mutex?.Dispose();
        base.OnExit(e);
    }
}
