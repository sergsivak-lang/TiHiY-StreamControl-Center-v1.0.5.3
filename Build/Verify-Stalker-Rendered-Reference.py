#!/usr/bin/env python3
"""Compare a real CI screenshot with texture-only regions of the approved mockup."""
import argparse, json
from pathlib import Path
from PIL import Image, ImageChops, ImageStat

REGIONS = {
    'outer-top': (0, 0, 1672, 18),
    'outer-bottom': (0, 920, 1672, 941),
    'outer-left': (0, 0, 18, 941),
    'outer-right': (1654, 0, 1672, 941),
    'header-emblem': (0, 0, 145, 145),
    'header-title': (145, 12, 610, 88),
    'header-skyline': (700, 0, 1080, 90),
    'chat-frame-tl': (5, 145, 78, 209),
    'donations-frame-tr': (1598, 145, 1672, 209),
    'mixer-frame-tl': (5, 449, 78, 513),
    'notifications-frame-tr': (1598, 449, 1672, 513),
    'system-frame-tl': (5, 664, 78, 728),
    'center-zone-panel': (590, 660, 1035, 927),
    'aida-frame-tr': (1598, 664, 1672, 728),
}

parser = argparse.ArgumentParser()
parser.add_argument('screenshot')
parser.add_argument('--reference', default='Assets/Themes/StalkerApproved/approved-full-reference.png')
parser.add_argument('--report', default='stalker-render-comparison.json')
parser.add_argument('--max-normalized-mae', type=float, default=0.075)
args = parser.parse_args()
shot = Image.open(args.screenshot).convert('RGB')
reference = Image.open(args.reference).convert('RGB')
if reference.size != (1672, 941):
    raise SystemExit(f'Approved reference has wrong size: {reference.size}')
normalized = shot.resize(reference.size, Image.Resampling.LANCZOS)
results, failed = {}, []
for name, box in REGIONS.items():
    expected, actual = reference.crop(box), normalized.crop(box)
    diff = ImageChops.difference(expected, actual)
    mae = sum(ImageStat.Stat(diff).mean) / (3.0 * 255.0)
    results[name] = {'box': box, 'normalized_mae': round(mae, 6)}
    print(f'{name:24s} MAE={mae:.4f}')
    if mae > args.max_normalized_mae: failed.append(name)
report = {
    'screenshot': str(args.screenshot), 'screenshot_size': shot.size,
    'reference': str(args.reference), 'reference_size': reference.size,
    'threshold': args.max_normalized_mae, 'regions': results,
    'passed': not failed, 'failed_regions': failed,
}
Path(args.report).write_text(json.dumps(report, ensure_ascii=False, indent=2), encoding='utf-8')
if failed: raise SystemExit('Approved texture comparison failed: ' + ', '.join(failed))
print('Rendered screenshot contains the approved STALKER texture regions.')
