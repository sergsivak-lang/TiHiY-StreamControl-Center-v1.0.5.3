#!/usr/bin/env python3
import argparse
from pathlib import Path

REQUIRED_XAML = [
    'x:Name="DesignSurface"',
    'x:Name="DashboardBlocksGrid"',
    'x:Name="TopBlocksGrid"',
    'x:Name="BottomBlocksGrid"',
    'x:Name="FooterBlocksGrid"',
    'x:Name="ChatBlockPanel"',
    'x:Name="DonationsBlockPanel"',
    'x:Name="MixerBlockPanel"',
    'x:Name="NotificationsBlockPanel"',
    'x:Name="SystemStatusBlockPanel"',
    'x:Name="SystemMonitorPanel"',
]
REQUIRED_RUNTIME = [
    'SetRow(design, 0, 88)',
    'SetRow(design, 1, 57)',
    'SetRow(design, 2, 512)',
    'SetRow(design, 3, 7)',
    'SetRow(design, 4, 263)',
    'SetColumn(top, 0, 816)',
    'SetColumn(top, 1, 10)',
    'SetColumn(top, 2, 836)',
    'SetColumn(footer, 0, 583)',
    'SetColumn(footer, 2, 445)',
    'monitor.Margin = new Thickness(-5, 0, 0, 0)',
]
FORBIDDEN = ['FreeformDashboardCanvas']

parser = argparse.ArgumentParser()
parser.add_argument('--xaml', default='MainWindow.xaml')
parser.add_argument('--runtime', default='Services/StalkerApprovedLayoutRuntime.cs')
args = parser.parse_args()

xaml = Path(args.xaml).read_text(encoding='utf-8-sig')
runtime = Path(args.runtime).read_text(encoding='utf-8-sig')
missing = []
for token in REQUIRED_XAML:
    ok = token in xaml
    print(('OK      ' if ok else 'MISSING ') + token)
    if not ok: missing.append(token)
for token in REQUIRED_RUNTIME:
    ok = token in runtime
    print(('OK      ' if ok else 'MISSING ') + token)
    if not ok: missing.append(token)
for token in FORBIDDEN:
    ok = token not in runtime
    print(('OK      ' if ok else 'FORBIDDEN ') + f'no {token}')
    if not ok: missing.append('forbidden:' + token)
if missing:
    raise SystemExit('Layout verification failed: ' + ', '.join(missing))
print('Verified exact grid-based STALKER layout against the real final MainWindow.xaml.')
