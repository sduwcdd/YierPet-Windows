"""Import one explicitly selected article; preserve originals, compile lossless frames."""
import argparse
import hashlib
import io
import json
import math
import re
from concurrent.futures import ThreadPoolExecutor, as_completed
from pathlib import Path
from urllib.parse import urldefrag

import requests
from bs4 import BeautifulSoup
from PIL import Image, ImageDraw, ImageFont, ImageSequence

ROOT = Path(__file__).resolve().parents[1]
OUT = ROOT / 'content' / 'wechat'
URL = 'https://mp.weixin.qq.com/s/CaQ-fa957yZ8h8b_sEHxwA'


def fetch(url):
    for attempt in range(3):
        try:
            response = requests.get(url, timeout=(12, 50), headers={'User-Agent': 'Mozilla/5.0', 'Referer': URL})
            response.raise_for_status()
            return response.content
        except requests.RequestException:
            if attempt == 2:
                raise


def import_one(spec):
    ident, url, declared = spec
    data = fetch(url)
    with Image.open(io.BytesIO(data)) as im:
        fmt = im.format.lower()
        extension = 'jpg' if fmt == 'jpeg' else fmt
        original = OUT / 'originals' / f'{ident}.{extension}'
        original.write_bytes(data)
        width, height = im.size
        # Pillow coalesces GIF disposal and partial frame updates when seeking.
        frames, durations = [], []
        for source in ImageSequence.Iterator(im):
            frames.append(source.convert('RGBA').copy())
            durations.append(max(20, int(source.info.get('duration', 100))))
        columns = max(1, min(8, 4096 // width, len(frames)))
        rows_per_page = max(1, 4096 // height)
        per_page = columns * rows_per_page
        pages = []
        for start in range(0, len(frames), per_page):
            group = frames[start:start + per_page]
            atlas = Image.new('RGBA', (width * columns, height * math.ceil(len(group) / columns)))
            for n, frame in enumerate(group):
                atlas.paste(frame, ((n % columns) * width, (n // columns) * height))
            name = f'compiled/{ident}-{len(pages)}.png'
            atlas.save(OUT / name, compress_level=3)
            pages.append({'file': name, 'firstFrame': start, 'frameCount': len(group), 'columns': columns})
        poster = frames[len(frames) // 3].copy()
        poster.thumbnail((144, 144), Image.Resampling.LANCZOS)
        poster.save(OUT / 'posters' / f'{ident}.png')
        return {'id': ident, 'name': ident, 'sourceUrl': url, 'original': f'originals/{ident}.{extension}',
                'sha256': hashlib.sha256(data).hexdigest(), 'format': fmt, 'width': width, 'height': height,
                'frameCount': len(frames), 'durationsMs': durations, 'pages': pages,
                'poster': f'posters/{ident}.png', 'role': 'unreviewed', 'reviewed': False,
                'enabled': True, 'tags': [], 'bytes': len(data)}


def contact_sheets(assets):
    for offset in range(0, len(assets), 60):
        group = assets[offset:offset + 60]
        canvas = Image.new('RGB', (1200, math.ceil(len(group) / 10) * 145), '#e5e9ed')
        draw = ImageDraw.Draw(canvas)
        for n, asset in enumerate(group):
            x, y = n % 10 * 120, n // 10 * 145
            with Image.open(OUT / asset['poster']) as im:
                im.thumbnail((116, 118))
                canvas.paste(im, (x + (120-im.width)//2, y), im)
            draw.text((x+8,y+120), asset['id'], fill='#000000')
        canvas.save(OUT / 'review' / f'contact-{offset//60+1:02}.jpg', quality=92)


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--html', type=Path)
    parser.add_argument('--workers', type=int, default=6)
    args = parser.parse_args()
    for directory in ('originals','compiled','posters','review'):
        (OUT / directory).mkdir(parents=True, exist_ok=True)
    html = args.html.read_text(encoding='utf-8-sig') if args.html else fetch(URL).decode('utf-8')
    soup = BeautifulSoup(html, 'html.parser')
    body = soup.select_one('#js_content')
    if body is None:
        raise RuntimeError('Article body unavailable; refusing to import a verification page')
    images = body.find_all('img')
    specs = []
    for n, image in enumerate(images, 1):
        url = urldefrag(image.get('data-src') or image.get('src') or '')[0]
        if not re.match(r'https://mmbiz\.qpic\.cn/', url):
            raise RuntimeError(f'Unexpected image host at {n}: {url[:100]}')
        specs.append((f'wx-{n:04}', url, image.get('data-type')))
    assets, errors = [], []
    with ThreadPoolExecutor(max_workers=args.workers) as pool:
        tasks = {pool.submit(import_one, s):s[0] for s in specs}
        for n, future in enumerate(as_completed(tasks), 1):
            try:
                assets.append(future.result())
            except Exception as error:
                errors.append({'id': tasks[future], 'error': str(error)})
            if n % 40 == 0 or n == len(tasks):
                print(f'{n}/{len(tasks)} completed; failures={len(errors)}', flush=True)
    assets.sort(key=lambda a:a['id'])
    title = soup.select_one('#activity-name')
    catalog = {'schemaVersion':1,'sourceArticle':URL,'articleTitle':title.get_text(strip=True) if title else '',
               'bodyImageCount':len(images),'assets':assets,'errors':errors,
               'processing':'Original bytes and coalesced RGBA pixels preserved; no AI redraw, no background removal.'}
    (OUT / 'catalog.json').write_text(json.dumps(catalog,ensure_ascii=False,indent=2),encoding='utf-8')
    contact_sheets(assets)
    print(json.dumps({'imported':len(assets),'failed':len(errors),'uniqueSha256':len({a['sha256'] for a in assets}),
                      'totalFrames':sum(a['frameCount'] for a in assets),'sourceBytes':sum(a['bytes'] for a in assets)}),flush=True)
    if errors:
        raise SystemExit(1)


if __name__ == '__main__':
    main()
