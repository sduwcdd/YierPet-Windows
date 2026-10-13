# 内置表情素材

当前包含 **522 项原表情、5,917 帧**：

| 来源 | 编号 | 数量 | 说明 |
| --- | --- | --- | --- |
| [微信指定文章](https://mp.weixin.qq.com/s/CaQ-fa957yZ8h8b_sEHxwA) | `wx-0001`～`wx-0492` | 492 | 485 GIF、7 JPEG，483 份不同 SHA256 原文件，共 5,887 帧 |
| [GetStickerPack 合集](https://getstickerpack.com/stickers/xiao-xiong-yi-er-he-bu-bu-credits-to-huang-xiaob) | `gsp-0001`～`gsp-0030` | 30 | 512×512 静态 PNG |

来源 URL 与原文件 SHA256 保存在 `catalog*.json`。转载来源不等同于作者官方授权；代码许可证不授予第三方素材的商用权利。

## 文件用途

| 路径 | 用途 |
| --- | --- |
| `originals/` | 保存原文件字节，便于校验及重新编译 |
| `compiled/` | 按 GIF 帧偏移、透明度和 disposal 合成的 PNG 图集 |
| `posters/` | 表情库缩略图 |
| `baseline/` | 一二和布布的静态待机图 |
| `catalog*.json` | ID、角色、尺寸、帧数、时长、图集与来源 |
| `interactions*.json` | 状态映射、双人画面、先后回应、单人同步及追逐规则 |
| `semantics.tsv` / `semantics.json` | 人工动作与回应说明 / 运行时语义索引 |
| `spatial-review.json` | 画面左右关系与入场要求 |

运行时合并基础清单和同目录补充清单。两个空的 `*.generated-motion.json` 用于覆盖旧安装的生成走路入口，不加载生成动作。

225 条互动规则中有 114 组单人素材搭配，覆盖 186 张单人表情，包含 6 组追逐。这些跨素材关联是应用编排，不声称为作者连续剧情。文字、道具和原底色保持原样；极短帧时长按至少 20 ms 播放。

走路使用 `wx-0150`（一二）和 `wx-0183`（布布），运行时去除 GIF 画布内的横向漂移，并在右行时镜像。全量动作说明见 [逐项语义表](../../docs/art-direction/sticker-semantics.md)。

## 修改与校验

普通用户请使用应用内的上传、编辑及停用功能，操作方法见 [使用说明](../../docs/user-sticker-library.md)。个人覆盖保存在 AppData，不改内置文件。

开发者可在仓库根目录运行：

```powershell
python -m pip install -r tools/requirements.txt
python tools/validate_content.py
```

校验器读取原文件和索引，不下载或重写素材。只有确实需要重新导入时才执行下面的完整流程，它会覆盖生成清单，因此应先提交人工修改：

```powershell
python tools/import_wechat_stickers.py
python tools/review_wechat_stickers.py
python tools/import_getstickerpack.py
python tools/prepare_motion_and_pack.py
python tools/prepare_semantic_links.py
python tools/validate_content.py
```

微信导入器仅处理上述指定文章，不遍历公众号。`review/` 是工具按需生成的人工审阅联系表，不随仓库和发布 EXE 分发。源码、构建和发布说明见 [开发说明](../../docs/development.md)。
