# 从 GitHub 恢复桌宠与本地语音工作流

核验日期：2026-11-14。**桌宠源码和内置提醒音频可从 GitHub 恢复；当前角色的新台词生成及原训练记录还不能只靠 GitHub 完整恢复。** 模型、参考录音、审核切片、原视频与训练检查点没有上传到远端。不要因代码已同步而删除原项目目录。

## 1. 哪些内容现在已纳入代码库

- `voice-training/runtime/`：训练、推理、模型下载、核验、开发台词比较、报告及打包脚本，CPU 环境版本、依赖输入与解析清单、三处上游兼容修改补丁。
- `voice-training/2026-10-14-six-videos/`：原视频解音频、机器转写、切片/审核包生成、导入人工审核的脚本。
- `voice-training/clarity-review-20261014/`：轻度清晰化对照脚本。
- `voice-training/recovery/`：固定上游提交、模型和文件 SHA256、可迁移配置模板、脱离个人目录的训练来源与结果记录，以及仍须保留的本地资产清单。
- `tools/restore_voice.py`：校验和恢复本地备份；只向当前检出的项目写入，不更改已安装桌宠或 AppData。
- `docs/recovery/legacy-editor-smoke/`：此前被忽略的编辑器验证源码文本，作为历史记录保存，不作为本轮验证成功的证据。

虚拟环境、可重新下载的基础模型、缓存、运行时生成的配置、个人音视频仍被忽略。下载脚本固定现有文件的哈希；上游文件若变化会校验失败，不会静默接受新模型。

## 2. 干净检出并构建桌宠

在一个**新的、空的目录**检出，保留原目录：

```powershell
git clone https://github.com/sduwcdd/YierPet-Windows.git YierPet-restored
cd YierPet-restored
.\windows\build-windows.ps1
```

构建需要 .NET 8 SDK。不要加 `-Run`，除非你希望启动新检出的应用；不要运行 `install-windows.ps1` 覆盖正在使用的安装版。内置表情和预生成提醒配音已经在 Git 中，不需要语音训练环境。

## 3. 固定上游源码、补丁与运行环境

```powershell
git clone https://github.com/RVC-Boss/GPT-SoVITS.git voice-training/runtime/GPT-SoVITS
git -C voice-training/runtime/GPT-SoVITS checkout --detach 1b47ca07ef43a284e0b8bd7f29d3e1e3ff76082a
$patch = (Resolve-Path voice-training/runtime/local-memory-compatibility.patch).Path
git -C voice-training/runtime/GPT-SoVITS apply --check $patch
git -C voice-training/runtime/GPT-SoVITS apply $patch
```

补丁保留原上游日志结构，修改零 worker 的数据加载设置、已预处理 WAV 的读取与 CPU 卷积兼容设置。不能直接套到其他提交；重复应用也会失败。`recovery/upstream-lock.json` 保存应用补丁后三个文件的 LF 规范化哈希。

使用独立的 **Python 3.10.18（Windows x64）** 创建推理/训练环境，避免修改系统 Python。以下 `py -3.10` 必须实际指向 3.10.18，可先检查版本。

```powershell
py -3.10 --version
py -3.10 -m venv voice-training/runtime/venv
$voicePy = (Resolve-Path voice-training/runtime/venv/Scripts/python.exe).Path
& $voicePy -m pip install torch==2.5.1+cpu torchaudio==2.5.1+cpu --index-url https://download.pytorch.org/whl/cpu
& $voicePy -m pip install -r voice-training/runtime/environment-lock.txt
& $voicePy -m pip check
& $voicePy voice-training/runtime/download_models.py
& $voicePy voice-training/runtime/download_g2pw.py
& $voicePy voice-training/runtime/download_frontend.py
```

`environment-lock.txt` 是当时 CPU 环境的精确版本快照，已移除原机器 wheel 绝对路径。`requirements-local.in` 是输入约束，`requirements-resolved.txt` 是解析记录，不必重新解析以免版本漂移。首次联网安装所有依赖、下载数 GB 基础模型需要时间；依赖索引无法提供某个固定版本时应停止并记录，不要自动升级后宣称完全复现。

### FFmpeg、语料与素材处理环境

视频切分/机器转写使用独立的 **Python 3.13.9** 环境，避免与 CPU 训练环境中 NumPy 等版本冲突：

```powershell
py -3.13 -m venv .venv
$mediaPy = (Resolve-Path .venv/Scripts/python.exe).Path
& $mediaPy -m pip install -r voice-training/recovery/media-environment-lock.txt
& $mediaPy -c "from pathlib import Path; import imageio_ffmpeg,shutil; p=Path('voice-training/runtime/GPT-SoVITS/runtime/ffmpeg.exe'); p.parent.mkdir(parents=True,exist_ok=True); shutil.copyfile(imageio_ffmpeg.get_ffmpeg_exe(),p)"
& $voicePy -m nltk.downloader -d voice-training/runtime/venv/nltk_data cmudict averaged_perceptron_tagger averaged_perceptron_tagger_eng
```

FFmpeg 来自固定的 `imageio-ffmpeg==0.6.0`，记录中的文件为 Windows x64 FFmpeg 7.1；哈希由恢复审计核对。也可从保存的旧项目原位复制该二进制文件。上游语言检测还使用官方 fastText `lid.176.bin`，此固定上游通过 LangSegmenter 将缓存设为项目内 `GPT_SoVITS/pretrained_models/fast_langdetect`；上面的 `download_frontend.py` 会预先下载并核验；来源、尺寸和 SHA256 见 `upstream-lock.json`。离线机器须另存这一项目内缓存以及 NLTK 语料。G2PW ZIP 与解压模型也须保留或重新下载，不能仅靠角色权重运行。

