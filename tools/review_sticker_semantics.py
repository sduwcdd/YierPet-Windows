"""Render time-spaced source frames for human semantic review; never edits assets."""
import json
from pathlib import Path
from PIL import Image, ImageDraw, ImageFont

ROOT = Path(__file__).resolve().parents[1]
CONTENT = ROOT / 'content/wechat'
OUT = CONTENT / 'review/semantics'


def frame(asset, index):
    page = next(p for p in asset['pages'] if p['firstFrame'] <= index < p['firstFrame'] + p['frameCount'])
    local = index - page['firstFrame']
    x, y = local % page['columns'] * asset['width'], local // page['columns'] * asset['height']
    with Image.open(CONTENT / page['file']) as sheet:
        return sheet.crop((x, y, x + asset['width'], y + asset['height'])).convert('RGBA')


def sample_indices(asset):
    durations = asset['durationsMs']
    result = []
    for fraction in (0, .25, .5, .75):
        target, elapsed = sum(durations) * fraction, 0
        for i, duration in enumerate(durations):
            elapsed += duration
            if elapsed > target:
                result.append(i)
                break
    return result


def main():
    OUT.mkdir(parents=True, exist_ok=True)
    assets = []
    for filename in ('catalog.json', 'catalog.getstickerpack.json'):
        assets.extend(json.loads((CONTENT / filename).read_text(encoding='utf-8'))['assets'])
    font = ImageFont.truetype('C:/Windows/Fonts/consola.ttf', 16)
    for offset in range(0, len(assets), 24):
        canvas = Image.new('RGB', (1664, 1080), '#e7ecf1')
        draw = ImageDraw.Draw(canvas)
        for cell, asset in enumerate(assets[offset:offset + 24]):
            x, y = cell % 4 * 416, cell // 4 * 180
            indices = sample_indices(asset)
            draw.text((x+5, y+4), f"{asset['id']} {asset['role']} [{','.join(map(str, indices))}]", font=font, fill='black')
            for col, index in enumerate(indices):
                picture = frame(asset, index)
                picture.thumbnail((100, 148))
                canvas.paste(picture, (x+col*104+(104-picture.width)//2, y+28+(148-picture.height)//2), picture)
            draw.line((x, y+179, x+415, y+179), fill='#9ca3af')
        canvas.save(OUT / f'review-{offset//24+1:02}.png')
    print(f'Rendered {len(assets)} originals, 4 time-spaced samples each, {OUT}')


if __name__ == '__main__':
    main()
