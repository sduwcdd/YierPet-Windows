"""Import the user's reviewed roles/text, preserving the original segmentation record."""
from pathlib import Path
import json, hashlib, shutil, csv, zipfile, argparse
import soundfile as sf

batch = Path(__file__).resolve().parent
parser = argparse.ArgumentParser()
parser.add_argument('--review', type=Path, default=batch.parent/'approved-20261014-04205443/voice-review.original.json')
review_path = parser.parse_args().review
review_bytes = review_path.read_bytes()
review_hash = hashlib.sha256(review_bytes).hexdigest()
review = json.loads(review_bytes.decode('utf-8-sig'))
original = json.loads((batch/'manifest.json').read_text(encoding='utf-8'))
original_clips = {c['id']:c for c in original['clips']}
review_clips = review['clips']
assert len({c['id'] for c in review_clips}) == len(review_clips), 'Duplicate reviewed IDs'
assert {c['id'] for c in review_clips} == set(original_clips), 'Reviewed clip set differs from source batch'
target = batch.parent / ('approved-20261014-' + review_hash[:8])
assert not target.exists(), 'Output already exists; preserve existing confirmed dataset'
prepared=[]
for item in review_clips:
    base=original_clips[item['id']]
    for field in ['file','sha256','source','start','end']:
        assert item[field]==base[field], f"Unexpected change to {item['id']} {field}"
    assert type(item['approved']) is bool
    if not item['approved']:continue
    assert item['role'] in ['yier','bubu'], 'Approved speaker must be assigned'
    text=item['text']
    assert isinstance(text,str) and text.strip() and not any(c in text for c in ['\r','\n','|']), 'Invalid label text'
    source=(batch/base['file']).resolve()
    assert source.is_relative_to(batch.resolve()) and source.is_file()
    assert hashlib.sha256(source.read_bytes()).hexdigest()==base['sha256'], 'Audio hash mismatch'
    info=sf.info(source)
    prepared.append((base,item,source,info))
assert prepared, 'No approved clips'
target.mkdir()
(target/'voice-review.original.json').write_bytes(review_bytes)
records=[]
for base,item,source,info in prepared:
    role=item['role'];relative=Path('audio')/role/(item['id']+'.wav');dest=target/relative
    dest.parent.mkdir(parents=True,exist_ok=True);shutil.copy2(source,dest)
    assert hashlib.sha256(dest.read_bytes()).hexdigest()==base['sha256']
    dest.with_suffix('.txt').write_text(item['text']+'\n',encoding='utf-8')
    records.append(dict(id=item['id'],role=role,role_name={'yier':'一二','bubu':'布布'}[role],
        approved=True,text=item['text'],text_status='用户已试听确认',
        file=relative.as_posix(),absolute_path=str(dest.resolve()),sha256=base['sha256'],
        duration=info.duration,sample_rate=info.samplerate,channels=info.channels,
        source=base['source'],start=base['start'],end=base['end'],
        original_role=base['role'],original_group=base['group'],
        original_note=base['note'],review_sha256=review_hash))
labels=target/'labels';labels.mkdir()
for role in ['yier','bubu']:
    selected=[r for r in records if r['role']==role]
    (labels/(role+'.list')).write_text(''.join(f"{r['absolute_path']}|{role}|ZH|{r['text']}\n" for r in selected),encoding='utf-8')
    (labels/(role+'.relative.list')).write_text(''.join(f"{r['file']}|{role}|ZH|{r['text']}\n" for r in selected),encoding='utf-8')

reference_records=[]
for role,clipid in [('yier','yier-002'),('bubu','bubu-009')]:
    record=next(r for r in records if r['id']==clipid and r['role']==role)
    assert 3<=record['duration']<=10
    dest=target/'references'/role/'reference.wav';dest.parent.mkdir(parents=True,exist_ok=True)
    shutil.copy2(target/record['file'],dest)
    (dest.parent/'reference.txt').write_text(record['text']+'\n',encoding='utf-8')
    reference_records.append(dict(role=role,clip_id=clipid,reference_audio=dest.relative_to(target).as_posix(),
        prompt_text=record['text'],prompt_lang='zh',sha256=record['sha256'],duration=record['duration']))
