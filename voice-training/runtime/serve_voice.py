"""Run the official local API with the CPU settings validated on this host."""
import os,runpy,sys,shutil
from train_pipeline import REPO,RUN,environment

os.environ.update(environment('yier'))
os.chdir(REPO)
sys.path[:0]=[str(REPO),str(REPO/'GPT_SoVITS')]
import torch
torch.set_num_threads(4)
torch.backends.mkldnn.enabled=False
source_config=RUN/'yier/inference.yaml'
assert source_config.exists(),source_config
config=RUN/'service-inference.yaml'
shutil.copy2(source_config,config)
sys.argv=[str(REPO/'api_v2.py'),'-a','127.0.0.1','-p','9880','-c',str(config)]
runpy.run_path(str(REPO/'api_v2.py'),run_name='__main__')
