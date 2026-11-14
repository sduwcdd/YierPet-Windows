# 从 GitHub 恢复桌宠与本地语音工作流

恢复方案更新于 2026-11-14。桌宠源码和内置提醒音频从仓库恢复，最终采用的声音模型、参考录音、推理基础、审核素材及历史记录通过独立 Release 归档。按下面步骤下载并校验四个 ZIP 后恢复所需内容。四个归档与两份清单均已从公开下载链接完整回读，尺寸和 SHA256 全部匹配；随后完成未使用权重清理。见 [语音归档 Release](https://github.com/sduwcdd/YierPet-Windows/releases/tag/assets-voice-20261114) 和 [archive-2026-11-14.json](recovery/archive-2026-11-14.json)。

## 1. 哪些内容现在已纳入代码库

- `voice-training/runtime/`：训练、推理、模型下载、核验、开发台词比较、报告及打包脚本，CPU 环境版本、依赖输入与解析清单、三处上游兼容修改补丁。
- `voice-training/2026-10-14-six-videos/`：原视频解音频、机器转写、切片/审核包生成、导入人工审核的脚本。
- `voice-training/clarity-review-20261014/`：轻度清晰化对照脚本。
- `voice-training/recovery/`：固定上游提交、模型和文件 SHA256、可迁移配置模板、脱离个人目录的训练来源与结果记录，以及仍须保留的本地资产清单。
- `tools/restore_voice.py`：校验和恢复本地备份；只向当前检出的项目写入，不更改已安装桌宠或 AppData。
- `docs/recovery/legacy-editor-smoke/`：此前被忽略的编辑器验证源码文本，作为历史记录保存，不作为本轮验证成功的证据。

虚拟环境、基础模型、缓存、运行时生成的配置和个人音视频通过独立归档及本地环境管理。下载脚本固定现有文件的哈希。需要重新训练时，可按锁定哈希补回训练专用基础权重。

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

### 下载并校验语音归档

从 `assets-voice-20261114` Release 下载以下四个包及同页的 `SHA256SUMS.txt`、`archives.json`。在下载目录执行 `Get-FileHash .\*.zip -Algorithm SHA256`，逐项比对 `SHA256SUMS.txt` 中的文件名和哈希，包级文件数、大小与哈希见 `archives.json`，逐文件清单位于各 ZIP 的 `archive-manifest.json`。校验通过后再解压。下载入口见 [语音归档 Release](https://github.com/sduwcdd/YierPet-Windows/releases/tag/assets-voice-20261114)。

| 归档 | 内容 | 恢复方式 |
| --- | --- | --- |
| `selected-models.zip` | 最终采用的四个权重、两角色参考录音与文字、所选模型元数据 | 第4节恢复脚本 |
| `approved-recordings.zip` | 61个审核素材文件，包括24段确认录音、标注、参考音与审核原件 | 第4节恢复脚本 |
| `inference-runtime-assets.zip` | BERT、HuBERT、G2PW、fastText和FFmpeg | 固定上游检出后，按项目根相对路径解压 |
| `sources-and-records.zip` | 原视频、切片、训练与清晰化试听、日志和特征，排除模型权重 | 解压到单独目录查看 |

仅生成新台词时，需要模型包和推理基础包；另两包用于素材追溯及重新训练。

公开审核JSON采用脱敏副本，仅替换个人绝对路径前缀，保留原审核决定和文字。本地审核原件原样保存，原始SHA256继续记录在 `recovery/assets.json`。恢复脚本同时接受清单固定的原件与 `portableCopy` 副本尺寸和SHA256，并逐项核验。

完成上游检出和补丁应用后，从项目根目录执行以下命令，将下载路径替换为实际位置。

```powershell
Expand-Archive -LiteralPath "X:\backup\inference-runtime-assets.zip" -DestinationPath .
```

此包提供已有推理基础和FFmpeg，接下来安装Python依赖即可。`download_models.py`、`download_g2pw.py`、`download_frontend.py` 供按需重新下载使用。历史记录包中的成功标记应留在查看目录，新训练的输出由新运行产生。

使用独立的 **Python 3.10.18（Windows x64）** 创建推理/训练环境，避免修改系统 Python。以下 `py -3.10` 必须实际指向 3.10.18，可先检查版本。

```powershell
py -3.10 --version
py -3.10 -m venv voice-training/runtime/venv
$voicePy = (Resolve-Path voice-training/runtime/venv/Scripts/python.exe).Path
& $voicePy -m pip install torch==2.5.1+cpu torchaudio==2.5.1+cpu --index-url https://download.pytorch.org/whl/cpu
& $voicePy -m pip install -r voice-training/runtime/environment-lock.txt
& $voicePy -m pip check
```

`environment-lock.txt` 是当时 CPU 环境的精确版本快照，已移除原机器 wheel 绝对路径。`requirements-local.in` 是输入约束，`requirements-resolved.txt` 是解析记录。首次联网安装依赖需要时间。依赖索引无法提供某个固定版本时，记录缺失版本再处理。

### FFmpeg、语料与素材处理环境

视频切分/机器转写使用独立的 **Python 3.13.9** 环境，避免与 CPU 训练环境中 NumPy 等版本冲突：

```powershell
py -3.13 -m venv .venv
$mediaPy = (Resolve-Path .venv/Scripts/python.exe).Path
& $mediaPy -m pip install -r voice-training/recovery/media-environment-lock.txt
```

FFmpeg 已在推理基础包中，来自固定的 `imageio-ffmpeg==0.6.0`，记录中的文件为 Windows x64 FFmpeg 7.1。fastText `lid.176.bin` 位于项目内 `GPT_SoVITS/pretrained_models/fast_langdetect`，G2PW也在该包中。来源、尺寸和SHA256见 `upstream-lock.json`。此前中文生成在本机未安装以下NLTK语料时已通过。需要英文支持时，可另外联网安装。

```powershell
& $voicePy -m nltk.downloader -d voice-training/runtime/venv/nltk_data cmudict averaged_perceptron_tagger averaged_perceptron_tagger_eng
```

素材 ASR 的两个模型来源与提交均记录在 `upstream-lock.json`。`process_sources.py` 使用当前用户的 Hugging Face 缓存；`check_samples.py --model-path <本地模型目录>` 可指定 turbo 模型。必须按锁定提交下载完整模型；这些机器检查不是音色相似度评分。

## 4. 恢复现有模型和素材

先完成上节的上游源码检出，再恢复资产，避免模型目录阻挡 git clone。下面的恢复脚本只用标准库，不会加载模型。脚本中的根目录始终是当前克隆，不是 PowerShell 的任意当前目录。

### 方案 A：原项目目录仍在

将路径替换为你保存的旧项目目录。审计只读；`prepare` 仅复制清单中所需资产、渲染项目内配置。

```powershell
python tools/restore_voice.py audit --from-root "X:\backup\YierPet" --group all
python tools/restore_voice.py prepare --from-root "X:\backup\YierPet" --group training
```

`inference` 恢复四个所选模型与两段参考音，`training` 另外恢复全部24段已审核录音、两段训练参考音和审核原件，`all` 还会复制六个根目录原视频。模板同时补齐标注、参考文字和CPU训练配置。旧训练成功标记留在历史归档中。清理后重新训练使用审核素材从头进行。

### 方案 B：使用 Release 模型包和审核包

```powershell
python tools/restore_voice.py prepare --group training `
  --models-zip "X:\backup\selected-models.zip" `
  --approved-zip "X:\backup\approved-recordings.zip"
```

脚本仅读取清单列出的ZIP成员，逐项校验尺寸和SHA256。只需要现有音色生成新台词时，用 `--group inference`，省略审核包即可。配置模板或文件冲突时，在新的干净检出中恢复。`sources-and-records.zip` 另行解压查看，保留其中的历史目录结构。

生成的 `voice-training/restored-voices.json` 是后续导入桌宠的预设。现在只是文件恢复，环境未准备好前不能生成语音。

## 5. 完整性检查与实际使用

```powershell
python tools/restore_voice.py audit --group inference --runtime
```

该检查核对已恢复资产、上游提交和补丁、七个推理基础资产、G2PW与fastText文件、FFmpeg，以及Python / torch / torchaudio版本，全程按文件和版本进行核验。依赖安装检查与真实生成还需分别完成。

环境就绪后，在桌宠语音工坊导入新项目的 `voice-training/restored-voices.json`，保持“按需启动，生成后立即卸载”，先用一句短台词试听两位角色。导入动作由用户主动进行；恢复脚本不会更改 AppData。手动服务调试可使用 `voice-training/runtime/start_voice_service.ps1`，默认端口 9880；不要与现有服务同时占用同一端口。

需要重新训练时，保存已有结果，使用另一份干净检出/新输出目录，恢复 `training` 素材后重新提取特征。先用 `download_models.py --training` 补回基础GPT与SoVITS判别器，随后执行训练组审计。训练入口为 `train_pipeline.py setup`、`prepare`、`s2`、`s1`，默认CPU，全部角色依次执行。脚本会跳过已有成功阶段，历史记录包应单独解压，成功标记留在历史目录中。

```powershell
& $voicePy voice-training/runtime/download_models.py --training
python tools/restore_voice.py audit --group training --runtime
& $voicePy voice-training/runtime/train_pipeline.py setup
& $voicePy voice-training/runtime/train_pipeline.py prepare
& $voicePy voice-training/runtime/train_pipeline.py s2
& $voicePy voice-training/runtime/train_pipeline.py s1
```

重新审核视频时使用未建approved输出的独立目录，先运行素材处理/切片，再用 `apply_review.py --review <已确认审核JSON>`。原审核包与历史训练记录保存在归档中。历史 `evaluate_voices.py` 比较入口和 `verify_models.py` 使用过未采用的权重、基础GPT或训练断点，重跑这些历史检查时需准备对应文件或新训练结果。现有声音的文件核验使用本节的SHA256审计。

## 6. 最终保留范围与清理记录

最终声音仅保留以下四个权重及其参考录音、文字和配置。

| 角色 | GPT | SoVITS |
| --- | --- | --- |
| 一二 | `trained-20261014-v2/yier/yier-e10.ckpt` | `runtime/GPT-SoVITS/GPT_SoVITS/pretrained_models/gsv-v2final-pretrained/s2G2333k.pth` |
| 布布 | `trained-20261014-v2/bubu/bubu-e10.ckpt` | `trained-20261014-v2/bubu/bubu_e2_s182.pth` |

表中路径相对于 `voice-training/`。所选模型哈希见 `selected-models.json` 和 `recovery/assets.json`。BERT、HuBERT、G2PW、fastText和FFmpeg保留供推理使用，素材及试听、日志、特征保存到另外三个归档中。

2026-11-14 在四个 Release 归档完成远端完整校验后，已删除 13 个未采用权重及含额外弃用权重的旧中文模型 ZIP，共 5,623,846,621 字节。其中包括5个未采用的角色导出权重、6个含优化器状态的训练断点，以及训练专用的基础GPT和SoVITS判别器。各项路径、字节数、哈希、远端核验结果与实际删除状态以 [archive-2026-11-14.json](recovery/archive-2026-11-14.json) 为准。清理后可以恢复所选声音、查阅历史记录，并用审核素材从头训练。

`voice-training/recovery/local-preservation-inventory.json` 保存清理前的历史盘点，含旧ZIP及待删除权重的哈希。现行恢复按四个新归档及其 `archives.json`、`SHA256SUMS.txt` 执行。AppData 中的个人上传表情、设置和新增语音另行备份。本轮检查确认语音配置文件未改变；所选资产、推理基础均再次通过哈希核验。

## 7. 归档调整前的干净恢复验证

以下保留前次测试事实，执行详情见对应验证记录。本节记录使用旧模型ZIP与本机依赖缓存的恢复测试，本次四包归档的上传和远端核验结果另见第6节记录。

前次从提交 `aaf3ed8` 干净检出，直接执行 `windows/build-windows.ps1`，.NET SDK 8.0.428 构建为零警告、零错误，并在干净上游检出应用补丁后逐文件核对哈希。

另新建 Python 3.10.18 虚拟环境，通过本机已有 uv 缓存及已保留 CPU wheel 按完整锁文件安装了 171 个包，兼容性检查通过，未复用原 site-packages。使用原模型 ZIP 和审核 ZIP 恢复 33 项资产，迁移全部标注路径，复制并校验固定基础模型、G2PW、fastText 与 FFmpeg。新环境的训练配置准备和两位角色的新台词合成都通过，生成 WAV 均为 32 kHz 单声道非静音。临时服务已确认退出，临时检出、试音和测试文件随后清理。

本机离线缓存安装的可复现命令（仅当对应缓存和 wheel 已保存）：

```powershell
uv pip sync --offline --python $voicePy --find-links voice-training/runtime/wheels voice-training/runtime/environment-lock.txt
uv pip check --python $voicePy
```

详细检查记录见 [verification-2026-11-14.json](recovery/verification-2026-11-14.json)。首次退出后的即时端口检查曾与子进程退出发生时序竞争，随后独立检查确认整个临时进程树与端口均已关闭。前次测试使用本机已有依赖与模型缓存，其他电脑的联网安装仍需当地验证。
