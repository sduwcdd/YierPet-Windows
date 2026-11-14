from pathlib import Path
import json, csv, hashlib, shutil, html, zipfile
from importlib.metadata import version
import soundfile as sf
import numpy as np

OUT=Path(__file__).resolve().parent
ROOT=OUT.parent.parent
S=['13436440147704881','13436440169362419','13436440184337709','13436440199616983','13436440212397614','13436440231623196']
# Boundaries come from both ASR word timing passes, screen scene changes and available pauses.
# Text is a screen-corrected draft, not a claim of completed auditory review.
plan=[
 (0,0,4.10,'bubu','给我老婆许个愿，愿我老婆左手握健康。','main','画面选中布布；台词与输入框对应。'),
 (0,4.10,9.00,'bubu','右手握财富，头上顶好运，脚下踩祥瑞。','main','画面选中布布；祥瑞一词识别有分歧，需试听。'),
 (0,9.00,14.10,'bubu','前方有明灯，身后有依靠，朝夕皆顺遂。','main','画面文案校正；三处识别分歧，需重点试听。'),
 (0,14.10,18.35,'bubu','岁岁长安宁，一生有福气，万事皆圆满。','main','画面选中布布；15秒附近切换输入文案，保留来源剪辑标记。'),
 (1,0,2.84,'unknown','三秒教会大家合成一二布布的语音。','tutorial','教程开场，没有明确的当前说话者标签；不要直接作为一二训练数据。'),
 (1,2.84,6.19,'unknown','第一步，打开一二布布语音合成器。','tutorial','教程旁白；画面选择一二，但不能据此保证旁白就是所选音色。'),
 (1,6.19,10.17,'unknown','第二步，在语音合成器里输入文字。','tutorial','教程旁白；画面选择一二，声音归属待试听。'),
 (1,10.17,14.17,'unknown','第三步，点击合成语音即可合成哦。','tutorial','教程旁白；末尾语气词按转写保留，声音归属待试听。'),
 (1,14.17,16.85,'yier','臭布布，堵油拉污咪？','colloquial','画面明确标注一二声音示意；中英拟音写法识别分歧较大，单独待确认。'),
 (2,0,4.50,'yier','亲爱的布布，谢谢你一直以来的陪伴和支持。','main','画面选中一二；另一角色是台词称呼，不是当前说话者。'),
 (2,4.50,9.92,'yier','你就像我生活中的小太阳，总是给我带来温暖和快乐。','main','画面选中一二；画面文案与大模型转写一致。'),
 (2,9.92,15.20,'yier','有你在我身边，我觉得自己是世界上最幸福的小熊。','main','画面选中一二；小熊一词以画面及大模型转写校正。'),
 (3,0,5.60,'yier','窝就系一只不乖熊，窝经常欺负臭布布，让布布伤心了。','main','画面选中一二；保留原文口语写法，发音与文字需重点试听确认。'),
 (3,5.60,11.54,'yier','窝错了，熊家以后尽量避免凶布布，不对布布随便发脾气。','main','画面选中一二；凶布布的识别有分歧，以画面文案作草稿。'),
 (3,11.54,18.05,'yier','重新做熊，做一只可爱又贴心的小熊，至此公开道歉。','main','末句小熊后是否有宝字，两次识别与画面有分歧，需试听补正。'),
 (4,0,4.52,'bubu','我就是一只臭熊，我不爱洗澡，屡教不改。','main','画面选中布布；屡教不改识别分歧较大，按画面文案暂记。'),
 (4,4.52,8.02,'bubu','惹媳妇一二生气，我大错特错。','main','画面选中布布；一二为台词中的称呼。'),
 (4,8.02,13.46,'bubu','我以后要改过自新重新做熊，从此杜绝这种恶劣行为。','main','画面选中布布；从此一词需要试听。'),
 (4,13.46,17.58,'bubu','做一个好男熊，至此公开道歉。','main','画面选中布布；后半段文案来自画面下方检讨书。'),
 (5,0,4.46,'bubu','最近有好多人问我，我和一二的声音是怎么来的？','main','画面选中布布；与两次转写对应。'),
 (5,4.46,8.35,'bubu','其实是我花了很长时间训练出来的模型哦。','main','视频人物在说的台词，不是本项目已训练完成的声明；哦/啊需试听。'),
 (5,8.35,10.83,'bubu','目前有布布的，一二的。','main','语音延续上一段布布示范；中间画面切换为模型列表，来源单独标记。'),
 (5,11.95,15.30,'bubu','大家好，我叫布布。上个视频……','alternate_recording','另一页面的屏摄示范；末尾含义不明，可能有旁白，不与主音色混用。'),
 (5,17.10,19.75,'yier','大家好，我叫一二。','alternate_recording','另一页面的屏摄示范，画面标签一二；短且录制条件不同，单独保留。'),
]
names={'yier':'一二','bubu':'布布','unknown':'待辨认'}
sources=json.loads((OUT/'sources.json').read_text(encoding='utf-8'))
cache={}
for source in sources:
    f=ROOT/source['file']
    assert hashlib.sha256(f.read_bytes()).hexdigest()==source['sha256'], f.name
    dest=OUT/'original-videos'/f.name;dest.parent.mkdir(exist_ok=True)
    if not dest.exists():shutil.copy2(f,dest)
    assert hashlib.sha256(dest.read_bytes()).hexdigest()==source['sha256']
    audio,sr=sf.read(OUT/'audio-original'/(f.stem+'.wav'),dtype='int16',always_2d=True)
    # Keep original stereo PCM, sample rate and levels: no artificial restoration or resampling.
    cache[f.stem]=(audio,sr)
