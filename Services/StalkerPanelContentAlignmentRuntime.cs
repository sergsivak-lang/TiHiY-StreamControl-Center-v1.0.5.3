using System;
using System.Runtime.CompilerServices;
using System.Windows;

namespace TiHiY.StreamControlCenter.Services;

/// <summary>
/// The proportional row-height experiment made the Donations, Notifications and
/// Mixer layouts worse at real window sizes. Keep this bootstrap as an explicit
/// no-op so no stale runtime continues changing panel geometry.
/// </summary>
internal static class StalkerPanelContentAlignmentBootstrap
{
    [ModuleInitializer]
    internal static void Initialize()
    {
        // Intentionally disabled. The original responsive panel geometry is kept.
    }
}
