# 开发说明

## 环境与常用命令

Windows 10/11 x64 + .NET 8 SDK。Python 仅用于素材开发和校验，普通用户不需要安装。

```powershell
dotnet build windows/YierPet.Windows/YierPet.Windows.csproj -c Release
python -m pip install -r tools/requirements.txt
python tools/validate_content.py
.\windows\publish-release.ps1 -Version 0.0.1
```

`build-windows.ps1 -Run` 构建并运行；`-Publish` 调用完整 EXE 发布脚本。`install-windows.ps1` 为开发机安装框架依赖版本，需 .NET 8 Desktop Runtime，并创建桌面及开机启动快捷方式。

## 应用结构

| 文件 | 职责 |
| --- | --- |
| `App.xaml.cs` | 单实例、启动及退出；发布验证入口 |
| `MainWindow.xaml(.cs)` | 单只角色、鼠标操作、逐帧显示与右键菜单 |
| `PairWindow.cs` | 双人同框的完整原表情 |
| `Core/DuoCoordinator.cs` | 独立活动、靠近入场、左右站位、追逐和同步播放 |
| `Core/StickerLibrary.cs` | 内置清单、图集缓存、原图走路稳定 |
| `Core/UserStickerLibrary.cs` | 个人素材索引、资料覆盖、关联及菜单配置 |
| `Core/StickerImporter.cs` | GIF disposal/帧偏移合成、PNG 及图集保存 |
| `StickerLibraryWindow.xaml(.cs)` | 搜索、预览、启停与编辑入口 |
| `StickerEditorWindow.cs` | 上传、资料及候选关联编辑 |
| `Core/AppSettings.cs` / `ReminderService.cs` | 用户开关与健康提醒 |
| `Core/ReleaseVerifier.cs` | 独立 EXE 内的只读素材验证和隔离数据读写检查 |

旧 macOS、Codex 原生宠物包、生成动画类及其图集已移除。角色枚举位于 `Core/PetCharacter.cs`。当前运行仅依赖 `content/wechat` 原图清单及基准图。

## 素材开发

`catalog*.json` 记录稳定 ID、来源哈希、帧时长和图集；`interactions*.json` 记录状态映射、双人场景和单人联动；`semantics.tsv` 为人工说明源，生成后的 `semantics.json` 供界面使用。左右审阅见 `spatial-review.json` 和 `tools/sticker_spatial.py`。

`originals/` 保留原文件；`compiled/`、`posters/`、`baseline/` 供运行时读取。`docs/art-direction/preview-v3.png` 仍被基准图裁切工具引用，因此保留；`getstickerpack-urls.json` 是补充包导入输入；逐项语义表也是工具输出，均不属于废弃草稿。

两个空的 `*.generated-motion.json` 是旧安装升级兼容清单：它们覆盖旧生成走路入口，不加载任何生成素材。不要在旧版覆盖升级中直接省略这两个文件。

正常构建无需下载素材。重新导入会覆盖生成清单，操作前应提交人工修改；顺序见 [素材说明](../content/wechat/README.md)。

`validate_content.py` 只读校验原文件哈希、图集完整性、帧时长、角色及互动引用、语义覆盖和左右规则，不判断人类动作语义。

## 完整 EXE 与发布验证

`publish-release.ps1` 使用 .NET 的 self-contained / single-file 发布，将运行时及 Content 一并打包。启用 `IncludeAllContentForSelfExtract`，启动前解包后通过 `AppContext.BaseDirectory` 读取素材；机制见 [微软单文件部署文档](https://learn.microsoft.com/en-us/dotnet/core/deploying/single-file/overview)。

脚本只将 EXE 复制到独立临时目录，调用 `--verify-release <报告路径>`。该入口不占用正常桌宠单实例、不读取用户上传目录，检查 522 项素材、225 条规则、所有首末帧、走路帧、默认菜单和隔离的用户数据保存/重载。临时测试文件退出后删除。

输出：

- `YierPet-Windows-v0.0.1-win-x64.exe`：供用户直接运行。
- `SHA256SUMS.txt`：下载文件哈希。
- `verification.json`：发布包验证结果；不等同于所有桌面交互的自动化测试。

修改协调器或界面后，仍需测试拖拽、单人独立活动、左右入场、追逐、上传预览、多个候选及停用恢复。

## GitHub Release

提交并推送 main 后，创建并推送版本标签：

```powershell
git tag -a v0.0.1 -m "Release 0.0.1"
git push github v0.0.1
```

新克隆通常只有 `origin`，请将命令中的 `github` 换成自己的远程名。`.github/workflows/release.yml` 在 Windows 上重新构建和验证，成功后通过仓库内置 `GITHUB_TOKEN` 创建 Release 并上传 EXE、哈希和报告。已有同名 Release 会停止，避免覆盖已发布文件。权限依据见 [GitHub GITHUB_TOKEN 文档](https://docs.github.com/en/actions/tutorials/authenticate-with-github_token)。

发布说明存放在 `docs/releases/<版本>.md`。当前正式目标为 Windows x64；未签名，不提供 ARM64 原生构建。
