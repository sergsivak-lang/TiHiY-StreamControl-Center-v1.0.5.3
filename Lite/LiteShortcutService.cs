using System.Diagnostics;
using TiHiY.StreamControlCenter.Services;

namespace TiHiY.StreamControlCenter;

internal static class LiteShortcutService
{
    public static void EnsureDesktopShortcut(AppLogger logger)
    {
        try
        {
            var exe = Environment.ProcessPath;
            if (string.IsNullOrWhiteSpace(exe) || !File.Exists(exe)) return;
            var desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            var shortcut = Path.Combine(desktop, "TiHiY StreamControl MINI Lite.lnk");
            if (File.Exists(shortcut)) return;
            var shellType = Type.GetTypeFromProgID("WScript.Shell");
            if (shellType is null) return;
            dynamic shell = Activator.CreateInstance(shellType)!;
            dynamic link = shell.CreateShortcut(shortcut);
            link.TargetPath = exe;
            link.WorkingDirectory = Path.GetDirectoryName(exe);
            link.IconLocation = exe + ",0";
            link.Description = "TiHiY StreamControl MINI Lite — Multichat, Streamlabs, Discord, HUD";
            link.Save();
            logger.Info("LITE: ярлик створено на Робочому столі.");
        }
        catch (Exception ex) { logger.Error("LITE shortcut", ex); }
    }
}
