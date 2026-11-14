from pathlib import Path
import download_models as d
d.ROOT=Path(__file__).parent/'wheels'
d.download({'path':'torch-2.5.1+cu124-cp310-cp310-win_amd64.whl','size':2510729365,'workers':20,
 'url':'https://download.pytorch.org/whl/cu124/torch-2.5.1%2Bcu124-cp310-cp310-win_amd64.whl',
 'lfs':{'oid':'6f99d8459369cfd6661c2aee14787592fe50156a33faf9ef643ba04e42d6543f'}})
print('TORCH_WHEEL_VERIFIED',flush=True)
