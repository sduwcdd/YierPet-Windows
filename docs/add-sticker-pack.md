# 追加微信素材包

先取得可读取的指定文章 HTML。若微信返回“环境异常 / 需要验证”，不要将验证页当作空合集导入，也不要用标题中的数量推断已经下载的素材数。

本次目标文章：用户提供的“布布一二表情包450+大合集（全网最全 保证有你喜欢的）”，链接 https://mp.weixin.qq.com/s/Z9FteIYKrm6JVpc9jD78gA 。当前尚未取得正文；以下为准备好的导入命令，不代表素材已导入。

```powershell
python tools/import_wechat_stickers.py --html article.html --url https://mp.weixin.qq.com/s/Z9FteIYKrm6JVpc9jD78gA --prefix z9 --catalog catalog.z9.json --source-label 微信450合集
```

新编号为 `z9-0001` 起，保持旧 `wx-*` / `gsp-*` 不变。原文件、图集和缩略图写入现有目录，人工审阅联系表写入忽略的 `review/`；不同包不会覆盖对方的编号。

拿到原文件后，先校对每张图的角色、多个时长采样点、实际动作和固定左右关系，再写资料与互动规则。哈希相同的旧素材可以复用已审阅的描述，并明确记录重合情况。

- `catalog.z9.json`：导入结果；未审阅素材保持 `reviewed=false`，不会自动播放。
- `semantics.z9.json`：与基础语义文件一样使用 `entries` 数组，记录动作、另一方可能回应、位置说明及关联。
- `interactions.z9.json`：已审阅的组合，沿用 `interactions` 数组。单人同步搭配必须按一二、布布顺序列出两张单人图，设置触发方与左右要求。
- 整体双人图继续整体播放；不拆分原人物。方向不明确时注明“与左右无关”，不猜测固定站位。

新增文件由运行时按文件名自动合并；`validate_content.py` 也合并所有包校验。不要修改基础 522 项的语义生成输入来挤入新包。
