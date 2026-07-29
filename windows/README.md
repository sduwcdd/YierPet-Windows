# YierPet Windows 移植版

这是 [YierPet](../README.md) 的 Windows 原生实现，使用 .NET 8 WPF 编写。它与原 macOS 代码相互独立，复用同一套 8×9 精灵图和动作时序。

## 环境要求

- Windows 10 或 Windows 11（x64）
- .NET 8 SDK：开发与构建时需要
- 运行发布后的 self-contained 版本时不需要额外安装 .NET

## 构建与运行

在仓库根目录运行：

~~~powershell
.\windows\build-windows.ps1 -Run
~~~

生成可分发的单文件版本：

~~~powershell
.\windows\build-windows.ps1 -Publish
~~~

发布结果在 windows\artifacts\win-x64\，启动 YierPet.Windows.exe 即可。退出应用请右键桌宠并选择“退出”。

## 使用

| 操作 | 效果 |
| --- | --- |
| 左键单击 | 挥手打招呼 |
| 左键拖拽 | 移动桌宠，按拖动方向奔跑 |
| 快速甩出 | 重力、反弹与受击反馈 |
| 右键 | 切换动作、暂停随机行为、开关提醒、退出 |
| Alt + 右键 | 额外显示“测试提醒”菜单 |

## Windows 功能

- 透明、无边框、始终置顶且不显示任务栏图标
- 原图集的 9 种动画：待机、奔跑、挥手、跳跃、沮丧、等待、工作中、审阅
- 7–15 秒一次的随机行为
- 久坐、喝水、深夜、已知视频播放器前台停留提醒
- CPU 高占用、内存压力、电量和系统盘空间提醒
- 提醒与随机行为开关保存在 %APPDATA%\YierPet-Windows\settings.json

## 隐私与限制

程序使用公开 Windows API 获取本机输入空闲时长、前台进程名和系统指标；不申请屏幕录制或辅助功能权限。为避免读取浏览器内容，它不会判断浏览器内的具体视频网站；“摸鱼检测”只针对已知的视频播放器进程。
