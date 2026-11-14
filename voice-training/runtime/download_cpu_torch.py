from pathlib import Path
import requests
import download_models as d
d.ROOT=Path(__file__).parent/'wheels'
for pkg,sha in [('torch','df93157482b672892d29134d3fae9d38ba3219702faedd79f407eb36774c56ce'),('torchaudio','8904a77ad311992d3dd3dae6556ea023fbb291b10b8698fecd1dec10bc189d6c')]:
    name=f'{pkg}-2.5.1+cpu-cp310-cp310-win_amd64.whl'
    url='https://download.pytorch.org/whl/cpu/'+name.replace('+','%2B')
    r=requests.get(url+'?size=1',headers={'Range':'bytes=0-1023'},timeout=45);r.raise_for_status()
    size=int(r.headers['Content-Range'].split('/')[-1])
    d.download(dict(path=name,size=size,url=url,workers=16,lfs={'oid':sha}))
print('CPU_WHEELS_VERIFIED',flush=True)
