"""Reproducible local training of the user-approved two-role v2 dataset."""
import argparse, hashlib, json, os, shutil, subprocess, sys, time
from pathlib import Path
import yaml

BASE=Path(__file__).resolve().parent
REPO=BASE/'GPT-SoVITS'
DATA=BASE.parent/'approved-20261014-04205443'
RUN=BASE.parent/'trained-20261014-v2'
PRE=REPO/'GPT_SoVITS/pretrained_models'
PY=BASE/'venv/Scripts/python.exe'
ROLES=('yier','bubu')

def digest(p):
    h=hashlib.sha256()
    with Path(p).open('rb') as f:
        for b in iter(lambda:f.read(8*1024*1024),b''):h.update(b)
    return h.hexdigest()

def write(p,data):
    p.parent.mkdir(parents=True,exist_ok=True)
    p.write_text(json.dumps(data,ensure_ascii=False,indent=2),encoding='utf-8')

def environment(role):
    e=os.environ.copy()
    e.update(PYTHONUTF8='1',PYTHONIOENCODING='utf-8',version='v2',hz='25hz',is_half='True',
             inp_text=str(DATA/f'labels/{role}.list'),inp_wav_dir='',exp_name=f'{role}-approved-v2',
             i_part='0',all_parts='1',opt_dir=str(REPO/f'logs/{role}-approved-v2'),
             bert_pretrained_dir=str(PRE/'chinese-roberta-wwm-ext-large'),
             cnhubert_base_dir=str(PRE/'chinese-hubert-base'),
             pretrained_s2G=str(PRE/'gsv-v2final-pretrained/s2G2333k.pth'),
             s2config_path=str(REPO/'GPT_SoVITS/configs/s2.json'),
             _CUDA_VISIBLE_DEVICES='0',CUDA_VISIBLE_DEVICES='0',USE_LIBUV='0',
             OMP_NUM_THREADS='4',MKL_NUM_THREADS='4',TOKENIZERS_PARALLELISM='false',YIER_DATA_WORKERS='0',CUDA_MODULE_LOADING='LAZY',
             PYTHONPATH=str(REPO)+os.pathsep+str(REPO/'GPT_SoVITS'))
    e['PATH']=str(REPO/'runtime')+os.pathsep+e['PATH']
    if os.environ.get('YIER_TRAIN_DEVICE','cpu')=='cpu':
        e['YIER_TRAIN_DEVICE']='cpu'
        e['CUDA_VISIBLE_DEVICES']='-1'
        e['_CUDA_VISIBLE_DEVICES']='-1'
    return e

def setup():
    RUN.mkdir(exist_ok=True)
    m=json.loads((DATA/'manifest.json').read_text(encoding='utf-8'))
    assert len(m['clips'])==24
    for c in m['clips']:
        assert c['approved'] and digest(DATA/c['file'])==c['sha256'],c['id']
    for role in ROLES:
        exp=REPO/f'logs/{role}-approved-v2';exp.mkdir(parents=True,exist_ok=True)
        (exp/'logs_s2_v2').mkdir(exist_ok=True)
        out=RUN/role;out.mkdir(exist_ok=True)
        for file in ('reference.wav','reference.txt'):
            shutil.copy2(DATA/f'references/{role}/{file}',out/file)
        s2=json.loads((REPO/'GPT_SoVITS/configs/s2.json').read_text())
        s2['train'].update(batch_size=1,epochs=2,segment_size=10240,fp16_run=False,log_interval=10,pretrained_s2G=str(PRE/'gsv-v2final-pretrained/s2G2333k.pth'),
            pretrained_s2D=str(PRE/'gsv-v2final-pretrained/s2D2333k.pth'),if_save_latest=True,
            if_save_every_weights=True,save_every_epoch=1,gpu_numbers='',grad_ckpt=False,lora_rank=32)
        s2['model']['version']='v2'
        s2['data']['exp_dir']=s2['s2_ckpt_dir']=str(exp)
        s2.update(save_weight_dir=str(out),name=role,version='v2')
        write(out/'s2.json',s2)
        s1=yaml.safe_load((REPO/'GPT_SoVITS/configs/s1longer-v2.yaml').read_text())
        s1['train'].update(batch_size=2,epochs=10,precision='32-true',save_every_n_epoch=5,if_save_every_weights=True,
            if_save_latest=True,if_dpo=False,half_weights_save_dir=str(out),exp_name=role)
        s1['data']['num_workers']=0
        s1.update(pretrained_s1=str(PRE/'gsv-v2final-pretrained/s1bert25hz-5kh-longer-epoch=12-step=369668.ckpt'),
            train_semantic_path=str(exp/'6-name2semantic.tsv'),train_phoneme_path=str(exp/'2-name2text.txt'),
            output_dir=str(exp/'logs_s1_v2'))
        (out/'s1.yaml').write_text(yaml.safe_dump(s1),encoding='utf-8')
    write(RUN/'provenance.json',dict(upstream_commit=subprocess.check_output(['git','rev-parse','HEAD'],cwd=REPO,text=True).strip(),
        approved_dataset=str(DATA),review_sha256=m['review_sha256'],clips={r:sum(c['role']==r for c in m['clips']) for r in ROLES},
        s2_epochs=2,s1_epochs=10,batch_size={'s2':1,'s1':2},s2_segment_samples=10240,training_device='cpu',seed=1234,python=str(PY),status='prepared'))

