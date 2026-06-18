<div align="center">

# 📸 Histshot

**Screenshots in one click — capture, annotate, save.**

A lightweight screenshot tool that lives in your system tray and is always one keypress away.
Hit `Print Screen` → select an area → draw an arrow → copy. Done.

Cross-platform, built with C# and [Avalonia UI](https://avaloniaui.net/).

[![.NET 8](https://img.shields.io/badge/.NET-8.0-512BD4?logo=dotnet&logoColor=white)](https://dotnet.microsoft.com/download/dotnet/8.0)
[![Avalonia UI](https://img.shields.io/badge/Avalonia-UI-8B5CF6?logo=avalonia&logoColor=white)](https://avaloniaui.net/)
[![Platform](https://img.shields.io/badge/platform-Windows%20%7C%20macOS%20%7C%20Linux-blue)](#-getting-started)
[![License: MIT](https://img.shields.io/badge/License-MIT-green.svg)](LICENSE)

</div>

---

## ✨ Features

- 🖥️ **Lives in the tray** — stays out of your way, can launch on system startup
- ⌨️ **Global hotkeys** — capture from anywhere, no need to switch to the app
- ✂️ **Area selection** — dims the screen so you can pick exactly what you want
- ✏️ **Built-in editor** — pencil, line, arrow, text
- 🎨 **Customizable tools** — color, line thickness, font size
- 📋 **Copy or save** — straight to the clipboard or to a file on disk
- 🕘 **Screenshot history** — every capture is kept, nothing gets lost
- 🌍 **Two languages** — English and Russian

---

## 📷 Screenshots

> _Replace these placeholders with real screenshots or a short GIF demo._

<div align="center">

| Area selection | Editor |
| :---: | :---: |
| <img src="docs/screenshot-capture.png" alt="Area selection" width="400"/> | <img src="docs/screenshot-editor.png" alt="Editor" width="400"/> |

</div>

---

## 🚀 Getting Started

### Requirements

- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) or newer
- Windows, macOS, or Linux

> **Note:** screen capture is currently fully implemented for **Windows** only.
> macOS and Linux still need a capture provider (see [below](#-for-developers)).

### Run it

```bash
# build
dotnet build

# run
dotnet run --project src/Histshot/Histshot.csproj
```

That's it — the icon shows up in your tray. 🎉

---

## 🎮 How to Use

| Action | Hotkey (default) |
| --- | --- |
| Take a screenshot | `Print Screen` |
| Quick save | `Shift + Print Screen` |

Hotkeys, autostart, and language can all be changed in **settings** (right-click the tray icon).

**Typical flow:**

1. Press `Print Screen`
2. Drag to select the area you want
3. Optionally draw an arrow, add text, or underline something
4. Copy to the clipboard (`Ctrl + V` anywhere) or save it as a file

Every capture is automatically added to your **history** — nothing is lost, even if you forget to save.

---

## 📂 Where Things Live

Screenshot history is stored here:

```
%LocalAppData%\Histshot\History
```

(on Windows, usually `C:\Users\YourName\AppData\Local\Histshot\History`)

---

## 🛠️ For Developers

### Project structure

```
src/
├── Histshot/          → desktop app (Avalonia: windows, tray, hotkeys)
└── Histshot.Core/     → core: models, drawing, screen capture, history
tests/
└── Histshot.Core.Tests/  → unit tests
```

### Tests

```bash
dotnet test
```

### Want to help with macOS / Linux?

Screen capture is platform-specific code, and only the Windows implementation is ready.
To add another OS, implement a provider in `Histshot.Core.Capture`
(see the `IScreenCaptureProvider` interface and the `WindowsScreenCapture` example).

PRs and ideas are welcome! 🤝

---

## 📄 License

Released under the [MIT License](LICENSE) — free to use, modify, and distribute.

---

<div align="center">
<sub>Made with ❤️ using C# and Avalonia UI</sub>
</div>
