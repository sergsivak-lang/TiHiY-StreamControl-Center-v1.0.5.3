using System.Collections.Concurrent;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using TiHiY.StreamControlCenter.Models;

namespace TiHiY.StreamControlCenter.Controls;

/// <summary>
/// Displays a circular author avatar for the built-in multichat. Twitch and
/// YouTube profile images are resolved lazily and cached for the current run.
/// A user initial remains visible when a platform image is unavailable.
/// </summary>
public sealed class ChatAuthorAvatar : Grid
{
    private static readonly HttpClient Http = new();
    private static readonly ConcurrentDictionary<string, Lazy<Task<string>>> UrlCache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly ConcurrentDictionary<string, Lazy<Task<ImageSource?>>> ImageCache = new(StringComparer.OrdinalIgnoreCase);

    public static readonly DependencyProperty MessageProperty = DependencyProperty.Register(
        nameof(Message),
        typeof(ChatMessage),
        typeof(ChatAuthorAvatar),
        new FrameworkPropertyMetadata(null, OnMessageChanged));

    private readonly TextBlock _initial;
    private readonly Ellipse _photo;
    private int _loadVersion;

    public ChatMessage? Message
    {
        get => (ChatMessage?)GetValue(MessageProperty);
        set => SetValue(MessageProperty, value);
    }

    public ChatAuthorAvatar()
    {
        Width = 28;
        Height = 28;
        MinWidth = 28;
        MinHeight = 28;
        HorizontalAlignment = HorizontalAlignment.Center;
        VerticalAlignment = VerticalAlignment.Center;
        SnapsToDevicePixels = true;

        Children.Add(new Ellipse
        {
            Fill = new SolidColorBrush(Color.FromRgb(9, 28, 43)),
            Stroke = new SolidColorBrush(Color.FromRgb(55, 103, 134)),
            StrokeThickness = 1
        });

        _initial = new TextBlock
        {
            Foreground = Brushes.White,
            FontWeight = FontWeights.Bold,
            FontSize = 12,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            IsHitTestVisible = false
        };
        Children.Add(_initial);

        _photo = new Ellipse
        {
            Stroke = new SolidColorBrush(Color.FromRgb(91, 142, 174)),
            StrokeThickness = 1,
            Visibility = Visibility.Collapsed,
            IsHitTestVisible = false
        };
        Children.Add(_photo);
    }

