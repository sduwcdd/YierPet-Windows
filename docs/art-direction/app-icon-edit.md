# 应用图标

- 输入：用户提供的 codex-clipboard-aea2e099-cce3-4db8-9b1f-7ef24c7ac41e.png。
- 使用内置 imagegen 编辑工具移除背景（未使用 CLI）；第二轮要求纠正第一轮新增的阴影。
- 透明 PNG：`windows/YierPet.Windows/Assets/app-icon.png`。
- Windows ICO：`windows/YierPet.Windows/Assets/app-icon.ico`，保持比例居中，仅进行格式转换和缩放，包含 16、20、24、32、40、48、64、128、256 像素尺寸。
- 图标通过 ApplicationIcon 编入 EXE，窗口使用 PNG，快捷方式指向安装目录的 ICO。
- 局限：生成式背景移除仍改变了部分线条及填色，不是逐像素保留的原图抠图。

## 最终编辑提示词

Edit target: the FIRST image (the original user image with dashed background). SECOND image is only an example of the desired transparent cutout, but it introduced unwanted shading. Make a faithful background-only cutout of the FIRST image: keep its original flat solid fills, original crisp dark-brown lines, exact faces, exact mouth shapes, original small bodies and existing hugging pose. Remove the beige dashed grid, yellow sparkles, pink decoration, orange corner, and every background mark. Only the two characters remain on real transparent alpha. Yier's white interior stays opaque. IMPORTANT targeted correction: absolutely no gradients, shading, texture, glows, airbrushing or 3D volume anywhere on characters; the supplied original has simple flat colors. Do not reinterpret or beautify the characters. Keep full feet and ears and a small transparent margin. No new text, background, shape or icon badge.
