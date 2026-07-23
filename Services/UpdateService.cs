using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;

namespace TiHiY.StreamControlCenter.Services;

public static class UpdateService
{
    public const string ManifestUrl = "https://tihiy-software.pages.dev/updates/streamcontrol-center.json";

    private static readonly HttpClient Http = CreateHttpClient();

    public static async Task CheckAndOfferUpdateAsync(
        Window owner,
        AppLogger logger,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var separator = ManifestUrl.Contains('?') ? '&' : '?';
            var manifestUri = $"{ManifestUrl}{separator}t={DateTimeOffset.UtcNow.ToUnixTimeSeconds()}";

            using var response = await Http.GetAsync(manifestUri, cancellationToken).ConfigureAwait(true);
            response.EnsureSuccessStatusCode();
            var manifestJson = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(true);

            var manifest = JsonSerializer.Deserialize<UpdateManifest>(manifestJson, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (manifest is null)
            {
                logger.Info("Оновлення: сайт повернув порожній маніфест.");
                return;
            }

            var versionText = FirstNotEmpty(manifest.Version, manifest.LatestVersion);
            var downloadUrl = FirstNotEmpty(manifest.DownloadUrl, manifest.Url, manifest.PackageUrl);
            var sha256 = FirstNotEmpty(manifest.Sha256, manifest.Checksum);
            var notes = FirstNotEmpty(manifest.ReleaseNotes, manifest.Notes, manifest.Changelog);

            if (!TryParseVersion(versionText, out var availableVersion))
            {
                logger.Info($"Оновлення: некоректна версія в маніфесті: '{versionText}'.");
                return;
            }

            var currentVersion = GetCurrentVersion();
            if (availableVersion.CompareTo(currentVersion) <= 0)
            {
                logger.Info($"Оновлення: встановлена актуальна версія {currentVersion}.");
                return;
            }

            if (!Uri.TryCreate(downloadUrl, UriKind.Absolute, out var packageUri) ||
                packageUri.Scheme != Uri.UriSchemeHttps)
            {
                logger.Info("Оновлення: у маніфесті відсутнє коректне HTTPS-посилання на пакет.");
                return;
            }

            var message = $"Доступна нова версія TiHiY StreamControl Center {availableVersion}.\n\n" +
                          $"Встановлена версія: {currentVersion}.";
            if (!string.IsNullOrWhiteSpace(notes))
                message += $"\n\nЩо нового:\n{notes.Trim()}";
            message += "\n\nЗавантажити й установити оновлення зараз? Налаштування, OAuth і локальні дані буде збережено.";

            if (MessageBox.Show(owner, message, "Оновлення TiHiY StreamControl Center",
                    MessageBoxButton.YesNo, MessageBoxImage.Information) != MessageBoxResult.Yes)
                return;

            owner.IsEnabled = false;
            try
            {
                await DownloadVerifyAndLaunchUpdaterAsync(
                    packageUri,
                    sha256,
                    availableVersion,
                    logger,
                    cancellationToken).ConfigureAwait(true);
            }
            finally
            {
                owner.IsEnabled = true;
            }
        }
        catch (OperationCanceledException)
        {
            logger.Info("Оновлення: перевірку скасовано.");
        }
        catch (Exception ex)
        {
            logger.Error("Оновлення: перевірка або запуск інсталяції не вдалися", ex);
        }
    }