    private static void OnMessageChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var control = (ChatAuthorAvatar)d;
        control.ApplyMessage(e.NewValue as ChatMessage);
    }

    private void ApplyMessage(ChatMessage? message)
    {
        var version = ++_loadVersion;
        _photo.Fill = null;
        _photo.Visibility = Visibility.Collapsed;
        _initial.Text = GetInitial(message?.User);
        ToolTip = message?.User;

        if (message is null)
            return;

        _ = LoadAvatarAsync(message, version);
    }

    private async Task LoadAvatarAsync(ChatMessage message, int version)
    {
        try
        {
            var cacheKey = BuildCacheKey(message);
            var url = await UrlCache.GetOrAdd(
                cacheKey,
                _ => new Lazy<Task<string>>(() => ResolveAvatarUrlAsync(message), LazyThreadSafetyMode.ExecutionAndPublication)).Value;

            if (string.IsNullOrWhiteSpace(url) || version != _loadVersion)
                return;

            var source = await ImageCache.GetOrAdd(
                url,
                value => new Lazy<Task<ImageSource?>>(() => DownloadImageAsync(value), LazyThreadSafetyMode.ExecutionAndPublication)).Value;

            if (source is null || version != _loadVersion)
                return;

            _photo.Fill = new ImageBrush(source)
            {
                Stretch = Stretch.UniformToFill,
                AlignmentX = AlignmentX.Center,
                AlignmentY = AlignmentY.Center
            };
            _photo.Visibility = Visibility.Visible;
        }
        catch
        {
            // The initial is a stable fallback when a remote avatar cannot be loaded.
        }
    }

    private static string BuildCacheKey(ChatMessage message)
    {
        var id = string.IsNullOrWhiteSpace(message.AuthorId) ? message.User : message.AuthorId;
        return $"{message.Platform}:{id}";
    }

    private static async Task<string> ResolveAvatarUrlAsync(ChatMessage message)
    {
        if (message.Platform.Equals("TWITCH", StringComparison.OrdinalIgnoreCase))
            return await ResolveTwitchAvatarAsync(message);

        if (message.Platform.Equals("YOUTUBE", StringComparison.OrdinalIgnoreCase))
            return await ResolveYouTubeAvatarAsync(message);

        return string.Empty;
    }

    private static async Task<string> ResolveTwitchAvatarAsync(ChatMessage message)
    {
        var services = App.Services;
        var clientId = services.Settings.Value.TwitchClientId?.Trim() ?? string.Empty;
        var token = LoadToken("TWITCH_TOKEN");
        if (string.IsNullOrWhiteSpace(clientId) || token is null || string.IsNullOrWhiteSpace(token.AccessToken))
            return string.Empty;

        var query = !string.IsNullOrWhiteSpace(message.AuthorId)
            ? "id=" + Uri.EscapeDataString(message.AuthorId)
            : "login=" + Uri.EscapeDataString(message.User.Trim());

        using var request = new HttpRequestMessage(HttpMethod.Get, "https://api.twitch.tv/helix/users?" + query);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.AccessToken);
        request.Headers.Add("Client-Id", clientId);

        using var response = await Http.SendAsync(request);
        if (!response.IsSuccessStatusCode)
            return string.Empty;

        var body = await response.Content.ReadAsStringAsync();
        var json = JsonNode.Parse(body)?.AsObject();
        return json?["data"]?.AsArray().FirstOrDefault()?["profile_image_url"]?.GetValue<string>() ?? string.Empty;
    }

    private static async Task<string> ResolveYouTubeAvatarAsync(ChatMessage message)
    {
        if (string.IsNullOrWhiteSpace(message.AuthorId))
            return string.Empty;

        var token = LoadToken("YOUTUBE_TOKEN");
        if (token is null || string.IsNullOrWhiteSpace(token.AccessToken))
            return string.Empty;

        var url = "https://www.googleapis.com/youtube/v3/channels?part=snippet&id=" + Uri.EscapeDataString(message.AuthorId);
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.AccessToken);

        using var response = await Http.SendAsync(request);
        if (!response.IsSuccessStatusCode)
            return string.Empty;

        var body = await response.Content.ReadAsStringAsync();
        var json = JsonNode.Parse(body)?.AsObject();
        var snippet = json?["items"]?.AsArray().FirstOrDefault()?["snippet"];
        return snippet?["thumbnails"]?["default"]?["url"]?.GetValue<string>()
            ?? snippet?["thumbnails"]?["medium"]?["url"]?.GetValue<string>()
            ?? string.Empty;
    }

    private static OAuthToken? LoadToken(string key)
    {
        try
        {
            var raw = App.Services.Credentials.LoadSecret(key);
            return string.IsNullOrWhiteSpace(raw) ? null : JsonSerializer.Deserialize<OAuthToken>(raw);
        }
        catch
        {
            return null;
        }
    }

    private static async Task<ImageSource?> DownloadImageAsync(string url)
    {
        try
        {
            var bytes = await Http.GetByteArrayAsync(url);
            await using var stream = new MemoryStream(bytes, writable: false);
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
            bitmap.StreamSource = stream;
            bitmap.EndInit();
            bitmap.Freeze();
            return bitmap;
        }
        catch
        {
            return null;
        }
    }

    private static string GetInitial(string? user)
    {
        if (string.IsNullOrWhiteSpace(user))
            return "?";

        return user.Trim()[0].ToString().ToUpperInvariant();
    }
}
