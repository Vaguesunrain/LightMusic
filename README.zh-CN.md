# LightMusic

[English](README.md) | 简体中文

一款轻量、美观、支持状态栏桌面歌词的 Windows 本地音乐播放器，对音乐文件的内嵌封面与 LRC 歌词提供良好支持。

## ✨ 核心特性

- **极致轻量与低占用**：程序本体与依赖文件总计不到 **30 MB**（其中轻量化的 `libmpv-2.dll` 核心解码库仅占 12.5 MB），常规播放时内存占用稳定在 **80 – 105 MB**。
- **Material You 动态取色**：主界面采用 Material You 设计风格，底部播放控制栏与状态栏悬浮歌词的强调色会自动提取并跟随当前专辑封面色彩动态变化。
- **沉浸式极简播放页**：播放界面采用现代极简主义布局，支持自由更换全屏自定义高清背景壁纸，并内置 4 种可切换的动态进度条线型。
- **状态栏桌面歌词**：支持悬浮在桌面/任务栏区域的实时滚动歌词，可自由调节字体、字号、透明度、颜色进度过渡及屏幕坐标偏移。
- **双语支持**：内置简体中文与 English 界面，支持一键实时无刷新切换。

## 📦 安装与运行说明

前往 [Releases](../../releases) 页面下载最新版本：

- **框架依赖版（Framework-dependent）**：体积更小（< 30 MB），运行前需确保系统已安装 **.NET 8.0 Desktop Runtime**。
- **系统托盘模式**：支持一键收起到系统托盘，自动释放高分辨率封面缓存以进一步降低后台内存占用。

## 🛠️ 技术栈

- **界面框架**：WPF (.NET 8.0) + MaterialDesignInXamlToolkit
- **音频引擎**：`libmpv-2.dll`（支持 FLAC、MP3、AAC、WAV、APE、ALAC 等主流无损与有损格式）
<img width="1087" height="720" alt="image" src="https://github.com/user-attachments/assets/f1c85989-0d9d-4b68-a049-9dc769d499e1" />
<img width="1093" height="721" alt="image" src="https://github.com/user-attachments/assets/22f21c95-a366-42b3-8ee5-01a2e310815f" />
<img width="570" height="45" alt="image" src="https://github.com/user-attachments/assets/0d25ebc7-c563-4f83-9ade-20b6432e7528" />