    private static async Task DownloadVerifyAndLaunchUpdaterAsync(
        Uri packageUri,
        string? expectedSha256,
        Version availableVersion,
        AppLogger logger,
        CancellationToken cancellationToken)
    {
        var updateRoot = Path.Combine(
            Path.GetTempPath(),
            "TiHiY",
            "StreamControlCenter",
            "Updates",
            availableVersion.ToString());

        if (Directory.Exists(updateRoot))
            Directory.Delete(updateRoot, recursive: true);
        Directory.CreateDirectory(updateRoot);

        var packagePath = Path.Combine(updateRoot, "TiHiY-StreamControl-Center-update.zip");
        var extractPath = Path.Combine(updateRoot, "package");
        Directory.CreateDirectory(extractPath);

        using (var response = await Http.GetAsync(
                   packageUri,
                   HttpCompletionOption.ResponseHeadersRead,
                   cancellationToken).ConfigureAwait(false))
        {
            response.EnsureSuccessStatusCode();
            await using var input = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            await using var output = File.Create(packagePath);
            await input.CopyToAsync(output, cancellationToken).ConfigureAwait(false);
        }

        if (!string.IsNullOrWhiteSpace(expectedSha256))
        {
            var actualSha256 = await ComputeSha256Async(packagePath, cancellationToken).ConfigureAwait(false);
            var normalizedExpected = NormalizeSha256(expectedSha256);
            if (!actualSha256.Equals(normalizedExpected, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException(
                    $"SHA-256 пакета не збігається. Очікується {normalizedExpected}, отримано {actualSha256}.");
            }
        }

        ZipFile.ExtractToDirectory(packagePath, extractPath, overwriteFiles: true);

        var exeName = Path.GetFileName(Environment.ProcessPath);
        if (string.IsNullOrWhiteSpace(exeName))
            exeName = "TiHiY.StreamControlCenter.exe";

        var sourcePath = ResolvePackageRoot(extractPath, exeName);
        var sourceExe = Path.Combine(sourcePath, exeName);
        if (!File.Exists(sourceExe))
            throw new InvalidDataException($"У пакеті оновлення не знайдено {exeName}.");

        var targetPath = AppContext.BaseDirectory.TrimEnd(
            Path.DirectorySeparatorChar,
            Path.AltDirectorySeparatorChar);
        var scriptPath = Path.Combine(updateRoot, "apply-update.ps1");
        await File.WriteAllTextAsync(scriptPath, BuildUpdaterScript(), cancellationToken).ConfigureAwait(false);

        var startInfo = new ProcessStartInfo
        {
            FileName = "powershell.exe",
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = updateRoot
        };
        startInfo.ArgumentList.Add("-NoProfile");
        startInfo.ArgumentList.Add("-ExecutionPolicy");
        startInfo.ArgumentList.Add("Bypass");
        startInfo.ArgumentList.Add("-File");
        startInfo.ArgumentList.Add(scriptPath);
        startInfo.ArgumentList.Add("-ProcessId");
        startInfo.ArgumentList.Add(Environment.ProcessId.ToString());
        startInfo.ArgumentList.Add("-Source");
        startInfo.ArgumentList.Add(sourcePath);
        startInfo.ArgumentList.Add("-Target");
        startInfo.ArgumentList.Add(targetPath);
        startInfo.ArgumentList.Add("-ExeName");
        startInfo.ArgumentList.Add(exeName);

        if (Process.Start(startInfo) is null)
            throw new InvalidOperationException("Не вдалося запустити модуль встановлення оновлення.");

        logger.Info($"Оновлення {availableVersion}: пакет перевірено, програму буде перезапущено.");
        Application.Current.Shutdown(0);
    }

    private static string ResolvePackageRoot(string extractPath, string exeName)
    {
        if (File.Exists(Path.Combine(extractPath, exeName)))
            return extractPath;

        var directories = Directory.GetDirectories(extractPath);
        var files = Directory.GetFiles(extractPath);
        if (directories.Length == 1 &&
            files.Length == 0 &&
            File.Exists(Path.Combine(directories[0], exeName)))
        {
            return directories[0];
        }

        var foundExe = Directory
            .EnumerateFiles(extractPath, exeName, SearchOption.AllDirectories)
            .FirstOrDefault();
        return foundExe is null ? extractPath : Path.GetDirectoryName(foundExe)!;
    }

    private static async Task<string> ComputeSha256Async(
        string filePath,
        CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(filePath);
        using var sha = SHA256.Create();
        var hash = await sha.ComputeHashAsync(stream, cancellationToken).ConfigureAwait(false);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static string NormalizeSha256(string value)
    {
        var firstToken = value
            .Trim()
            .Split(new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault();

        return (firstToken ?? string.Empty)
            .Replace("sha256:", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Trim()
            .ToLowerInvariant();
    }

    private static Version GetCurrentVersion()
    {
        var assembly = Assembly.GetEntryAssembly() ?? Assembly.GetExecutingAssembly();
        var informational = assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion;

        if (TryParseVersion(informational, out var parsed))
            return parsed;

        return assembly.GetName().Version ?? new Version(0, 0, 0, 0);
    }

    private static bool TryParseVersion(string? value, out Version version)
    {
        version = new Version(0, 0, 0, 0);
        if (string.IsNullOrWhiteSpace(value))
            return false;

        var normalized = value.Trim().TrimStart('v', 'V');
        var separatorIndex = normalized.IndexOfAny(new[] { '-', '+' });
        if (separatorIndex >= 0)
            normalized = normalized[..separatorIndex];

        if (!Version.TryParse(normalized, out var parsed) || parsed is null)
            return false;

        version = parsed;
        return true;
    }

    private static string? FirstNotEmpty(params string?[] values) =>
        values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));

    private static HttpClient CreateHttpClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("TiHiY-StreamControl-Center/1.0.5.3");
        client.DefaultRequestHeaders.CacheControl =
            new System.Net.Http.Headers.CacheControlHeaderValue { NoCache = true };
        return client;
    }

    private static string BuildUpdaterScript() =>
        """
        param(
            [Parameter(Mandatory=$true)][int]$ProcessId,
            [Parameter(Mandatory=$true)][string]$Source,
            [Parameter(Mandatory=$true)][string]$Target,
            [Parameter(Mandatory=$true)][string]$ExeName
        )
        $ErrorActionPreference = 'Stop'
        try { Wait-Process -Id $ProcessId -Timeout 120 -ErrorAction SilentlyContinue } catch {}
        Start-Sleep -Milliseconds 800
        New-Item -ItemType Directory -Force -Path $Target | Out-Null
        Get-ChildItem -LiteralPath $Source -Force | ForEach-Object {
            $destination = Join-Path $Target $_.Name
            Copy-Item -LiteralPath $_.FullName -Destination $destination -Recurse -Force
        }
        $exe = Join-Path $Target $ExeName
        if (-not (Test-Path -LiteralPath $exe)) { throw "Updated executable not found: $exe" }
        Start-Process -FilePath $exe -WorkingDirectory $Target
        """;

    private sealed class UpdateManifest
    {
        public string? Version { get; set; }
        public string? LatestVersion { get; set; }
        public string? DownloadUrl { get; set; }
        public string? Url { get; set; }
        public string? PackageUrl { get; set; }
        public string? Sha256 { get; set; }
        public string? Checksum { get; set; }
        public string? ReleaseNotes { get; set; }
        public string? Notes { get; set; }
        public string? Changelog { get; set; }
    }
}
