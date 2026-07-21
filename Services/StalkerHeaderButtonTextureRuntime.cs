using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace TiHiY.StreamControlCenter.Services;

internal static class StalkerHeaderButtonTextureBootstrap
{
    [ModuleInitializer]
    internal static void Initialize()
    {
        EventManager.RegisterClassHandler(
            typeof(MainWindow),
            FrameworkElement.LoadedEvent,
            new RoutedEventHandler(OnMainWindowLoaded));
    }

    private static void OnMainWindowLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is MainWindow window)
            _ = StalkerHeaderButtonTextureRuntime.Attach(window);
    }
}

internal static class StalkerHeaderButtonTextureRuntime
{
    private static readonly ConditionalWeakTable<MainWindow, Controller> Controllers = new();

    internal static IDisposable Attach(MainWindow window)
    {
        if (Controllers.TryGetValue(window, out var existing)) return existing;
        var controller = new Controller(window);
        Controllers.Add(window, controller);
        return controller;
    }

    private sealed class Controller : IDisposable
    {
        private readonly MainWindow _window;
        private readonly HashSet<Button> _styledButtons = new();
        private readonly BitmapSource _atlas;
        private readonly ControlTemplate _buttonTemplate;
        private bool _lastStalker;
        private bool _disposed;

        internal Controller(MainWindow window)
        {
            _window = window;
            _atlas = LoadAtlas();
            _buttonTemplate = CreateButtonTemplate();
            _window.Closed += WindowClosed;
            App.Services.Theme.ThemeChanged += ThemeChanged;
            _window.Dispatcher.BeginInvoke(new Action(ApplyNow), DispatcherPriority.ContextIdle);
        }

        private void ThemeChanged(object? sender, EventArgs e) =>
            _window.Dispatcher.BeginInvoke(new Action(ApplyNow), DispatcherPriority.ContextIdle);

        private void ApplyNow()
        {
            if (_disposed) return;
            var stalker = StalkerApprovedAssets.IsStalkerTheme();
            if (stalker)
                ApplyTextures();
            else if (_lastStalker)
                RestoreButtons();
            _lastStalker = stalker;
        }

        private void ApplyTextures()
        {
            foreach (var button in StalkerApprovedAssets.FindDescendants<Button>(_window))
            {
                if (!IsHeaderButton(button)) continue;
                var text = Normalize(ExtractText(button.Content));
                if (!TryResolveCrop(text, out var crop)) continue;

                button.Template = _buttonTemplate;
                button.Background = CreateCropBrush(crop);
                button.BorderThickness = new Thickness(0);
                button.Padding = new Thickness(0);
                button.HorizontalContentAlignment = HorizontalAlignment.Stretch;
                button.VerticalContentAlignment = VerticalAlignment.Stretch;
                _styledButtons.Add(button);
            }
        }

        private void RestoreButtons()
        {
            foreach (var button in _styledButtons)
            {
                button.ClearValue(Control.TemplateProperty);
                button.ClearValue(Control.BackgroundProperty);
                button.ClearValue(Control.BorderThicknessProperty);
                button.ClearValue(Control.PaddingProperty);
                button.ClearValue(Control.HorizontalContentAlignmentProperty);
                button.ClearValue(Control.VerticalContentAlignmentProperty);
            }
            _styledButtons.Clear();
        }

        private bool IsHeaderButton(Button button)
        {
            try
            {
                var point = button.TransformToAncestor(_window).Transform(new Point(0, 0));
                return point.Y >= 0 && point.Y <= 190;
            }
            catch
            {
                return false;
            }
        }

