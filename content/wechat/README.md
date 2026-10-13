# 原表情素材库

基础清单来自用户指定的当前文章：
https://mp.weixin.qq.com/s/CaQ-fa957yZ8h8b_sEHxwA

492 个正文图片条目，485 GIF、7 JPEG；483 份不同 SHA256 文件，共 5,887 帧。合集转载来源不等同于原作者官方授权或原始发行包。源 URL 与每份原文件 SHA256 均记录在 `catalog.json`。

## 后续补充包

本目录沿用最初 `wechat` 名称，现通过独立清单支持多来源。读取基础 `catalog.json` 后，按文件名合并 `catalog.*.json`；互动清单与状态映射同理。原微信编号与重新导入流程保持独立。

- `catalog.getstickerpack.json`：用户指定的 https://getstickerpack.com/stickers/xiao-xiong-yi-er-he-bu-bu-credits-to-huang-xiaob 。30 张 512×512 原始静态 PNG，`gsp-0001`～`gsp-0030`，单人一二 4、布布 16、双人 10。与微信原文件没有 SHA256 完全相同的文件；不代表视觉内容必然不同。全部原文件字节、RGBA 像素已核验。导入命令 `python tools/import_getstickerpack.py`。
- `catalog.generated-motion.json` 现为空清单，用于覆盖旧安装中的生成移动入口。历史源图保留，但不再被加载；左右移动使用 wx-0150 / wx-0183，右行运行时镜像，原帧不改。
- `python tools/prepare_motion_and_pack.py` 应用 30 项目视分类并重建当前语义清单，不再生成旧跑步索引。当前合计 522 个原图库条目。
- `semantics.tsv` 是全部 522 项动作与回应设想的可编辑源；`semantics.json` 是运行时说明与配对索引。`interactions.semantic.json` 有 114 个明确配对，覆盖 186 张单人原素材，包含 6 组追逐。生成命令 `python tools/prepare_semantic_links.py`，可读总表为 `docs/art-direction/sticker-semantics.md`。

闲置播放各自每隔 4–8 秒抽样，80% 单人原表情、20% 走动；禁用项与生成跑步不进入单人随机表情池。两个计时器独立，不要求对方空闲。双人互动仍由共同协调器管理。

## 目录

| 路径 | 用途 |
| --- | --- |
| `originals/` | 微信 CDN 返回的原始文件，按正文顺序编号，不改变字节 |
| `compiled/` | 按 GIF disposal 合成后的无损 RGBA 帧图集，未删文字、道具、底色 |
| `posters/` | 缩略图，供素材库浏览 |
| `baseline/` | 从用户已批准样稿直接裁切的一二、布布静态待机，背景连通白色区域变透明 |
| `catalog.json` | 稳定 ID、来源、哈希、角色、帧矩形、时长、名字 |
| `interactions.json` | 双人原动画、跨素材回应与状态映射 |
| `review/` | 全量联系表、多帧互动检查图与素材库渲染截图 |

## 删除和恢复

先在桌宠素材库中停用，选择写入 `%APPDATA%/YierPet-Windows/settings.json` 的 `DisabledStickerIds`，不修改原图。程序同时排除依赖此编号的互动和状态映射；播放中停用立即取消。恢复时点击同一按钮。后续用户可直接要求“删除 wx-0154、wx-0210”，再按编号移除目录项、依赖配置和无用文件，无需修改动作枚举。

## 角色与关联

分类来自人工目视原图预览与多时点复核。本次修正 wx-0044、0083、0188、0321、0328、0343、0453，区分动画后段出现的另一只角色、圆饼以及重复角色场景。多人场景与无明确身份的局部手势单列，不猜造互动。

既有的整体画面和顺序回应仍保留。新增的 `simultaneous`、`chase` 通过触发编号关联两个单人窗口，双方忙碌时回退独立；停用依赖立即取消。所有跨素材组合是应用编排，不声称为作者连续剧情。原图动作说明依据四个时长采样点，疑点补看密集帧；局部道具仍有疑问的行保留不确定表述。

## 可复现导入与验证

开发环境可用 `python -m pip install -r tools/requirements.txt` 安装依赖；桌宠运行本身不需要 Python。正常构建无需重新导入，以下流程仅用于重新下载和编译素材。

```powershell
python tools/import_wechat_stickers.py
python tools/review_wechat_stickers.py
python tools/import_getstickerpack.py
python tools/prepare_motion_and_pack.py
python tools/prepare_semantic_links.py
python tools/validate_content.py
dotnet build windows/YierPet.Windows/YierPet.Windows.csproj -c Release
```

下载器只读取指定文章正文和微信图片 CDN，不递归抓取整个公众号。重新导入会重建生成目录索引，角色和关联审阅由第二条命令重新应用；用户的停用选择独立保存，不受重导入影响。

像素与哈希校验结果见 `docs/art-direction/wechat-import-verification.json`（仓库根目录相对路径）。5,887 帧逐帧与原文件解码 RGBA 相等；15 个原始帧的极短延迟被设为 20ms，其余保持原时长。无生成式动作帧。来源图本来的白底、字幕和遮挡原样保留。