def call(role,stage,args):
    log=RUN/role/(stage+'.log'); receipt=RUN/role/(stage+'.result.json')
    if receipt.exists() and json.loads(receipt.read_text())['exit_code']==0:return
    started=time.time()
    print('START',role,stage,flush=True)
    with log.open('w',encoding='utf-8') as f:
        p=subprocess.run([str(PY),'-s',*args],cwd=REPO,env=environment(role),stdout=f,stderr=subprocess.STDOUT)
    write(receipt,dict(stage=stage,role=role,exit_code=p.returncode,seconds=round(time.time()-started,2),command=[str(PY),*args]))
    print('END',role,stage,p.returncode,flush=True)
    if p.returncode:raise RuntimeError(f'{role}/{stage} failed; see {log}')

def prepare(role):
    for stage,script in [('text','1-get-text.py'),('hubert','2-get-hubert-wav32k.py'),('semantic','3-get-semantic.py')]:
        call(role,'prepare-'+stage,['GPT_SoVITS/prepare_datasets/'+script])
    exp=REPO/f'logs/{role}-approved-v2'
    phones=(exp/'2-name2text-0.txt').read_text(encoding='utf-8')
    sem=(exp/'6-name2semantic-0.tsv').read_text(encoding='utf-8')
    expected={Path(l.split('|')[0]).name for l in (DATA/f'labels/{role}.list').read_text(encoding='utf-8').splitlines() if l.strip()}
    assert {l.split('\t')[0] for l in phones.splitlines() if l.strip()}==expected
    assert {l.split('\t')[0] for l in sem.splitlines() if l.strip()}==expected
    assert {p.stem for p in (exp/'3-bert').glob('*.pt')}==expected
    assert {p.stem for p in (exp/'4-cnhubert').glob('*.pt')}==expected
    assert {p.name for p in (exp/'5-wav32k').glob('*.wav')}==expected
    (exp/'2-name2text.txt').write_text(phones,encoding='utf-8')
    (exp/'6-name2semantic.tsv').write_text('item_name\tsemantic_audio\n'+sem+'\n',encoding='utf-8')
    pcount={l.split('\t')[0]:len(l.split('\t')[1].split()) for l in phones.splitlines() if l.strip()}
    scount={l.split('\t')[0]:len(l.split('\t')[1].split()) for l in sem.splitlines() if l.strip()}
    ratios={n:pcount[n]/(scount[n]/25) for n in expected}
    assert all(3<=ratios[n]<=25 and scount[n]<=54*25 and pcount[n]<=540 for n in expected),ratios
    write(RUN/role/'preparation-verified.json',dict(expected_count=len(expected),files=sorted(expected),all_features_present=True,passes_gpt_dataset_filters=True,phonemes_per_second=ratios))

if __name__=='__main__':
    ap=argparse.ArgumentParser();ap.add_argument('stage',choices=['setup','prepare','s2','s1','all']);ap.add_argument('--role',choices=ROLES)
    a=ap.parse_args()
    if a.stage=='setup':setup();sys.exit(0)
    for role in ([a.role] if a.role else ROLES):
        if a.stage in ('prepare','all'):prepare(role)
        if a.stage in ('s2','all'):call(role,'train-s2',['GPT_SoVITS/s2_train.py','--config',str(RUN/role/'s2.json')])
        if a.stage in ('s1','all'):call(role,'train-s1',['GPT_SoVITS/s1_train.py','--config_file',str(RUN/role/'s1.yaml')])