items=[];counters={'yier':0,'bubu':0,'pending':0}
for i,a,b,role,text,group,note in plan:
    main=group=='main';folder=role if main else 'needs-review';prefix=role if main else 'pending'
    counters[prefix]+=1;key=f'{prefix}-{counters[prefix]:03d}'
    audio,sr=cache[S[i]];first=round(a*sr);last=round(b*sr)
    assert 0<=first<last<=len(audio)
    chunk=audio[first:last];path=OUT/'clips'/folder/(key+'.wav');path.parent.mkdir(parents=True,exist_ok=True)
    sf.write(path,chunk,sr,subtype='PCM_16')
    restored,rate=sf.read(path,dtype='int16',always_2d=True)
    assert rate==sr and np.array_equal(chunk,restored),key
    asr={}
    for model,subdir in [('small','asr'),('large-v3-turbo','asr-turbo')]:
        words=[w for s in json.loads((OUT/subdir/(S[i]+'.json')).read_text(encoding='utf-8')) for w in s['words']]
        asr[model]=''.join(w['text'] for w in words if a<=(w['start']+w['end'])/2<b)
    item=dict(id=key,role=role,role_name=names[role],group=group,main_candidate=main,
        source=S[i]+'.mp4',start=a,end=b,duration=round(len(chunk)/sr,4),
        sample_rate=sr,channels=chunk.shape[1],file=path.relative_to(OUT).as_posix(),
        absolute_path=str(path),text=text,text_status='画面文案校正草稿；待逐字试听',
        approved=False,note=note,asr=asr,sha256=hashlib.sha256(path.read_bytes()).hexdigest(),
        near_full_scale_percent=round(float(np.mean(np.abs(chunk.astype(np.int32))>=32735))*100,4))
    items.append(item)
    path.with_suffix('.txt').write_text(text+'\n',encoding='utf-8')

manifest=dict(schema_version=1,created='2026-10-14',status='segmented_pending_auditory_review',
    sources=sources,tools={p:version(p) for p in ['faster-whisper','ctranslate2','soundfile','imageio-ffmpeg']},
    asr_model_revisions={'small':'536b0662742c02347bc0e980a01041f333bce120','large-v3-turbo':'0a363e9161cbc7ed1431c9597a8ceaf0c4f78fcf'},
    constraints=['未训练角色模型','未做背景音乐/重叠语音分离','角色依据画面标签和台词对应，不是声纹认证','未完成逐字试听确认'],clips=items)
