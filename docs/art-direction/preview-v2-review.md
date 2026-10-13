# 一二和布布静态形象 V2：等待用户确认

本轮范围：只绘制一张静态形象样稿，用户确认还原度后再继续动画与程序开发。不得将样稿自动安装到桌宠。

用户新增参考方向：黄小B抖音作品，以及微信中的一二布布表情包和二创；未来用于动作参考，保留来源区分。

## 参考获取情况

- 用户提供的抖音短链接 https://v.douyin.com/DbhRdimcDJY/ 已在浏览器解析到“黄小B（一二和布布）”主页；作品列表本轮出现登录限制/服务异常，未完整查看。
- 本轮实际用于图像参考的是公开转载合集 https://getstickerpack.com/stickers/xiao-xiong-yi-er-he-bu-bu-credits-to-huang-xiaob ，页面标注 Credits to 黄小B。它不是微信官方商店，也未据此断言所有图都是作者原作。
- 选取该合集 #9、#11、#18、#21，分别核对一二脸型、颈部标志、布布身体和一二站姿。参考图保存在 reference-v2 子目录，只作为设计参考。
- 未直接读取用户微信或核验当前微信表情商店目录。

## 样稿

文件：preview-v2.png。内置 image_gen 生成，一张白底双角色静态图。用户未确认，不表示已经还原成功。本轮未修改运行代码、安装目录或动画素材。

## 实际生成提示词

```text
Use case: identity-preserve / faithful reference redraw.
Asset type: ONE static character approval sheet, not animation, not a sprite grid.
The four attached images are the ONLY visual source of truth. Redraw precisely these existing 一二 (Yier, white) and 布布 (Bubu, tan) characters as clean high-resolution 2D sticker drawings. The client rejected an earlier generic chibi redesign. This must look like the SAME CHARACTERS as the supplied stickers, not an interpretation or mascot redesign.

REFERENCE ROLES:
Image 1: Yier head silhouette, small low facial features, round ears and long soft body.
Image 2: Yier neck marking and soft full-body construction.
Image 3: Bubu's exact peach-tan color, thin dark-brown outline, broad head shape, tiny eyes, cheek positions, plump body and fused short legs. Remove only the worried eyebrows; expression should be a quiet neutral little w smile.
Image 4: Yier's complete normal standing proportions and small hands/feet, and size of its tiny dark neck marking.

DRAW ONE PAIR ONLY: Yier on the left and Bubu on the right, both front-facing, whole bodies including feet, calmly standing with arms relaxed beside the body, separated by generous white space. Bubu may be only about 5 percent taller. Plain PURE WHITE background, landscape composition, generous margins, no scene, no accessories, no text or titles, no graphics, no extra poses. Make the characters large enough for the user to inspect the faces.

CRITICAL GEOMETRY:
Copy the broad softly squared head contour from the stickers, especially the flatter lower cheek/chin edge. Gently domed broad top, NOT a spherical head or perfectly round teddy head. The dark-brown outline should be thin and slightly organic, about 1 to 1.3 percent of head width, NOT heavy bold vector outlines. The head is approximately 56 to 60 percent of the total figure height, NOT an enormous 75 percent bobblehead over a tiny body. There is a substantial soft pudgy torso beneath the head, small sausage arms and a continuous lower body flowing into tiny rounded feet, no detailed fingers or paws. No separated neck, shoulders, oversized head bow or clothing.
The eyes are VERY SMALL solid cocoa dots, only roughly 4 percent of head width, with NO shine. They sit LOW in the head with wide empty forehead. Place them and the tiny w mouth as in the supplied reference, do not enlarge to enhance cuteness. Cheeks are flat simple pastel circular patches touching the lower outer cheek area, pink on Yier, muted light apricot yellow on Bubu. Mouth is very small and simple, not a large cat smile. Yier has solid cocoa round ears and a tiny dark neck mark matching image4; white body with small dark foot accents exactly like the reference. Bubu has tan ears with dark centers and tan rims, uninterrupted tan neck and tan feet, no bow.
Use solid flat white and peach tan fills directly matching the supplied stickers. NO gradient, no shading, no texture, no glossy highlights, no colored rim light, no blush gradients, no 3D effect, no plush rendering, no anime eyes, no added eyebrows. Do not stylize, beautify, exaggerate, or re-proportion. The task is recognizability and fidelity to the attached stickers, not novelty.
```
