from __future__ import annotations
import hashlib, json, sys
from pathlib import Path
from PIL import Image, ImageChops

def fail(message: str) -> None:
    print(f'ERROR: {message}', file=sys.stderr)
    raise SystemExit(1)

def tile(source: Image.Image, size: tuple[int,int]) -> Image.Image:
    result=Image.new('RGBA',size)
    for y in range(0,size[1],source.height):
        for x in range(0,size[0],source.width):
            result.alpha_composite(source,(x,y))
    return result

def main() -> None:
    root=Path(__file__).resolve().parents[1]
    asset_root=root/'Assets'/'Themes'/'StalkerApproved'
    manifest=json.loads((root/'approved-crop-manifest.json').read_text(encoding='utf-8'))
    source=Image.open(asset_root/manifest['source']).convert('RGBA')
    if list(source.size)!=manifest['source_size']: fail(f'approved reference size mismatch: {source.size}')
    verified=0
    for filename,item in manifest['assets'].items():
        path=asset_root/filename
        if not path.is_file(): fail(f'missing asset: {filename}')
        if hashlib.sha256(path.read_bytes()).hexdigest()!=item['sha256']: fail(f'SHA-256 mismatch: {filename}')
        actual=Image.open(path).convert('RGBA')
        if list(actual.size)!=item['size']: fail(f'size mismatch: {filename}: {actual.size}')
        if 'crop_box' in item:
            expected=source.crop(tuple(item['crop_box']))
            kind='exact crop'
        elif 'base_crop_box' in item:
            base=source.crop(tuple(item['base_crop_box']))
            expected=base.copy()
            for operation in item.get('operations',[]):
                box=tuple(operation['box']); x1,y1,x2,y2=box
                if operation['op']=='tile_fill':
                    fill=Image.open(asset_root/operation['source']).convert('RGBA')
                    expected.alpha_composite(tile(fill,(x2-x1,y2-y1)),(x1,y1))
                elif operation['op']=='restore_from_base':
                    expected.alpha_composite(base.crop(box),(x1,y1))
                else: fail(f'unknown operation {operation["op"]} in {filename}')
            kind='approved-only composition'
        else:
            fail(f'no verification recipe: {filename}')
        if ImageChops.difference(actual,expected).getbbox() is not None:
            fail(f'pixel verification failed: {filename}')
        verified+=1
        print(f'OK  {filename}  {actual.width}x{actual.height}  {kind}')
    print(f'Verified {verified} assets. Every pixel comes from the approved reference or its exact approved fill crop.')

if __name__=='__main__': main()
