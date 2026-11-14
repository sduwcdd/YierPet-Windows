"""Download only required official v2 assets; validate lengths and LFS SHA256."""
import argparse, concurrent.futures, hashlib, json, os, time
from pathlib import Path
import requests

ROOT = Path(__file__).parent / 'GPT-SoVITS/GPT_SoVITS/pretrained_models'
MANIFEST = Path(__file__).resolve().parents[1] / 'recovery/pretrained-models.json'
items = json.loads(MANIFEST.read_text(encoding='utf-8'))


def sha(path):
    h = hashlib.sha256()
    with path.open('rb') as f:
        for b in iter(lambda:f.read(8*1024*1024), b''): h.update(b)
    return h.hexdigest()

def download(item):
    path=ROOT/item['path']; path.parent.mkdir(parents=True,exist_ok=True)
    size=item['size']; expected=item.get('sha256') or item.get('lfs',{}).get('oid')
    if path.exists() and path.stat().st_size==size and (not expected or sha(path)==expected):
        print('verified existing',item['path'],flush=True); return
    url=item.get('url') or 'https://huggingface.co/lj1995/GPT-SoVITS/resolve/main/'+item['path']
    chunk=2*1024*1024
    parts=path.parent/(path.name+'.parts'); parts.mkdir(exist_ok=True)
    def part(i):
        start=i*chunk; end=min(size,start+chunk)-1; dest=parts/str(i)
        if dest.exists() and dest.stat().st_size==end-start+1:return
        for attempt in range(8):
            try:
                r=requests.get(url+f'?download=true&part={i}',headers={'Range':f'bytes={start}-{end}'},timeout=(30,90))
                r.raise_for_status()
                if size>chunk:
                    assert r.status_code==206 and r.headers.get('Content-Range')==f'bytes {start}-{end}/{size}',(r.status_code,r.headers.get('Content-Range'))
                assert len(r.content)==end-start+1,(len(r.content),end-start+1)
                dest.write_bytes(r.content);return
            except Exception as e:
                if attempt==7:raise
                print('retry',item['path'],i,str(e)[:100],flush=True);time.sleep(1+attempt)
    count=(size+chunk-1)//chunk
    with concurrent.futures.ThreadPoolExecutor(max_workers=item.get('workers',10)) as pool:
        for n,_ in enumerate(pool.map(part,range(count)),1):
            if n%25==0:print(item['path'],n,count,flush=True)
    temp=path.with_name(path.name+'.partial')
    with temp.open('wb') as f:
        for i in range(count):f.write((parts/str(i)).read_bytes())
    assert temp.stat().st_size==size
    if expected:assert sha(temp)==expected,'SHA256 mismatch: '+str(path)
    os.replace(temp,path)
    for f in parts.iterdir():f.unlink()
    parts.rmdir()
    print('VERIFIED',item['path'],size,flush=True)

if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--training', action='store_true', help='Also download the base GPT and discriminator for new training')
    args = parser.parse_args()
    for item in items:
        if args.training or not item.get('trainingOnly'):
            download(item)
    print('ALL_MODELS_VERIFIED',flush=True)
