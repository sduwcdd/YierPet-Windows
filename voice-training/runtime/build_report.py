"""Package verification evidence and a local, offline listening page."""
import html,json,math
from pathlib import Path
from train_pipeline import RUN,REPO,digest,write
import matplotlib
matplotlib.use('Agg')
import matplotlib.pyplot as plt
from tensorboard.backend.event_processing.event_accumulator import EventAccumulator

fig,axes=plt.subplots(2,2,figsize=(11,7))
curves={};summary={}
for row,role in enumerate(('yier','bubu')):
    exp=REPO/f'logs/{role}-approved-v2'
    for col,(stage,folder,tag) in enumerate([('SoVITS',exp,'loss/g/mel'),('GPT',exp/'logs_s1_v2','total_loss_epoch')]):
        points=[]
        for event in folder.rglob('events.out.tfevents.*'):
            if stage=='SoVITS' and event.parent!=exp:continue
            ea=EventAccumulator(str(event),size_guidance={'scalars':0});ea.Reload()
            if tag in ea.Tags()['scalars']:points.extend((x.step,x.value) for x in ea.Scalars(tag))
        points=sorted(dict(points).items())
        assert points and all(math.isfinite(v) for _,v in points),(role,stage,tag)
        curves[role+'_'+stage]=points
        ax=axes[row,col];ax.plot([p[0] for p in points],[p[1] for p in points]);ax.set_title(role+' / '+stage+' training loss');ax.set_xlabel('logged training step');ax.set_ylabel(tag);ax.grid(alpha=.25)
    records={stage:json.loads((RUN/role/f'train-{stage}.result.json').read_text()) for stage in ('s1','s2')}
    assert all(r['exit_code']==0 for r in records.values())
    summary[role]=dict(training=records,files=[dict(file=str(p.relative_to(RUN)),bytes=p.stat().st_size,sha256=digest(p)) for p in (RUN/role).iterdir() if p.suffix in ('.ckpt','.pth','.wav','.yaml','.json','.txt') and 'result' not in p.name])
fig.tight_layout();fig.savefig(RUN/'training-curves.png',dpi=160);plt.close(fig)
write(RUN/'training-curves.json',curves)
asr=json.loads((RUN/'asr-evaluation.json').read_text(encoding='utf-8'))
extra_asr=RUN/'asr-checkpoint-comparison.json'
if extra_asr.exists():
    asr['samples']+=json.loads(extra_asr.read_text(encoding='utf-8'))['samples']
choices=json.loads((RUN/'selected-models.json').read_text(encoding='utf-8')) if (RUN/'selected-models.json').exists() else {}
lookup={s['file']:s for s in asr['samples']}
blocks=[]
labels={'base':'训练前：基础模型＋同一参考录音','trained':'完整训练：GPT 10 轮＋SoVITS 2 轮','gpt5-sovits2':'中途权重：GPT 5 轮＋SoVITS 2 轮','gptbase-sovits2':'组合对照：基础 GPT＋角色 SoVITS 2 轮'}
labels.update({'gpt10-sovitsbase':'组合对照：角色 GPT 10 轮＋基础 SoVITS','gpt10-sovits1':'组合对照：GPT 10 轮＋SoVITS 1 轮','gptbase-sovits1':'组合对照：基础 GPT＋角色 SoVITS 1 轮'})
for role,name in [('yier','一二'),('bubu','布布')]:
    blocks.append(f'<h2>{name}</h2><p>参考原录音</p><audio controls preload="none" src="{role}/reference.wav"></audio>')
    modes=[mode for mode in labels if (RUN/role/f'samples/{mode}-results.json').exists()]
    if role in choices:
        chosen=choices[role]['mode']
        blocks.append('<p><strong>当前试用配置：'+html.escape(labels[chosen])+'</strong>。依据下面三句的机器转写暂选；这些句子已用于选择配置，不是独立测试集。音色仍请你试听确认。</p>')
        modes.remove(chosen);modes.insert(0,chosen)
    results={mode:json.loads((RUN/role/f'samples/{mode}-results.json').read_text(encoding='utf-8')) for mode in modes}
    for i in range(3):
        blocks.append('<section><h3>'+html.escape(results['trained']['samples'][i]['text'])+'</h3><div class="pair">')
        for j,mode in enumerate(modes):
            if j==2:
                blocks.append('</div><details><summary>展开其他训练版本，比较发音与音色</summary><div class="pair">')
            label=labels[mode]
            s=results[mode]['samples'][i];rel=Path(s['file']).relative_to(RUN).as_posix();check=lookup[s['file']]
            blocks.append(f'<div><b>{label}</b><audio controls preload="none" src="{rel}"></audio><p>机器识别：{html.escape(check["transcription"])}</p><small>{s["duration"]:.2f} 秒 · 生成耗时 {s["generation_seconds"]:.2f} 秒</small> <a href="{rel}" download>下载 WAV</a></div>')
        blocks.append('</div>'+('</details>' if len(modes)>2 else '')+'</section>')
if (RUN/'desktop-asr-evaluation.json').exists():
    desktop=json.loads((RUN/'desktop-asr-evaluation.json').read_text(encoding='utf-8'))
    preview=['<h2>桌宠接口 · 两句新的试用台词</h2><p>通过桌宠实际调用代码生成；这两句没有用于上面的模型选择。机器转写仍可能有同音字错误，请以试听为准。</p><div class="pair">']
    for s in desktop['samples']:
        rel=Path(s['file']).relative_to(RUN).as_posix()
        name='一二' if s['role']=='yier' else '布布'
        preview.append(f'<section><h3>{name}</h3><p>{html.escape(s["text"])}</p><audio controls preload="none" src="{rel}"></audio><p>机器识别：{html.escape(s["transcription"])}</p><a href="{rel}" download>下载 WAV</a></section>')
    preview.append('</div>')
    blocks[:0]=preview
page='''<!doctype html><html lang="zh-CN"><meta charset="utf-8"><title>一二布布 · 声音训练试听</title><style>body{font:16px/1.65 system-ui,sans-serif;color:#40352e;background:#faf5ee;max-width:1100px;margin:32px auto;padding:0 24px}h1{font-size:30px}h2{margin-top:40px}section{background:white;padding:18px 22px;border-radius:16px;margin:16px 0}.pair{display:grid;grid-template-columns:1fr 1fr;gap:24px}audio{display:block;width:100%;margin:12px 0}small{color:#76685e}a{color:#88572d}img{width:100%}@media(max-width:700px){.pair{grid-template-columns:1fr}}</style><h1>一二布布 · 声音训练试听</h1><p>这是使用你已确认的 24 段录音训练的第一版。两个人物各训练 GPT 10 轮、SoVITS 2 轮；以下三句新台词不在训练标注中。</p><p>请比较发音、音色、语气和句尾完整性。机器识别只能辅助检查可懂度，不能代替人耳评价音色。训练前后使用相同台词、参考录音和随机种子。</p>'''+''.join(blocks)+'<h2>训练记录</h2><p>曲线显示训练过程，不代表音色相似度或独立测试集成绩。</p><img src="training-curves.png" alt="训练损失曲线"><p>本页完全离线，音频均在当前目录。不同文本继续合成需要配套 GPT-SoVITS 环境。</p></html>'
(RUN/'试听对比.html').write_text(page,encoding='utf-8')
write(RUN/'verification.json',dict(status='training_and_sample_generation_completed',roles=summary,sample_count=len(asr['samples']),asr=asr,subjective_voice_quality='awaiting_user_listening'))
print('REPORT_READY',str(RUN/'试听对比.html'))
