using TiHiY.StreamControlCenter.Services;

namespace TiHiY.StreamControlCenter;

public partial class App
{
    private bool _updateCheckStarted;

    public App()
    {
        Activated += OnFirstApplicationActivated;
    }

    private async void OnFirstApplicationActivated(object? sender, EventArgs e)
    {
        if (_updateCheckStarted) return;
        _updateCheckStarted = true;
        Activated -= OnFirstApplicationActivated;

        if (Environment.GetCommandLineArgs().Any(argument =>
                argument.StartsWith("--ci-", StringComparison.OrdinalIgnoreCase)))
            return;

        await Task.Delay(TimeSpan.FromSeconds(2.5));

        if (MainWindow is Window owner && Services is not null)
            await UpdateService.CheckAndOfferUpdateAsync(owner, Services.Logger);
    }
}
