"""Independent local ASR intelligibility check; not a voice similarity score."""
import argparse,json, re, subprocess, sys
from pathlib import Path
import numpy as np
from faster_whisper import WhisperModel
from opencc import OpenCC

BASE=Path(__file__).resolve().parent
RUN=BASE.parent/'trained-20261014-v2'
MODEL=Path.home()/'.cache/huggingface/hub/models--mobiuslabsgmbh--faster-whisper-large-v3-turbo/snapshots/0a363e9161cbc7ed1431c9597a8ceaf0c4f78fcf'
FF=BASE/'GPT-SoVITS/runtime/ffmpeg.exe'
ap=argparse.ArgumentParser()
ap.add_argument('--model-path', type=Path, default=MODEL)
ap.add_argument('--comparisons',action='store_true')
ap.add_argument('--modes',help='Only these comma-separated additional modes; append to comparison evidence')
args=ap.parse_args()
output=RUN/('asr-checkpoint-comparison.json' if args.comparisons else 'asr-evaluation.json')

cc=OpenCC('t2s')
def norm(s):return re.sub(r'[^\u4e00-\u9fffA-Za-z0-9]','',cc.convert(s)).lower()
def distance(a,b):
    row=list(range(len(b)+1))
    for i,x in enumerate(a,1):
        nxt=[i]
        for j,y in enumerate(b,1):nxt.append(min(nxt[-1]+1,row[j]+1,row[j-1]+(x!=y)))
        row=nxt
    return row[-1]

model=WhisperModel(str(args.model_path),device='cpu',compute_type='int8',cpu_threads=4)
records=json.loads(output.read_text(encoding='utf-8'))['samples'] if args.modes and output.exists() else []
for role in ('yier','bubu'):
    modes=sorted(p.name.removesuffix('-results.json') for p in (RUN/role/'samples').glob('*-results.json') if p.name not in ('base-results.json','trained-results.json')) if args.comparisons else ('base','trained')
    if args.modes:modes=[mode for mode in modes if mode in args.modes.split(',')]
    for mode in modes:
        results=json.loads((RUN/role/f'samples/{mode}-results.json').read_text(encoding='utf-8'))
        for s in results['samples']:
            assert not any(r['file']==s['file'] for r in records),'Refusing to overwrite a prior ASR observation'
            raw=subprocess.check_output([str(FF),'-v','error','-i',s['file'],'-f','f32le','-ac','1','-ar','16000','pipe:1'])
            seg,_=model.transcribe(np.frombuffer(raw,dtype=np.float32),language='zh',beam_size=5,condition_on_previous_text=False,vad_filter=False)
            text=''.join(x.text for x in seg)
            expected=norm(s['text']);actual=norm(text)
            records.append(dict(role=role,mode=mode,file=s['file'],expected=s['text'],transcription=text,character_edits=distance(expected,actual),reference_characters=len(expected),cer=round(distance(expected,actual)/len(expected),4)))
            print(role,mode,Path(s['file']).name,text,flush=True)
            output.write_text(json.dumps({'method':'faster-whisper large-v3-turbo CPU int8; Traditional Chinese normalized to Simplified; punctuation removed; no prompt','limitation':'ASR agreement is only an intelligibility proxy, not a human evaluation or speaker similarity measurement.','samples':records},ensure_ascii=False,indent=2),encoding='utf-8')
report=json.loads(output.read_text(encoding='utf-8'))
report['aggregate']=[]
for role in ('yier','bubu'):
    for mode in sorted({r['mode'] for r in records if r['role']==role}):
        rows=[r for r in records if r['role']==role and r['mode']==mode]
        edits=sum(r['character_edits'] for r in rows)
        chars=sum(r['reference_characters'] for r in rows)
        report['aggregate'].append(dict(role=role,mode=mode,samples=len(rows),character_edits=edits,reference_characters=chars,cer=edits/chars))
output.write_text(json.dumps(report,ensure_ascii=False,indent=2),encoding='utf-8')