(OUT/'manifest.json').write_text(json.dumps(manifest,ensure_ascii=False,indent=2),encoding='utf-8')
with (OUT/'片段清单.csv').open('w',encoding='utf-8-sig',newline='') as f:
    fields=['id','role_name','group','main_candidate','source','start','end','duration','text','note','file','approved']
    writer=csv.DictWriter(f,fieldnames=fields,extrasaction='ignore');writer.writeheader();writer.writerows(items)

refdir=OUT/'reference-candidates';refdir.mkdir(exist_ok=True)
references=[]
for role,clipid in [('yier','yier-002'),('bubu','bubu-009')]:
    item=next(c for c in items if c['id']==clipid)
    assert 3<=item['duration']<=10
    dest=refdir/(role+'-reference.wav');shutil.copy2(OUT/item['file'],dest)
    (refdir/(role+'-reference.txt')).write_text(item['text']+'\n',encoding='utf-8')
    references.append(dict(role=role,clip_id=clipid,duration=item['duration'],approved=False,text=item['text']))
(refdir/'references.json').write_text(json.dumps(references,ensure_ascii=False,indent=2),encoding='utf-8')

totals={r:dict(count=sum(c['main_candidate'] and c['role']==r for c in items),seconds=round(sum(c['duration'] for c in items if c['main_candidate'] and c['role']==r),2)) for r in ['yier','bubu']}
pending=[c for c in items if not c['main_candidate']]
stats=dict(main=totals,pending_count=len(pending),pending_seconds=round(sum(c['duration'] for c in pending),2),
           total_clips=len(items),source_seconds=round(sum(x['duration'] for x in sources),2),
           source_sha256_verified=6,clips_pcm_roundtrip_verified=len(items),trained_models=0,approved_clips=0)
(OUT/'verification.json').write_text(json.dumps(stats,indent=2),encoding='utf-8')
readme=f'''# 一二与布布：6 个视频的语音切分

已保存 {len(items)} 个 WAV 切片：一二主素材 {totals['yier']['count']} 段 / {totals['yier']['seconds']} 秒，布布主素材 {totals['bubu']['count']} 段 / {totals['bubu']['seconds']} 秒，单独待确认 {len(pending)} 段 / {stats['pending_seconds']} 秒。

## 怎么使用

1. 双击 `试听与核对.html`，逐条播放，并对照原视频。可以修改角色和文字，勾选“已试听确认”。
2. “导出确认清单”保存你的修改；“导出训练标注”只导出你勾选且角色明确的片段。初始没有任何片段自动获准进入训练。
3. 训练标注采用 GPT-SoVITS 的 `音频路径|说话者|ZH|文字` 形式，路径指向当前电脑的 WAV；移动素材后需更新路径。
4. `reference-candidates/` 提供一二和布布各一段 3–10 秒参考候选及文字，仍须试听确认，不会自动写入桌宠配置。

## 文件说明

- `clips/yier/`、`clips/bubu/`：画面角色标签与台词能对应的主候选。
- `clips/needs-review/`：教程旁白、拟音台词、另一页面屏摄示范。即使有角色提示，也先与主音色分开。
- `片段清单.csv`、`manifest.json`：原始文件、时间段、台词草稿、角色依据、识别差异、校验值。
- `original-videos/`：6 个原 MP4 的逐字节副本；项目根目录的原文件未修改。
- `audio-original/`：完整解码音轨，保留原采样率及双声道。
- `review/`：用于检查角色选项、字幕及切换页面的原画面截图。
- `asr/`、`asr-turbo/`：两次机器转写及词时间戳，保留原结果以供追溯。

## 当前限制

这些视频主要是合成语音演示，并不是未经处理的真人录音。切片为 44.1 kHz、16-bit PCM、双声道，保留原音的声道、音量及采样，不做变声、补词、拼接、降噪或背景音乐分离。没有把采样率升高来伪装音质提高。

角色分组根据画面的选项和台词对应，不能替代声音试听。台词由两次 ASR 加画面文案校正，未完成逐字听审；“屡教不改”“凶布布”、拟音句以及个别语气词有识别分歧，已在片段说明中标注。`approved=false` 是实际状态。

有少量原音采样接近满幅，统计见 `signal-stats.json`；未做无法验证效果的修复。背景声及重叠声音是否影响训练还需要试听检查。

本次只完成切分和标注草稿，没有训练一二或布布的声音模型。两位的主素材都不足一分钟，能否达到目标音色要在确认素材后试验，不能仅凭切分数量保证。

## 验证

已检查 6 个原视频 SHA256 未变化，{len(items)} 个切片可解码，逐采样与对应原音时间范围一致；参考候选均在 3–10 秒之间。结果见 `verification.json`。

转写使用 [faster-whisper](https://github.com/SYSTRAN/faster-whisper)，具体版本及本地模型提交保存在 manifest.json。`process_sources.py` / `build_pack.py` 是本批材料的可复现处理脚本，不是训练脚本。
'''
(OUT/'README.md').write_text(readme,encoding='utf-8')