素材 ASR 的两个模型来源与提交均记录在 `upstream-lock.json`。`process_sources.py` 使用当前用户的 Hugging Face 缓存；`check_samples.py --model-path <本地模型目录>` 可指定 turbo 模型。必须按锁定提交下载完整模型；这些机器检查不是音色相似度评分。

## 4. 恢复现有模型和素材

先完成上节的上游源码检出，再恢复资产，避免模型目录阻挡 git clone。下面的恢复脚本只用标准库，不会加载模型。脚本中的根目录始终是当前克隆，不是 PowerShell 的任意当前目录。

### 方案 A：原项目目录仍在

将路径替换为你保存的旧项目目录。审计只读；`prepare` 仅复制清单中所需资产、渲染项目内配置。

```powershell
python tools/restore_voice.py audit --from-root "X:\backup\YierPet" --group all
python tools/restore_voice.py prepare --from-root "X:\backup\YierPet" --group training
```

`inference` 只恢复四个所选模型与两段参考音；`training` 另外恢复全部 24 段已审核录音、两段训练参考音和审核原件；`all` 还会复制六个根目录原视频。模板同时补齐标注、参考文字和 CPU 训练配置。**不会复制旧训练成功标记或自动跳过新训练。** 这不是完整训练断点恢复工具，原 `GPT-SoVITS/logs/` 仍须另外保留。

### 方案 B：只有已有模型包和审核包

```powershell
python tools/restore_voice.py prepare --group training `
  --models-zip "X:\backup\一二布布-声音模型-20261014-v2.zip" `
  --approved-zip "X:\backup\approved-20261014-04205443.zip"
```

仅读取清单列出的 ZIP 成员，逐项校验尺寸和 SHA256。不向项目外解压；不覆盖不同的已有配置或资产。只需要现有音色生成新台词时，用 `--group inference`，省略审核包即可。ZIP 本身目前**不在 GitHub Release**，必须来自保留的本地备份。配置模板或文件冲突时，请在新的干净检出中恢复，不要强行覆盖现有结果。

生成的 `voice-training/restored-voices.json` 是后续导入桌宠的预设。现在只是文件恢复，环境未准备好前不能生成语音。

## 5. 完整性检查与实际使用

```powershell
python tools/restore_voice.py audit --group training --runtime
```

该检查核对已恢复资产、上游提交和补丁、九个基础模型文件、G2PW 与 fastText 文件、FFmpeg，以及 Python / torch / torchaudio 版本。它不加载模型，**也不能替代真实生成或依赖安装检查**。

环境就绪后，在桌宠语音工坊导入新项目的 `voice-training/restored-voices.json`，保持“按需启动，生成后立即卸载”，先用一句短台词试听两位角色。导入动作由用户主动进行；恢复脚本不会更改 AppData。手动服务调试可使用 `voice-training/runtime/start_voice_service.ps1`，默认端口 9880；不要与现有服务同时占用同一端口。

需要重新训练时，先保存旧结果，使用另一份干净检出/新输出目录。只恢复 `training` 素材即可重新提取特征，**不需要为了本次备份核验再训练一次**。训练入口为 `train_pipeline.py setup`、`prepare`、`s2`、`s1`；默认 CPU，全部角色依次执行。脚本会跳过已有成功阶段，所以不能把别处的成功标记复制来充当本次训练结果。

```powershell
& $voicePy voice-training/runtime/train_pipeline.py setup
& $voicePy voice-training/runtime/train_pipeline.py prepare
& $voicePy voice-training/runtime/train_pipeline.py s2
& $voicePy voice-training/runtime/train_pipeline.py s1
```

重新审核视频时需使用未建 approved 输出的独立目录，先运行素材处理/切片，再用 `apply_review.py --review <已确认审核JSON>`。原审核包及完整训练记录始终保留，不因新处理覆盖。

## 6. 仍需保留的独有资产

2026-11-14 实查三个 GitHub Release 均只有 EXE、SHA256SUMS 和 verification.json，**没有下表资产**。本地 ZIP 与同盘原文件不是异地备份。

| 本地位置 | 尺寸 | 为什么保留 |
| --- | ---: | --- |
| 根目录六个 MP4 | 3,887,164 B | 原始视频，也已包含在六视频 ZIP 中 |
| `voice-training/一二布布-六视频语音切片-20261014.zip` | 44,046,904 B | 原视频、原切片与审核草稿 |
| `voice-training/approved-20261014-04205443.zip` | 18,698,060 B | 24 段已确认录音、标注、参考音和审核原件 |
| `voice-training/一二布布-声音模型-20261014-v2.zip` | 597,349,689 B | 当前所选权重、参考音、部分最终权重及试听记录 |
| `voice-training/trained-20261014-v2/` | 972,506,034 B（审计时） | 含模型包未收录的 GPT 第 5 轮、SoVITS 第 1 轮权重及其他记录 |
| `voice-training/runtime/GPT-SoVITS/logs/` | 4,228,300,025 B | 完整训练恢复检查点、TensorBoard、特征和日志，不能视为环境缓存 |
| `voice-training/clarity-review-20261014/` | 见清单 | 用户试听过的清晰化对照与记录 |

三个 ZIP 已验证 CRC、模型包列出的 55 项 SHA256、审核包 61 项与本地的一致性以及六个原视频的一致性。详见 `voice-training/recovery/local-preservation-inventory.json`，另有 `assets.json` 精确列出日常推理/从头训练所需的最小资产。

**清理结论：暂不删除整个 YierPet 目录或整个 voice-training/runtime。** 后续若要释放空间，应先将上表独有内容备份到独立磁盘或可信远端，并重新校验 SHA256。AppData 中的个人上传表情、设置和新增语音同样应单独备份；它们不在本次源码同步范围，本次没有读取或改动。
