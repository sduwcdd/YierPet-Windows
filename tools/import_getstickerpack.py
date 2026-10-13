"""Import the 30 image links obtained from the user-selected public pack page."""
import json
from concurrent.futures import ThreadPoolExecutor
from pathlib import Path
from PIL import Image,ImageDraw
import import_wechat_stickers as importer

ROOT=Path(__file__).resolve().parents[1]
PAGE='https://getstickerpack.com/stickers/xiao-xiong-yi-er-he-bu-bu-credits-to-huang-xiaob'

def main():
    importer.URL=PAGE
    urls=json.loads((ROOT/'docs/art-direction/getstickerpack-urls.json').read_text(encoding='utf8'))
    assert len(urls)==30
    # The page thumbnails include d=200x200. Same public image path without this
    # transform returns the original-resolution file, whose actual format is read.
    tasks=[(f'gsp-{i:04}',url.split('?')[0],'png') for i,url in enumerate(urls,1)]
    with ThreadPoolExecutor(max_workers=4) as pool:
        assets=list(pool.map(importer.import_one,tasks))
    for a in assets:a.update(sourceLabel='GetStickerPack',sourcePage=PAGE)
    out=importer.OUT
    (out/'catalog.getstickerpack.json').write_text(json.dumps({'schemaVersion':1,'sourceArticle':PAGE,'assets':assets},ensure_ascii=False,indent=2),encoding='utf8')
    canvas=Image.new('RGB',(1200,3*170),'#e5e9ed');draw=ImageDraw.Draw(canvas)
    for n,a in enumerate(assets):
        with Image.open(out/a['poster']) as im:
            im.thumbnail((116,140));canvas.paste(im,(n%10*120,n//10*170),im)
        draw.text((n%10*120,n//10*170+143),a['id'],fill='black')
    canvas.save(out/'review/getstickerpack-contact.png')
    print(json.dumps({'count':len(assets),'frames':sum(a['frameCount'] for a in assets),'sizes':sorted(set((a['width'],a['height']) for a in assets)),'formats':sorted(set(a['format'] for a in assets))}))

if __name__=='__main__':main()
