"""Create a reusable model archive only after real training/inference verification."""
import json,zipfile
from pathlib import Path
from train_pipeline import RUN,BASE,digest,write

verified=json.loads((RUN/'model-verification.json').read_text(encoding='utf-8'))
report=json.loads((RUN/'verification.json').read_text(encoding='utf-8'))
assert report['sample_count']>=12
selected=json.loads((RUN/'selected-models.json').read_text(encoding='utf-8'))
entries=[]
for role in ('yier','bubu'):
    for stage,name in [('s1','gpt.ckpt'),('s2','sovits.pth')]:
        key='gpt' if stage=='s1' else 'sovits'
        path=Path(selected[role][key])
        assert digest(path)==selected[role][key+'_sha256']
        entries.append((path,f'{role}/{name}'))
        final=Path(verified[role][stage]['file'])
        if final!=path:
            assert digest(final)==verified[role][stage]['sha256']
            entries.append((final,f'{role}/training-final/{name}'))
    for name in ('reference.wav','reference.txt','s1.yaml','s2.json'):
        entries.append((RUN/role/name,f'{role}/{name}'))
    for path in sorted((RUN/role/'samples').glob('*.wav')):
        entries.append((path,f'{role}/samples/{path.name}'))
for path in sorted((RUN/'desktop-client/audio').glob('*.wav')):
    entries.append((path,path.relative_to(RUN).as_posix()))
for name in ('试听对比.html','training-curves.png','training-curves.json','asr-evaluation.json','asr-checkpoint-comparison.json','model-verification.json','selected-models.json','provenance.json','desktop-client-verification.json','desktop-asr-evaluation.json'):
    entries.append((RUN/name,name))
manifest=dict(format='GPT-SoVITS v2',training_device='cpu',gpt_epochs=10,sovits_epochs=2,
    selected_modes={role:selected[role]['mode'] for role in ('yier','bubu')},
    files=[dict(file=name,bytes=p.stat().st_size,sha256=digest(p)) for p,name in entries],
    subjective_quality='pending user listening; ASR checks are not speaker similarity scores')
readme='''# 一二布布声音模型 · 第一版

每个人物包含 GPT 权重、SoVITS 权重、参考录音和逐字文本。输入新文本不需要重新训练。
角色目录根部的 gpt.ckpt / sovits.pth 是当前试用组合；selected-models.json 记录具体来源。若它与完整训练的最终权重不同，最终权重另存于 training-final/。所有原始训练结果仍保留，未用试音筛选结果覆盖。

## 当前电脑使用

1. 运行 `voice-training/runtime/start_voice_service.ps1`，保留服务运行。
2. 打开桌宠的「语音工坊」，分别选择角色的 GPT、SoVITS 和参考 WAV，并填写 reference.txt 的文字。
3. 服务地址为 http://127.0.0.1:9880 。输入中文新台词，点击「生成并试听」。首次加载较慢。
4. 生成结果可以下载为 WAV。「归档声音文件」可以再复制一份到桌宠自己的语音目录。

## 备份与迁移

模型 ZIP 包包含两个人物的权重、参考素材、试听对比页及验证记录，不包含运行环境和基础模型，也不是独立 EXE。
在其他电脑准备匹配的 GPT-SoVITS v2 环境后，解压并重新选择各文件。训练配置中的原电脑绝对路径需要更新。
本机使用 Python 3.10.18、PyTorch / torchaudio 2.5.1+cpu；完整依赖版本见 environment-lock.txt。
上游源码固定于 1b47ca07ef43a284e0b8bd7f29d3e1e3ff76082a，Windows 兼容修改见 local-memory-compatibility.patch。

## 本次训练与试音

一二使用用户确认的 11 段录音（49.91 秒），布布使用 13 段（52.95 秒）。全部纳入特征提取和训练过滤检查。
每位 SoVITS 2 轮、GPT 10 轮。因系统内存限制使用 CPU，SoVITS 批量为 1、随机音频窗口为 10240 个采样点（0.32 秒），GPT 批量为 2。
完整录音保持不变。上游的小数据重复采样、日志目录和优化方法保留；CPU 卷积加速路径因本机崩溃而禁用。

打开「试听对比.html」比较三句新台词的训练前后结果；参考音、随机种子和文本相同。
机器转写用于辅助检查可懂度，训练曲线用于核对训练过程，二者都不是人工音色相似度评分。
本次是第一版少量素材训练，音色、语气及较长文本的稳定性仍需用户试听确认。
首轮机器检查发现一二的部分新台词转写偏差较大，因此增加 GPT 第 5 轮和基础 GPT 配合角色 SoVITS 的对照。当前试用组合按三句开发台词的转写结果暂选，这不是独立测试集成绩，也不保证音色最相似。请在试听页比较所有原始版本。

上游项目：https://github.com/RVC-Boss/GPT-SoVITS
'''
readme+='\n## 当前试用配置\n\n一二：训练 10 轮的 GPT，搭配基础 SoVITS。布布：训练 10 轮的 GPT，搭配训练 2 轮的 SoVITS。\n\n完整微调没有稳定超过基础模型的机器转写表现。一二完整微调的部分新台词偏差较大；当前配置仅是已训练组合中的暂选方案，不代表人工音色验收通过。基础模型对照也保存在试听页中。\n'
(RUN/'使用说明.md').write_text(readme,encoding='utf-8')
archive=RUN.parent/'一二布布-声音模型-20261014-v2.zip'
with zipfile.ZipFile(archive,'w',compression=zipfile.ZIP_STORED) as z:
    for p,name in entries:z.write(p,name)
    z.writestr('使用说明.md',readme)
    z.writestr('manifest.json',json.dumps(manifest,ensure_ascii=False,indent=2))
    for name in ('environment-lock.txt','local-memory-compatibility.patch'):
        z.write(BASE/name,name)
with zipfile.ZipFile(archive) as z:assert z.testzip() is None
write(RUN/'model-package.json',dict(file=str(archive),bytes=archive.stat().st_size,sha256=digest(archive),zip_crc_verified=True))
print('MODEL_PACKAGE_READY',archive)
