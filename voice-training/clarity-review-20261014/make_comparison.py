"""Non-destructive, loudness-matched listening trial; no model changes or denoising."""
import hashlib,html,json,re,subprocess
from pathlib import Path
import numpy as np
import soundfile as sf

OUT=Path(__file__).resolve().parent
ROOT=OUT.parent
RUN=ROOT/'trained-20261014-v2'
FF=ROOT/'runtime/GPT-SoVITS/runtime/ffmpeg.exe'
(OUT/'audio').mkdir(exist_ok=True)
EQ='highpass=f=65,equalizer=f=350:t=q:w=0.7:g=-2,equalizer=f=2600:t=q:w=0.8:g=2.5,treble=g=1.5:f=4500'
NORM='loudnorm=I=-18:TP=-1.5:LRA=11'
FORMAT='aformat=sample_rates=32000:channel_layouts=mono'

def sha(p):
    h=hashlib.sha256()
    with Path(p).open('rb') as f:
        for b in iter(lambda:f.read(8*1024*1024),b''):h.update(b)
    return h.hexdigest()

def measure(path,filters='anull'):
    result=subprocess.run([str(FF),'-hide_banner','-i',str(path),'-af',FORMAT+','+filters+','+NORM+':print_format=json','-f','null','-'],capture_output=True,text=True,check=True)
    return json.loads(re.findall(r'\{[^{}]*"input_i"[^{}]*\}',result.stderr,re.S)[-1])

def render(source,name,filters):
    prepared=OUT/'audio/.render-input.wav'
    subprocess.run([str(FF),'-y','-hide_banner','-loglevel','error','-i',str(source),'-ar','32000','-ac','1','-c:a','pcm_f32le',str(prepared)],check=True)
    before=measure(prepared,filters)
    gain=-18-float(before['input_i'])
    assert float(before['input_tp'])+gain<=-1.5
    norm=f'volume={gain}dB:precision=double'
    target=OUT/'audio'/name
    subprocess.run([str(FF),'-y','-hide_banner','-loglevel','error','-i',str(prepared),'-af',FORMAT+','+filters+','+norm,'-ar','32000','-ac','1','-c:a','pcm_s16le',str(target)],check=True)
    audio,sr=sf.read(target);original,source_sr=sf.read(source)
    assert np.isfinite(audio).all() and np.max(np.abs(audio))<1
    assert abs(len(audio)/sr-len(original)/source_sr)<0.03
    after=measure(target)
    assert abs(float(after['input_i'])+18)<0.35,after
    prepared.unlink()
    return dict(file=str(target),source=str(source),source_sha256=sha(source),sha256=sha(target),filters=filters,loudness_lufs=float(after['input_i']),true_peak_dbtp=float(after['input_tp']),duration=len(audio)/sr)

selected=json.loads((RUN/'selected-models.json').read_text(encoding='utf-8'))
records=[];blocks=[]
for role,label in [('yier','一二'),('bubu','布布')]:
    for key in ('gpt','sovits'):assert sha(selected[role][key])==selected[role][key+'_sha256']
    reference=render(RUN/role/'reference.wav',f'{role}-reference.wav','anull')
    records.append(reference)
    blocks.append(f'<h2>{label}</h2><p>原始参考录音（仅统一音量）：先判断原声是否也有发闷或电音。</p><audio controls preload="none" src="audio/{role}-reference.wav"></audio>')
    results=json.loads((RUN/role/f'samples/{selected[role]["mode"]}-results.json').read_text(encoding='utf-8'))
    for key,item in zip(('welcome','water'),results['samples'][:2]):
        blocks.append('<section><h3>'+html.escape(item['text'])+'</h3><div class="pair">')
        pair=[]
        for variant,title,filters in [('original','A · 原生成音（仅统一音量）','anull'),('clarity','B · 轻度清晰化试听',EQ)]:
            rec=render(Path(item['file']),f'{role}-{key}-{variant}.wav',filters);rec.update(role=role,text=item['text'],variant=variant);records.append(rec);pair.append(rec)
            rel=Path(rec['file']).relative_to(OUT).as_posix()
            blocks.append(f'<div><b>{title}</b><audio controls preload="none" src="{rel}"></audio><a href="{rel}" download>下载 WAV</a></div>')
        assert abs(pair[0]['loudness_lufs']-pair[1]['loudness_lufs'])<0.3
        blocks.append('</div></section>')
    for key in ('gpt','sovits'):assert sha(selected[role][key])==selected[role][key+'_sha256']

page='''<!doctype html><html lang="zh-CN"><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"><title>一二布布 · 清晰度对照</title><style>body{font:16px/1.65 system-ui,sans-serif;color:#40352e;background:#faf5ee;max-width:1050px;margin:32px auto;padding:0 24px}h1{font-size:30px}h2{margin-top:36px}section{background:white;padding:18px 22px;border-radius:16px;margin:16px 0}.pair{display:grid;grid-template-columns:1fr 1fr;gap:24px}audio{display:block;width:100%;margin:12px 0}a{color:#88572d}.note{background:#f0e4d5;padding:16px;border-radius:12px}@media(max-width:700px){.pair{grid-template-columns:1fr}}</style><h1>一二布布 · 清晰度对照</h1><p>保持你已选定的模型组合，比较同一段生成音在轻度均衡前后的听感。没有重新训练，也没有更换声音。</p><p class="note">A、B 已匹配响度，避免只因为更响就觉得更清楚。B 仅轻度削减低中频、提升中高频；没有改变音高、语速或使用降噪。它可能缓解发闷，也可能让电音更明显，不能修复已经丢失的发音细节。</p><p>建议重点听：咬字是否更清楚、电音是否变重、是否仍像原角色。当前仅为试听样本，尚未应用到桌宠。</p>'''+''.join(blocks)+'</html>'
(OUT/'清晰度对照.html').write_text(page,encoding='utf-8')
(OUT/'comparison-manifest.json').write_text(json.dumps(dict(status='listening_trial_not_applied',model_configurations_unchanged=True,loudness_target_lufs=-18,subjective_improvement='unverified',files=records),ensure_ascii=False,indent=2),encoding='utf-8')
print('CLARITY_COMPARISON_READY',len(records),'files; model hashes unchanged')
