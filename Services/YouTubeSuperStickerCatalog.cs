using System.Text;

namespace TiHiY.StreamControlCenter.Services;

/// <summary>
/// YouTube Data API intentionally omits Super Sticker image URLs, but Google's
/// live-chat documentation publishes an official sticker-id-to-URL CSV. Load it
/// lazily and cache it for the process lifetime.
/// </summary>
internal sealed class YouTubeSuperStickerCatalog
{
    private const string CatalogUrl = "https://youtube.googleapis.com/super_stickers/sticker_ids_to_urls.csv";
    private readonly HttpClient _http = new();
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Dictionary<string, string> _urls = new(StringComparer.Ordinal);
    private bool _loaded;

    public async Task<string> ResolveAsync(string stickerId, CancellationToken token)
    {
        if (string.IsNullOrWhiteSpace(stickerId)) return string.Empty;
        await EnsureLoadedAsync(token).ConfigureAwait(false);
        lock (_urls) return _urls.TryGetValue(stickerId.Trim(), out var url) ? url : string.Empty;
    }

    private async Task EnsureLoadedAsync(CancellationToken token)
    {
        if (_loaded) return;
        await _gate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            if (_loaded) return;
            try
            {
                using var response = await _http.GetAsync(CatalogUrl, token).ConfigureAwait(false);
                if (!response.IsSuccessStatusCode) return;
                var csv = await response.Content.ReadAsStringAsync(token).ConfigureAwait(false);
                ParseCsv(csv);
                _loaded = true;
            }
            catch (OperationCanceledException) { throw; }
            catch { }
        }
        finally { _gate.Release(); }
    }

    private void ParseCsv(string csv)
    {
        foreach (var rawLine in csv.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries))
        {
            var cells = ParseCsvLine(rawLine);
            if (cells.Count < 2) continue;
            var url = cells.FirstOrDefault(x => x.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || x.StartsWith("https://", StringComparison.OrdinalIgnoreCase));
            if (string.IsNullOrWhiteSpace(url)) continue;

            // Google's catalog has changed column labels over time, so avoid
            // hard-coding the column number. Sticker IDs are the first compact,
            // non-URL value on each data row.
            var id = cells.FirstOrDefault(x =>
                !string.IsNullOrWhiteSpace(x) &&
                !x.StartsWith("http", StringComparison.OrdinalIgnoreCase) &&
                !x.Contains("sticker", StringComparison.OrdinalIgnoreCase) &&
                !x.Contains("url", StringComparison.OrdinalIgnoreCase));
            if (string.IsNullOrWhiteSpace(id)) continue;
            lock (_urls) _urls[id.Trim()] = url.Trim();
        }
    }

    private static List<string> ParseCsvLine(string line)
    {
        var result = new List<string>();
        var current = new StringBuilder();
        var quoted = false;
        for (var i = 0; i < line.Length; i++)
        {
            var ch = line[i];
            if (ch == '"')
            {
                if (quoted && i + 1 < line.Length && line[i + 1] == '"')
                {
                    current.Append('"');
                    i++;
                }
                else quoted = !quoted;
                continue;
            }
            if (ch == ',' && !quoted)
            {
                result.Add(current.ToString().Trim());
                current.Clear();
                continue;
            }
            current.Append(ch);
        }
        result.Add(current.ToString().Trim());
        return result;
    }
}
