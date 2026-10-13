# 开发与目录说明

## Windows 代码

主项目为 `windows/YierPet.Windows`（.NET 8 / WPF），沿用现有命名空间和文件布局。

| 模块 | 职责 |
| --- | --- |
| `App.xaml.cs` | 单实例、启动和退出 |
| `MainWindow.xaml(.cs)` | 单只桌宠、拖拽、走路、菜单与逐帧显示 |
| `PairWindow.cs` | 双人同框原表情显示 |
| `Core/DuoCoordinator.cs` | 独立活动、靠近入场、站位、追逐与同步播放 |
| `Core/StickerLibrary.cs` | 内置清单加载、帧缓存、移动稳定、可用素材查询 |
| `Core/UserStickerLibrary.cs` | 用户上传索引、资料覆盖、关联及菜单配置 |
| `Core/StickerImporter.cs` | GIF / PNG 解码、帧合成和图集保存 |
| `StickerLibraryWindow.xaml(.cs)` | 搜索、预览、停用及编辑入口 |
| `StickerEditorWindow.cs` | 上传与资料/关联编辑 |
| `Core/AppSettings.cs` / `ReminderService.cs` | 用户开关、健康提醒 |

`Sources/`、`Package.swift` 是上游 macOS 实现；`codex-pet/` 为独立的 Codex 宠物包，不参与 Windows 构建。

## 数据与素材

- `content/wechat/catalog*.json`：稳定编号、原文件哈希、角色、帧时长及图集位置。
- `interactions*.json`：状态映射、双人同框及跨素材搭配。
- `semantics.tsv`：人工审阅的动作与回应说明；`semantics.json` 为运行时版本。
- `spatial-review.json`：逐项左右位置判断；编排实现见 `tools/sticker_spatial.py`。
- `originals/` 保存来源字节；`compiled/`、`posters/`、`baseline/` 供运行时使用。
- `docs/art-direction/` 保存设计依据和历史验证结果。历史结果不代替修改后的重新验证。

安装目录的 `Content/wechat` 随程序发布。用户资料位于 `%APPDATA%/YierPet-Windows`，不会写回仓库；详见[用户说明](user-sticker-library.md)。

## 构建与检查

```powershell
dotnet build windows/YierPet.Windows/YierPet.Windows.csproj -c Release
python -m pip install -r tools/requirements.txt
python tools/validate_content.py
git diff --check
```

校验器只读检查原文件 SHA256、图集完整性与范围、时长、状态和互动引用、语义覆盖及左右规则，不下载或重写素材。它不能判断画面语义是否正确，也不替代桌面交互测试。

修改播放或编辑流程后，手动验证：左右走路循环、双方独立活动、固定左右/无关/追逐入场、GIF 上传与重启读取、多候选触发、菜单入口和停用恢复。临时冒烟工程及用户数据不提交。

## 素材工具

普通构建直接使用仓库内素材，无需重新下载。重新导入会重建清单，执行前先提交或备份人工修改。

| 工具 | 用途 |
| --- | --- |
| `import_wechat_stickers.py` | 下载指定文章原文件并编译帧图集 |
| `review_wechat_stickers.py` | 应用基础人工角色审阅及互动规则 |
| `import_getstickerpack.py` | 按已记录的 30 个 URL 导入补充包 |
| `prepare_motion_and_pack.py` | 应用补充包分类，恢复原图走路与语义规则 |
| `prepare_semantic_links.py` | 生成语义、互动与左右站位索引 |
| `review_sticker_semantics.py` | 生成多时点人工审阅联系表 |

完整重新导入顺序见 [素材说明](../content/wechat/README.md)。`review/` 联系表、已退役的生成走路图、本机部署记录、缓存和 `.tmp-*` 文件夹由 Git 忽略；仍被代码引用的兼容素材保留。

## 许可边界

项目代码沿用 [MIT](../LICENSE)。一二、布布形象及第三方表情素材的权利属于相应权利人，代码许可证不授予这些素材的版权或商用授权。素材来源和哈希保留在清单中。
