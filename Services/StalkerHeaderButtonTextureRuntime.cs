using System;
using System.Collections.Generic;
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
        private readonly BitmapSource _headerWithoutPaintedButtons;
        private readonly ControlTemplate _buttonTemplate;
        private Image? _headerImage;
        private ImageSource? _originalHeaderSource;
        private double _originalHeaderWidth;
        private Stretch _originalHeaderStretch;
        private bool _headerWasChanged;
        private bool _lastStalker;
        private bool _disposed;

        internal Controller(MainWindow window)
        {
            _window = window;

            // Use the existing compiled WPF resource. Do not decode image data from
            // a runtime Base64 string: a truncated string caused the test-47 popup.
            _atlas = StalkerApprovedAssets.Load("header-full-exact.png");
            var headerCrop = new CroppedBitmap(_atlas, new Int32Rect(0, 0, 1085, 145));
            headerCrop.Freeze();
            _headerWithoutPaintedButtons = headerCrop;
            _buttonTemplate = CreateButtonTemplate();

            _window.Closed += WindowClosed;
            App.Services.Theme.ThemeChanged += ThemeChanged;

            // Two one-shot passes cover the normal Loaded order and the header
            // decoration created later by the approved texture runtime. No timers or
            // SizeChanged handlers are used.
            _window.Dispatcher.BeginInvoke(new Action(ApplyNow), DispatcherPriority.ContextIdle);
            _window.Dispatcher.BeginInvoke(new Action(ApplyNow), DispatcherPriority.ApplicationIdle);
        }

        private void ThemeChanged(object? sender, EventArgs e) =>
            _window.Dispatcher.BeginInvoke(new Action(ApplyNow), DispatcherPriority.ContextIdle);

        private void ApplyNow()
        {
            if (_disposed) return;
            var stalker = StalkerApprovedAssets.IsStalkerTheme();
            if (stalker)
            {
                HidePaintedHeaderButtonCopies();
                ApplyTextures();
            }
            else if (_lastStalker)
            {
                RestoreButtons();
                RestoreHeader();
            }

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

        private void HidePaintedHeaderButtonCopies()
        {
            if (_headerImage is null)
            {
                _headerImage = StalkerApprovedAssets.FindDescendants<Image>(_window)
                    .FirstOrDefault(image =>
                        (image.Source?.ToString() ?? string.Empty)
                        .Contains("header-full-exact.png", StringComparison.OrdinalIgnoreCase));
            }

            if (_headerImage is null) return;

            if (!_headerWasChanged)
            {
                _originalHeaderSource = _headerImage.Source;
                _originalHeaderWidth = _headerImage.Width;
                _originalHeaderStretch = _headerImage.Stretch;
                _headerWasChanged = true;
            }

            // Keep the approved emblem, title, status cards and skyline, but crop
            // away the painted copies of the action buttons. The real buttons remain
            // on top and receive their own individual cropped textures below.
            _headerImage.Source = _headerWithoutPaintedButtons;
            _headerImage.Width = 1085;
            _headerImage.Stretch = Stretch.Fill;
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

        private void RestoreHeader()
        {
            if (!_headerWasChanged || _headerImage is null) return;
            _headerImage.Source = _originalHeaderSource;
            _headerImage.Width = _originalHeaderWidth;
            _headerImage.Stretch = _originalHeaderStretch;
            _headerWasChanged = false;
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

            // Pixel-perfect crops from the already embedded approved header resource
            // (1672 x 145). Each live button receives its own CroppedBitmap.
            if (text == "−") crop = new Int32Rect(1184, 20, 39, 43);
            else if (text.Contains('%')) crop = new Int32Rect(1227, 20, 78, 43);
            else if (text == "+") crop = new Int32Rect(1312, 20, 41, 43);
            else if (text.StartsWith("МАКЕТ:", StringComparison.OrdinalIgnoreCase)) crop = new Int32Rect(1362, 20, 153, 43);
            else if (text == "—") crop = new Int32Rect(1524, 20, 42, 43);
            else if (text is "□" or "▢" or "▫") crop = new Int32Rect(1572, 20, 41, 43);
            else if (text is "×" or "X") crop = new Int32Rect(1620, 20, 41, 43);
            else if (text.Contains("НАЛАШТУВАННЯ", StringComparison.OrdinalIgnoreCase)) crop = new Int32Rect(1113, 76, 184, 53);
            else if (text.Contains("ТРАНСЛЯЦІЯ", StringComparison.OrdinalIgnoreCase)) crop = new Int32Rect(1307, 76, 163, 53);
            else if (text.Contains("МУЗИЧНИЙ ПЛЕЄР", StringComparison.OrdinalIgnoreCase)) crop = new Int32Rect(1481, 76, 180, 53);

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
                    .Where(value => !string.IsNullOrWhiteSpace(value));
                return string.Join(" ", parts);
            }

            return content.ToString() ?? string.Empty;
        }

        private static string ExtractElementText(UIElement? element) =>
            element is null ? string.Empty : ExtractText(element);

        private static string Normalize(string value) =>
            string.Join(" ", value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)).Trim();

        private void WindowClosed(object? sender, EventArgs e) => Dispose();

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            RestoreButtons();
            RestoreHeader();
            _window.Closed -= WindowClosed;
            App.Services.Theme.ThemeChanged -= ThemeChanged;
        }
    }
}