template='''<!doctype html><html lang="zh-CN"><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"><title>一二和布布 · 声音素材核对</title>
<style>body{font:16px/1.6 "Microsoft YaHei",sans-serif;background:#f7f2ec;color:#412d26;max-width:1080px;margin:30px auto;padding:0 20px}h1{margin-bottom:5px}button,select{font:inherit;padding:8px 12px;border:1px solid #d6bca7;border-radius:8px;background:#fff9f3;cursor:pointer}button:hover{background:#eed8c7}header{position:sticky;top:0;background:#f7f2ec;padding:12px 0;z-index:1}.tools{display:flex;gap:8px;flex-wrap:wrap}.card{background:white;border:1px solid #e5d7ca;border-radius:12px;padding:18px;margin:16px 0}.meta{font-size:13px;color:#786557}.note{color:#936543;font-size:14px}audio{width:100%;margin:12px 0}textarea{box-sizing:border-box;width:100%;font:inherit;border:1px solid #d6bca7;border-radius:6px;padding:8px;min-height:75px}summary{cursor:pointer}video{max-width:100%;max-height:400px}label{display:inline-block;margin:8px 10px 0 0}.badge{font-size:13px;background:#f4e3d3;padding:4px 8px;border-radius:6px}a{color:#955332}#state{font-size:13px;color:#796352}</style>
<h1>一二和布布的声音素材</h1><p>逐条试听，确认是谁在说话、文字是否一致。切片保留原音；背景声、口语发音和不同版本的示范，需要在训练前检查。</p>
<header><div class="tools"><select id="filter"><option value="all">全部片段</option><option value="yier">一二主素材</option><option value="bubu">布布主素材</option><option value="pending">待确认素材</option></select><button id="export">导出确认清单</button><button id="training">导出训练标注（仅已确认）</button></div><div id="state"></div></header><main id="cards"></main>
<script>const manifest=__DATA__;const clips=manifest.clips;const key='yierpet-voice-review-20261014-six-videos-v1';let edits={};try{edits=JSON.parse(localStorage.getItem(key)||'{}')}catch(e){};const esc=s=>String(s??'').replace(/[&<>"']/g,c=>({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;'}[c]));
function value(c){const v={...c,...(edits[c.id]||{})};return {...v,original_role:c.role,role_name:({yier:'一二',bubu:'布布',unknown:'待辨认'})[v.role]||'待辨认'}}function status(){document.getElementById('state').textContent=`共 ${clips.length} 段，已试听确认 ${clips.filter(c=>value(c).approved).length} 段。修改保存在本浏览器；请导出清单备份。`}
function save(c,field,val){edits[c.id]={...(edits[c.id]||{}),[field]:val};try{localStorage.setItem(key,JSON.stringify(edits));status()}catch(e){document.getElementById('state').textContent='浏览器不能保存本地修改，请及时导出清单。'}}
function render(){let filter=document.getElementById('filter').value;const shown=clips.filter(c=>filter==='all'||filter==='pending'&&!c.main_candidate||c.main_candidate&&c.role===filter);document.getElementById('cards').innerHTML=shown.map(c=>{const v=value(c);return `<section class="card" data-id="${c.id}"><b>${c.id} · ${esc(c.role_name)}</b> <span class="badge">${c.main_candidate?'主候选，尚待听审':'单独待确认'}</span><div class="meta">${c.source} ｜ ${c.start.toFixed(2)}–${c.end.toFixed(2)} 秒 ｜ ${c.duration.toFixed(2)} 秒</div><audio controls preload="none" src="${c.file}"></audio><textarea aria-label="台词草稿">${esc(v.text)}</textarea><div class="note">${esc(c.note)}</div><label>角色 <select class="role"><option value="yier" ${v.role==='yier'?'selected':''}>一二</option><option value="bubu" ${v.role==='bubu'?'selected':''}>布布</option><option value="unknown" ${v.role==='unknown'?'selected':''}>待辨认 / 不使用</option></select></label><label><input class="approved" type="checkbox" ${v.approved?'checked':''}>已试听确认（角色、台词和切片均合适）</label><a href="${c.file}" download>下载这一段</a><details><summary>原视频与识别差异</summary><video controls preload="none" src="original-videos/${c.source}#t=${c.start},${c.end}"></video><p>small：${esc(c.asr.small)}<br>large-v3-turbo：${esc(c.asr['large-v3-turbo'])}</p></details></section>`}).join('');for(const el of document.querySelectorAll('.card')){const c=clips.find(c=>c.id===el.dataset.id);el.querySelector('textarea').oninput=e=>save(c,'text',e.target.value);el.querySelector('.role').onchange=e=>save(c,'role',e.target.value);el.querySelector('.approved').onchange=e=>save(c,'approved',e.target.checked)}status()}
document.addEventListener('play',e=>{for(const a of document.querySelectorAll('audio,video'))if(a!==e.target)a.pause()},true);
function download(name,data,type){const url=URL.createObjectURL(new Blob([data],{type}));const a=document.createElement('a');a.href=url;a.download=name;a.click();setTimeout(()=>URL.revokeObjectURL(url),1000)}
document.getElementById('filter').onchange=render;document.getElementById('export').onclick=()=>download('voice-review.json',JSON.stringify({...manifest,clips:clips.map(value)},null,2),'application/json');
document.getElementById('training').onclick=()=>{const rows=clips.map(value).filter(c=>c.approved&&['yier','bubu'].includes(c.role)&&c.text.trim());if(!rows.length){alert('请先试听并勾选确认片段。');return}download('confirmed.list',rows.map(c=>`${c.absolute_path}|${c.role}|ZH|${c.text.replace(/[\\r\\n|]+/g,' ').trim()}`).join('\\n')+'\\n','text/plain;charset=utf-8')};render();</script></html>'''
payload=json.dumps(manifest,ensure_ascii=False).replace('<','\\u003c')
(OUT/'试听与核对.html').write_text(template.replace('__DATA__',payload),encoding='utf-8')

archive=OUT.parent/'一二布布-六视频语音切片-20261014.zip'
with zipfile.ZipFile(archive,'w',zipfile.ZIP_DEFLATED,compresslevel=6) as z:
    for path in sorted(OUT.rglob('*')):
        if path.is_file() and path.suffix not in ['.log','.pyc'] and '__pycache__' not in path.parts:
            z.write(path,path.relative_to(OUT))
with zipfile.ZipFile(archive) as z:
    assert z.testzip() is None
    for item in items:assert hashlib.sha256(z.read(item['file'])).hexdigest()==item['sha256']
print(json.dumps(stats,ensure_ascii=False))
print('ZIP',archive,archive.stat().st_size,hashlib.sha256(archive.read_bytes()).hexdigest())
