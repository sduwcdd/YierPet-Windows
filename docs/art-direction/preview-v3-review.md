# 人物静态样稿 v3

状态：用户已确认此静态样稿；未接入桌宠程序。

## 后续动作素材约束（2026-10-11）

用户已否决随后生成的动作图集，原因是人物形象偏离样稿。这些生成图未接入项目 Assets，不作为后续参考。

停止生成式重绘人物及动作。后续优先收集用户认可的现成一二布布表情素材，保留原始人物像素与动作，使用确定性的裁切、透明背景处理、拆帧及动画接入。没有对应素材的动作不得自行补画。不同表情包的画风需先与样稿核对，不能承诺跨来源人物百分之百一致。

来源核查（当时）：黄小B微博 https://www.weibo.com/u/2623471650 的可检索内容包含一二表情包上架公告；当时尚未取得微信表情专辑原始文件。公开转载候选需标记为转载，不能冒充微信官方文件。

后续用户提供了文章 https://mp.weixin.qq.com/s/CaQ-fa957yZ8h8b_sEHxwA ，已从其正文完整导入 492 项原文件并接入素材库，详细记录见 `content/wechat/README.md`。此静态样稿只经确定性裁切与边缘白底清理，作为默认待机；动作使用文章原图，不再生成式重绘。

本轮例外：用户随后明确要求“目前的向左跑向右跑没有做表情包，你尝试生成一下”。因此仅对左右跑增加了独立的生成试版，使用本样稿为人物参考；原图表情库和静态待机保持原素材。完整提示词见 `running-generation-prompts.md`。

参考：用户提供的黄小B原版截图（用户所述来源），本轮未独立核实作者归属。
源文件：用户上传的 `codex-clipboard-4171aa8c-4f7f-4875-81cc-beea2cc7cea6.png`（本机临时文件，不随仓库分发）

生成方式：内置 imagegen，以用户截图为唯一图像参考。

目标：保留布布在左、一二在右的形象、姿势、表情和比例；清除背景装饰、播放图标及右下角遮挡。

限制：遮挡部位为生成补绘；生成结果仍存在描边粗细和色彩渐变差异，不能视为逐像素或100%还原。

## 完整提示词

Edit the supplied screenshot into a clean, highly faithful character reference. This is an extremely conservative restoration of the EXACT two characters already visible, NOT a redesign or a generic bear drawing. Preserve the original drawing as closely as possible: Bubu the tan bear on the LEFT and Yier the white bear on the RIGHT. Preserve their exact poses, facial expressions, head silhouettes, face placement, body shapes, relative sizes, spacing, flat colors and dark cocoa outline thickness. Bubu has the same large slightly asymmetric rounded-square tan head, tan-rimmed dark inner ears, low small cocoa eyes, open pink tongue mouth, apricot cheeks, pudgy tan torso and feet, and original asymmetric curled arms. Yier has the same large rounded-square white head, solid dark round ears, low small cocoa eyes, tiny w mouth, pink cheeks, dark neck line and two rounded dark lobes beneath, white pudgy body and dark feet, original asymmetric arms. Do not symmetrize, beautify, add shading, add texture, change expression, move eyes, change head-to-body proportions, add eyelashes, flip sides or invent accessories.
Only remove the pale decorative flower shapes from the background, the translucent white video play icon overlapping Bubu's right head edge, and the orange/yellow foreground object at lower right. Restore the very small sections of character contours hidden by those overlays, following the adjacent original lines. Keep the original visible character drawing intact everywhere else. Plain pure white background, clean flat 2D sticker illustration. Both entire characters fully in frame with a small white margin. No text, no panels, no additional poses, no watermark. The priority is faithful visual identity from this exact supplied reference, not stylistic interpretation.
