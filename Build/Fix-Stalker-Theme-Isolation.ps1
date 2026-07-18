$ErrorActionPreference = 'Stop'

$texturePath = "Services/StalkerApprovedTextureRuntime.cs"
$text = Get-Content $texturePath -Raw

# Remove polling timers from both the main and settings STALKER runtimes.
$text = $text -replace '(?m)^\s*private readonly DispatcherTimer _timer;\r?\n', ''
$text = [regex]::Replace($text, '(?ms)\s*_timer = new DispatcherTimer\(DispatcherPriority\.Background, window\.Dispatcher\)\s*\{\s*Interval = TimeSpan\.FromMilliseconds\(\d+\)\s*\};\s*_timer\.Tick \+= TimerTick;\s*', "`r`n")
$text = $text -replace '(?m)^\s*_timer\.Start\(\);\r?\n', ''
$text = [regex]::Replace($text, '(?ms)\r?\n\s*private void TimerTick\(object\? sender, EventArgs e\)\s*\{.*?\r?\n\s*\}\r?\n', "`r`n")
$text = $text -replace '(?m)^\s*_timer\.Stop\(\);\r?\n', ''
$text = $text -replace '(?m)^\s*_timer\.Tick -= TimerTick;\r?\n', ''

$oldMain = @'
        private void ApplyNow()
        {
            if (_disposed || !_window.IsLoaded) return;
            var stalker = StalkerApprovedAssets.IsStalkerTheme();
            ApplyRoot(stalker);
            ApplyExactHeaderAndOuterFrame(stalker);
            ApplyExactPanelShells(stalker);
            ApplyExactCenterPanel(stalker);
            ApplyAidaMetricLayout(stalker);
            ApplyLiveControls(stalker);
            ApplyTypography(stalker);
            ApplyArtworkVisibility(stalker);
            _lastStalker = stalker;
        }
'@

$newMain = @'
        private void ApplyNow()
        {
            if (_disposed || !_window.IsLoaded) return;
            var stalker = StalkerApprovedAssets.IsStalkerTheme();

            if (stalker)
            {
                ApplyRoot(true);
                ApplyExactHeaderAndOuterFrame(true);
                ApplyExactPanelShells(true);
                ApplyExactCenterPanel(true);
                ApplyAidaMetricLayout(true);
                ApplyLiveControls(true);
                ApplyTypography(true);
                ApplyArtworkVisibility(true);
            }
            else if (_lastStalker)
            {
                ClearStalkerOverrides();
            }

            _lastStalker = stalker;
        }

        private void ClearStalkerOverrides()
        {
            foreach (var parts in _decorations.Values)
                SetVisibility(parts, false);

            foreach (var border in _borderStates.Keys)
            {
                border.ClearValue(Border.BackgroundProperty);
                border.ClearValue(Border.BorderBrushProperty);
                border.ClearValue(Border.BorderThicknessProperty);
                border.ClearValue(Border.CornerRadiusProperty);
            }

            foreach (var block in _contentStates.Keys)
            {
                block.ClearValue(Control.BackgroundProperty);
                block.ClearValue(Control.BorderBrushProperty);
                block.ClearValue(Control.BorderThicknessProperty);
                block.ClearValue(Control.PaddingProperty);
            }

            foreach (var control in _controlStates.Keys)
            {
                control.ClearValue(Control.BackgroundProperty);
                control.ClearValue(Control.BorderBrushProperty);
                control.ClearValue(Control.BorderThicknessProperty);
                control.ClearValue(Control.ForegroundProperty);
                control.ClearValue(Control.FontFamilyProperty);
            }

            foreach (var text in _textStates.Keys)
            {
                text.ClearValue(TextBlock.ForegroundProperty);
                text.ClearValue(TextBlock.FontFamilyProperty);
                text.ClearValue(TextBlock.FontWeightProperty);
            }

            foreach (var element in _opacityStates.Keys)
                element.ClearValue(UIElement.OpacityProperty);

            foreach (var border in _metricStates.Keys)
            {
                border.ClearValue(FrameworkElement.WidthProperty);
                border.ClearValue(FrameworkElement.HeightProperty);
                border.ClearValue(FrameworkElement.MarginProperty);
                border.ClearValue(Border.BackgroundProperty);
                border.ClearValue(Border.BorderBrushProperty);
                border.ClearValue(Border.BorderThicknessProperty);
                border.ClearValue(Border.CornerRadiusProperty);
            }

            _borderStates.Clear();
            _contentStates.Clear();
            _controlStates.Clear();
            _textStates.Clear();
            _opacityStates.Clear();
            _metricStates.Clear();

            _window.InvalidateVisual();
            _window.InvalidateMeasure();
            _window.InvalidateArrange();
        }
'@

