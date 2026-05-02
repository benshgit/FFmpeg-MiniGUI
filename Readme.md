# Minimalist FFmpeg Tactical Exoskeleton (极简 FFmpeg 战术外骨骼)

A lightweight, purely command-line driven GUI wrapper for FFmpeg, written in C#. Born for the ultimate pure command-line experience. 
为极致纯粹的命令行体验而生的——轻量、原生、极具战术感的 FFmpeg 批处理图形界面。

## ✨ Features | 核心特性

* **Dark Mode UI:** Fully customized dark UI using native Windows API painting, avoiding the clunky standard WinForms look. Features immersive DWM dark title bars. (深度定制的暗黑主题与原生 DWM 标题栏)
* **Process-Level Pause/Resume :** Utilizes `ntdll.dll` (`NtSuspendProcess`/`NtResumeProcess`) to magically pause and resume FFmpeg tasks instantly. (进程级控制)
* **Smart Batch Processing:** Drag and drop multiple media files instantly. Processes a queue of inputs sequentially with zero friction.
 (支持多文件拖放与队列批处理)
* **Defense Mechanism:** Automatically injects timestamps into output filenames to prevent accidental overwriting of your source files. (智能防覆盖机制，自动为输出文件植入时间戳)
* **Multi-language Support:** Built-in 5 languages (English, 简体中文, 日本語, Français, Deutsch) with dynamic switching. (内置五国语言，一键无缝切换)
* **No Dependency Hell:** Single executable, minimal API calls, purely invokes local `ffmpeg.exe`. (绿色单文件，纯原生 API，无第三方依赖)

## 🚀 Quick Start | 快速开始

1. Provide the full path to your local `ffmpeg.exe` in the first input box. (指定 ffmpeg.exe 的路径)
2. Drag and drop your audio/video files into the input box.  (将音视频文件拖入输入框)
3. Specify your output directory (or leave blank to save in the source folder). （指定输出路径）
4. Enter your raw FFmpeg arguments (e.g., `-c:v libx264 -crf 23`). (输入纯粹的 FFmpeg 参数)
5. Click the **Run (▶)** button and enjoy the tactical execution.

## 🛠 Requirements (系统要求)
* Windows OS (Windows 10/11 recommended for full DWM dark mode features)
* .NET Framework 4.x / .NET 6.0+ (Windows Forms)
* FFmpeg executable

## 📜 License | 开源协议

This project is licensed under the **GNU General Public License v3.0** (GPL-3.0) - see the LICENSE file for details.
本项目采用 GPL v3 协议开源。