        private static bool TryResolveCrop(string text, out Int32Rect crop)
        {
            crop = default;
            if (text == "−") crop = new Int32Rect(83, 8, 36, 43);
            else if (text.Contains('%')) crop = new Int32Rect(127, 8, 97, 43);
            else if (text == "+") crop = new Int32Rect(230, 8, 34, 43);
            else if (text.StartsWith("МАКЕТ:", StringComparison.OrdinalIgnoreCase)) crop = new Int32Rect(273, 8, 151, 43);
            else if (text == "—") crop = new Int32Rect(444, 8, 37, 43);
            else if (text is "□" or "▢" or "▫") crop = new Int32Rect(489, 8, 35, 43);
            else if (text is "×" or "X") crop = new Int32Rect(531, 8, 37, 43);
            else if (text.Contains("НАЛАШТУВАННЯ", StringComparison.OrdinalIgnoreCase)) crop = new Int32Rect(14, 61, 214, 63);
            else if (text.Contains("ТРАНСЛЯЦІЯ", StringComparison.OrdinalIgnoreCase)) crop = new Int32Rect(242, 61, 165, 63);
            else if (text.Contains("МУЗИЧНИЙ ПЛЕЄР", StringComparison.OrdinalIgnoreCase)) crop = new Int32Rect(421, 61, 148, 63);
            return !crop.IsEmpty;
        }

        private ImageBrush CreateCropBrush(Int32Rect rect)
        {
            var cropped = new CroppedBitmap(_atlas, rect);
            cropped.Freeze();
            var brush = new ImageBrush(cropped)
            {
                Stretch = Stretch.Fill,
                AlignmentX = AlignmentX.Center,
                AlignmentY = AlignmentY.Center,
                TileMode = TileMode.None
            };
            brush.Freeze();
            return brush;
        }

        private static ControlTemplate CreateButtonTemplate()
        {
            var root = new FrameworkElementFactory(typeof(Border), "TextureRoot");
            root.SetBinding(Border.BackgroundProperty, new Binding(nameof(Control.Background))
            {
                RelativeSource = RelativeSource.TemplatedParent
            });
            root.SetValue(Border.SnapsToDevicePixelsProperty, true);
            root.SetValue(Border.CornerRadiusProperty, new CornerRadius(0));

            var template = new ControlTemplate(typeof(Button)) { VisualTree = root };
            var hover = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
            hover.Setters.Add(new Setter(UIElement.OpacityProperty, 0.94, "TextureRoot"));
            template.Triggers.Add(hover);
            var pressed = new Trigger { Property = Button.IsPressedProperty, Value = true };
            pressed.Setters.Add(new Setter(UIElement.OpacityProperty, 0.72, "TextureRoot"));
            template.Triggers.Add(pressed);
            var disabled = new Trigger { Property = UIElement.IsEnabledProperty, Value = false };
            disabled.Setters.Add(new Setter(UIElement.OpacityProperty, 0.42, "TextureRoot"));
            template.Triggers.Add(disabled);
            return template;
        }

        private static string ExtractText(object? content)
        {
            if (content is null) return string.Empty;
            if (content is string value) return value;
            if (content is TextBlock textBlock) return textBlock.Text ?? string.Empty;
            if (content is ContentControl contentControl) return ExtractText(contentControl.Content);
            if (content is Panel panel)
                return string.Join(" ", panel.Children.Cast<UIElement>().Select(ExtractElementText));
            if (content is Decorator decorator) return ExtractElementText(decorator.Child);
            if (content is DependencyObject dependencyObject)
            {
                var parts = LogicalTreeHelper.GetChildren(dependencyObject)
                    .Cast<object>()
                    .Select(ExtractText)
                    .Where(x => !string.IsNullOrWhiteSpace(x));
                return string.Join(" ", parts);
            }
            return content.ToString() ?? string.Empty;
        }

        private static string ExtractElementText(UIElement? element) => element is null ? string.Empty : ExtractText(element);

        private static string Normalize(string value) =>
            string.Join(" ", value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)).Trim();

        private static BitmapSource LoadAtlas()
        {
            var bytes = Convert.FromBase64String(ButtonAtlasBase64);
            using var stream = new MemoryStream(bytes, writable: false);
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.StreamSource = stream;
            image.EndInit();
            image.Freeze();
            return image;
        }

        private void WindowClosed(object? sender, EventArgs e) => Dispose();

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _window.Closed -= WindowClosed;
            App.Services.Theme.ThemeChanged -= ThemeChanged;
        }

        private const string ButtonAtlasBase64 = "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[... ELLIPSIZATION ...]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";
    }
}