if ($text.Contains($oldMain)) {
    $text = $text.Replace($oldMain, $newMain)
} elseif (-not $text.Contains('private void ClearStalkerOverrides()')) {
    throw 'Main ApplyNow block was not found.'
}

$oldSettings = @'
        private void ApplyNow()
        {
            var stalker = StalkerApprovedAssets.IsStalkerTheme();
            ApplyBorders(stalker);
            ApplyControls(stalker);
            ApplyImages(stalker);
            ApplyTexts(stalker);
            _lastStalker = stalker;
        }
'@

$newSettings = @'
        private void ApplyNow()
        {
            var stalker = StalkerApprovedAssets.IsStalkerTheme();
            if (stalker)
            {
                ApplyBorders(true);
                ApplyControls(true);
                ApplyImages(true);
                ApplyTexts(true);
            }
            else if (_lastStalker)
            {
                foreach (var border in _borders.Keys)
                {
                    border.ClearValue(Border.BackgroundProperty);
                    border.ClearValue(Border.BorderBrushProperty);
                    border.ClearValue(Border.BorderThicknessProperty);
                    border.ClearValue(Border.CornerRadiusProperty);
                }
                foreach (var control in _controls.Keys)
                {
                    control.ClearValue(Control.BackgroundProperty);
                    control.ClearValue(Control.BorderBrushProperty);
                    control.ClearValue(Control.BorderThicknessProperty);
                    control.ClearValue(Control.ForegroundProperty);
                    control.ClearValue(Control.FontFamilyProperty);
                }
                foreach (var image in _images.Keys)
                {
                    image.ClearValue(UIElement.VisibilityProperty);
                    image.ClearValue(UIElement.OpacityProperty);
                }
                foreach (var text in _texts.Keys)
                {
                    text.ClearValue(TextBlock.ForegroundProperty);
                    text.ClearValue(TextBlock.FontFamilyProperty);
                    text.ClearValue(TextBlock.FontWeightProperty);
                    text.ClearValue(UIElement.OpacityProperty);
                }
                _borders.Clear();
                _controls.Clear();
                _images.Clear();
                _texts.Clear();
                _window.InvalidateVisual();
            }
            _lastStalker = stalker;
        }
'@

if ($text.Contains($oldSettings)) {
    $text = $text.Replace($oldSettings, $newSettings)
}

Set-Content $texturePath $text -Encoding utf8

if ((Get-Content $texturePath -Raw) -match 'DispatcherTimer _timer|TimerTick') {
    throw 'Polling timer remains in STALKER texture runtime.'
}

$layoutPath = "Services/StalkerApprovedLayoutRuntime.cs"
$layout = Get-Content $layoutPath -Raw
$layout = $layout -replace '(?m)^\s*private readonly DispatcherTimer _timer;\r?\n', ''
$layout = [regex]::Replace($layout, '(?ms)\s*_timer = new DispatcherTimer\(DispatcherPriority\.Background, window\.Dispatcher\)\s*\{\s*Interval = TimeSpan\.FromMilliseconds\(\d+\)\s*\};\s*_timer\.Tick \+= TimerTick;\s*', "`r`n")
$layout = $layout -replace '(?m)^\s*_timer\.Start\(\);\r?\n', ''
$layout = [regex]::Replace($layout, '(?ms)\r?\n\s*private void TimerTick\(object\? sender, EventArgs e\)\s*\{.*?\r?\n\s*\}\r?\n', "`r`n")
$layout = $layout -replace '(?m)^\s*_timer\.Stop\(\);\r?\n', ''
$layout = $layout -replace '(?m)^\s*_timer\.Tick -= TimerTick;\r?\n', ''
$layout = $layout.Replace("            _window.WindowState = WindowState.Normal;`r`n", "")
$layout = $layout.Replace("            _window.MaxWidth = Math.Max(DesignWidth, work.Width);", "            _window.MaxWidth = double.PositiveInfinity;")
$layout = $layout.Replace("            _window.MaxHeight = Math.Max(DesignHeight, work.Height);", "            _window.MaxHeight = double.PositiveInfinity;")
$layout = $layout.Replace("            _window.Width = DesignWidth;", "            if (_window.WindowState == WindowState.Normal && _window.Width < 1200) _window.Width = Math.Min(DesignWidth, work.Width);")
$layout = $layout.Replace("            _window.Height = DesignHeight;", "            if (_window.WindowState == WindowState.Normal && _window.Height < 700) _window.Height = Math.Min(DesignHeight, work.Height);")
Set-Content $layoutPath $layout -Encoding utf8

if ((Get-Content $layoutPath -Raw) -match 'DispatcherTimer _timer|TimerTick') {
    throw 'Polling timer remains in STALKER layout runtime.'
}
