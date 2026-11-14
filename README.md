# LightMusic

English | [简体中文](README.zh-CN.md)

A lightweight, clean local music player for Windows featuring taskbar/desktop lyrics, with native support for embedded album artwork and LRC lyrics.

## ✨ Features

- **Ultra-Lightweight Footprint**: Total disk footprint is under **30 MB** (including the stripped-down `libmpv-2.dll` audio core at 12.5 MB). Runtime memory usage stays around **80 – 105 MB**.
- **Material You Dynamic Theming**: Built with Material You design principles—both the bottom playback bar and the floating taskbar lyrics dynamically adapt their accent colors to the current album cover.
- **Minimalist Now Playing View**: A modern, distraction-free playback screen supporting custom high-resolution background wallpapers and 4 switchable expressive progress bar styles.
- **Floating Taskbar Lyrics**: Real-time synchronized lyrics overlay with customizable font family, size, capsule opacity, karaoke color progress, and X/Y screen offset.
- **Bilingual UI**: Supports instant, hot-swappable switching between English and Simplified Chinese.
-  New: You can now add songs to different collections and assign unique covers and backgrounds to each.
## 📦 Installation & Requirements

Download the latest build from the [Releases](../../releases) page:

- **Self-Contained Build**: Bundles the runtime—ready to run out of the box with no extra setup.
- **Framework-Dependent Build (< 30 MB)**: Requires the **.NET 8.0 Desktop Runtime** installed on Windows.
- **Low-Memory Tray Mode**: Minimize to the system tray to automatically release high-res cover caches while keeping background playback and taskbar lyrics active.

## 🛠️ Built With

- **UI Framework**: WPF (.NET 8.0) & MaterialDesignInXamlToolkit
- **Audio Engine**: `libmpv-2.dll` (supports FLAC, MP3, AAC, WAV, APE, ALAC, and other lossless/lossy formats)
<img width="1087" height="720" alt="image" src="https://github.com/user-attachments/assets/f1c85989-0d9d-4b68-a049-9dc769d499e1" />
<img width="1089" height="723" alt="image" src="https://github.com/user-attachments/assets/58bcfc12-e906-4703-86e0-bc20ed98ead9" />
<img width="1093" height="721" alt="image" src="https://github.com/user-attachments/assets/22f21c95-a366-42b3-8ee5-01a2e310815f" />
<img width="570" height="45" alt="image" src="https://github.com/user-attachments/assets/0d25ebc7-c563-4f83-9ade-20b6432e7528" />

