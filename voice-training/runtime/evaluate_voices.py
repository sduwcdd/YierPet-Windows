"""Generate held-out-text samples with base and fine-tuned models, same seed/reference."""
import argparse,json,os,sys,time
from pathlib import Path
from train_pipeline import REPO,RUN,PRE,DATA,digest,write,environment

TEXTS=[
 ('welcome','你回来啦，今天也辛苦了，快来抱抱。'),
 ('water','工作再忙，也要记得喝水和休息哦。'),
 ('play','我们一起出去散步吧，回来再吃小蛋糕。'),
]

def main():
    ap=argparse.ArgumentParser();ap.add_argument('--role',choices=['yier','bubu'],required=True);ap.add_argument('--mode',choices=['base','trained'],required=True)
    ap.add_argument('--gpt-epoch',type=int)
    ap.add_argument('--sovits-epoch',type=int)
    ap.add_argument('--label')
    a=ap.parse_args();role=a.role
    label=a.label or a.mode
    os.environ.update(environment(role));os.chdir(REPO)
    sys.path[:0]=[str(REPO),str(REPO/'GPT_SoVITS')]
    import numpy as np,soundfile as sf,torch
    from TTS_infer_pack.TTS import TTS,TTS_Config
    torch.set_num_threads(4)
    torch.backends.mkldnn.enabled=False
    out=RUN/role/'samples';out.mkdir(exist_ok=True)
    provenance=json.loads((RUN/'provenance.json').read_text(encoding='utf-8'))
    gpt=(RUN/role/f'{role}-e{provenance["s1_epochs"]}.ckpt') if a.mode=='trained' else PRE/'gsv-v2final-pretrained/s1bert25hz-5kh-longer-epoch=12-step=369668.ckpt'
    sovits=next((RUN/role).glob(f'{role}_e{provenance["s2_epochs"]}_s*.pth')) if a.mode=='trained' else PRE/'gsv-v2final-pretrained/s2G2333k.pth'
    if a.gpt_epoch is not None:
        gpt=RUN/role/f'{role}-e{a.gpt_epoch}.ckpt' if a.gpt_epoch else PRE/'gsv-v2final-pretrained/s1bert25hz-5kh-longer-epoch=12-step=369668.ckpt'
    if a.sovits_epoch is not None:
        sovits=next((RUN/role).glob(f'{role}_e{a.sovits_epoch}_s*.pth')) if a.sovits_epoch else PRE/'gsv-v2final-pretrained/s2G2333k.pth'
    cfg=dict(bert_base_path=str(PRE/'chinese-roberta-wwm-ext-large'),cnhuhbert_base_path=str(PRE/'chinese-hubert-base'),device='cpu',is_half=False,version='v2',t2s_weights_path=str(gpt),vits_weights_path=str(sovits))
    import yaml
    runtime_config=out/f'{label}-inference.yaml'
    runtime_config.write_text(yaml.safe_dump({'custom':cfg}),encoding='utf-8')
    pipeline=TTS(TTS_Config(str(runtime_config)))
    prompt=(RUN/role/'reference.txt').read_text(encoding='utf-8').strip()
    data=json.loads((DATA/'manifest.json').read_text(encoding='utf-8'))
    records=[]
    for key,text in TEXTS:
        assert all(text!=c['text'] for c in data['clips'])
        args=dict(text=text,text_lang='zh',ref_audio_path=str(RUN/role/'reference.wav'),prompt_text=prompt,prompt_lang='zh',top_k=15,top_p=1.0,temperature=1.0,text_split_method='cut5',batch_size=1,speed_factor=1.0,seed=1234,parallel_infer=False,split_bucket=False,return_fragment=False,fragment_interval=0.3,repetition_penalty=1.35,use_cuda_graph=False,use_flash_attention=False)
        started=time.time();chunks=list(pipeline.run(args));elapsed=time.time()-started
        assert chunks and len({sr for sr,_ in chunks})==1
        sr=chunks[0][0];audio=np.concatenate([chunk for _,chunk in chunks])
        assert audio.size and np.isfinite(audio).all()
        file=out/f'{label}-{key}.wav';sf.write(file,audio,sr,subtype='PCM_16')
        arr,_=sf.read(file);peak=float(np.max(np.abs(arr)));rms=float(np.sqrt(np.mean(arr**2)))
        assert 0.5<len(arr)/sr<25 and rms>0.001
        records.append(dict(role=role,mode=label,text=text,file=str(file),sample_rate=sr,duration=round(len(arr)/sr,3),generation_seconds=round(elapsed,3),peak=peak,rms=rms,sha256=digest(file),parameters=args))
        write(out/f'{label}-results.json',dict(gpt=str(gpt),sovits=str(sovits),gpt_sha256=digest(gpt),sovits_sha256=digest(sovits),samples=records))
        print('SAMPLE_OK',role,label,key,round(elapsed,1),flush=True)
    if a.mode=='trained' and not a.label:
        import yaml
        (RUN/role/'inference.yaml').write_text(yaml.safe_dump({'custom':cfg}),encoding='utf-8')

if __name__=='__main__':main()