manifest=dict(schema_version=1,status='user_reviewed_training_data_ready',review_sha256=review_hash,
    review_source=str(review_path),review_date='2026-10-14',clips=records,references=reference_records,
    provenance='用户上传的 voice-review.json 为角色、文字与确认状态的依据；原始分段记录另行保留。',
    model_training_status='not_started')
(target/'manifest.json').write_text(json.dumps(manifest,ensure_ascii=False,indent=2),encoding='utf-8')
with (target/'已确认片段.csv').open('w',encoding='utf-8-sig',newline='') as f:
    writer=csv.DictWriter(f,fieldnames=['id','role_name','duration','text','source','start','end','original_role','original_group','file'],extrasaction='ignore')
    writer.writeheader();writer.writerows(records)
counts={role:dict(clips=sum(r['role']==role for r in records),seconds=round(sum(r['duration'] for r in records if r['role']==role),2)) for role in ['yier','bubu']}
changes=[dict(id=r['id'],before=r['original_role'],after=r['role']) for r in records if r['original_role']!=r['role']]
for role in ['yier','bubu']:
    rows=(labels/(role+'.list')).read_text(encoding='utf-8').splitlines()
    assert len(rows)==counts[role]['clips']
    for row in rows:
        path,speaker,language,text=row.split('|')
        assert Path(path).is_file() and speaker==role and language=='ZH' and text
report=dict(success=True,review_sha256=review_hash,total_approved=len(records),counts=counts,role_changes=changes,
    all_audio_hashes_verified=True,all_label_paths_verified=True,reference_count=len(reference_records),trained_models=0)
(target/'verification.json').write_text(json.dumps(report,ensure_ascii=False,indent=2),encoding='utf-8')
(target/'README.md').write_text(f'''# 用户已确认的一二、布布训练素材

已按用户提交的 voice-review.json 导入全部 {len(records)} 个勾选片段及其文字。

- 一二：{counts['yier']['clips']} 段，{counts['yier']['seconds']} 秒。
- 布布：{counts['bubu']['clips']} 段，{counts['bubu']['seconds']} 秒。
- 原“待确认”片段已按用户指定角色纳入，包括教程旁白和屏摄示范；原始来源与分组仍在 manifest.json 保留。

## 训练输入

- 一二标注：`labels/yier.list`
- 布布标注：`labels/bubu.list`
- 音频：`audio/yier/`、`audio/bubu/`
- 参考音及准确文本：`references/yier/`、`references/bubu/`

每行标注格式为 `音频绝对路径|说话者|ZH|用户确认文字`，可分别用于 GPT-SoVITS 的两个角色实验。不要把两位的标注合并成同一个说话者。

`*.list` 的绝对路径指向本机当前目录。迁移时使用对应 `*.relative.list` 里的相对路径，加上新机器素材目录作为前缀，重新生成绝对路径。不要直接把相对标注传给工作目录不同的训练程序。

角色变更：pending-001 → 布布；pending-002、pending-003、pending-004 → 一二。其余按确认文件保留，台词未自行改写。pending-005、pending-006、pending-007 也已由用户确认并纳入。

原视频、原切片和旧草稿未覆盖。所有 WAV 逐字节复制，保留 44.1 kHz、双声道、16-bit PCM；原始审核 JSON 逐字节存档，SHA256 为 `{review_hash}`。

此文件夹是已确认的训练数据，不是已训练模型。本次没有生成 GPT / SoVITS 权重，也没有完成音色效果测试。
''',encoding='utf-8')
archive=target.with_suffix('.zip')
with zipfile.ZipFile(archive,'x',zipfile.ZIP_DEFLATED,compresslevel=6) as z:
    for path in sorted(target.rglob('*')):
        if path.is_file():z.write(path,path.relative_to(target))
with zipfile.ZipFile(archive) as z:
    assert z.testzip() is None
    for r in records:assert hashlib.sha256(z.read(r['file'])).hexdigest()==r['sha256']
print(json.dumps(report,ensure_ascii=False))
print('DATASET',target)
print('ARCHIVE',archive)
