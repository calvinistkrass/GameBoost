# GameBoost

**Game mode and crosshair overlay for Windows 10/11** – one click for more performance and fewer distractions, plus a fully customizable crosshair on top of any game.

No installation, no admin rights, a single `.exe`.

![Crosshair editor](docs/screenshots/crosshair.png)

---

## Features

### 🎮 Game mode
- **One button** turns everything on and off.
- **Close background programs** – pick them with checkboxes from a list of running programs. Your selection is saved.
- **Built-in safety:** system, Windows, security and anti-cheat processes are never listed and never closed. Only programs of your own user account are considered.
- **“High performance” power plan** is activated; your previous plan is remembered and restored afterwards.
- **Windows notifications** are suppressed while you play.
- **Start programs** – e.g. launch Discord, Steam or a game launcher automatically.
- **Restart closed programs** when game mode is turned off (optional).
- **Profiles** for different games (e.g. “Valorant”, “Fortnite”).
- **Automation:** a profile turns on by itself as soon as the configured game `.exe` is running – and turns off when the game closes.
- **Crash-safe:** if your PC or GameBoost crashes, power plan and notifications are restored automatically on the next start.

### ✛ Crosshair overlay
- Styles: **cross, dot, circle, cross + dot, T-shape**
- Color, opacity, length, thickness, gap, dot size, outline (on/off, color, width), offset X/Y
- **Live preview** with zoom (1×/3×/6×)
- **Pixel-exact center** of the selected monitor – multi-monitor and Windows display scaling (DPI) are handled
- Always on top, **click-through**, not shown in the taskbar or Alt+Tab
- **Presets**: save, load, overwrite, delete
- **Share by code** (`GB1-…`) – copy it, send it to friends, import theirs

### ⌨️ Hotkeys & comfort
- Global, fully customizable hotkeys – they also work in-game
- Keeps running in the **system tray** when the window is closed
- Tray menu: game mode, crosshair, next preset, open, exit
- Optional **start with Windows**
- English UI (German available in Settings)

![Settings](docs/screenshots/settings.png)

---

## Download & run

1. Download the latest `GameBoost-x.y.z-win-x64.zip` from **[Releases](../../releases)**.
2. Unzip it and run `GameBoost.exe` – done.

> **Windows SmartScreen:** the `.exe` is not digitally signed, so on first launch Windows may show “Windows protected your PC”. Click **More info → Run anyway**.

**Requirements:** Windows 10 (1809+) or Windows 11, 64-bit. .NET does not need to be installed.

---

## Usage

| Area | How to |
|---|---|
| **Game mode** | Pick a profile → press the big button. The panel on the right shows what will happen / what happened. |
| **Close programs** | “Close programs” tab → tick the programs you want closed. Selected programs stay in the list even when they are not running. |
| **Start programs** | “Start programs” tab → “Add program” (`.exe` or shortcut). |
| **Automation** | “Automation” tab → enter the game's `.exe` name (Task Manager → “Details” shows it while the game runs) or pick it with “Browse…”. |
| **Crosshair** | “Crosshair” page → choose style and values → turn on “Show crosshair overlay”. |
| **Share** | Copy the code under “Your crosshair code”; paste codes from others under “Import code”. |

### Default hotkeys

| Action | Shortcut |
|---|---|
| Toggle crosshair | `Ctrl` + `Alt` + `C` |
| Toggle game mode | `Ctrl` + `Alt` + `G` |
| Next crosshair preset | `Ctrl` + `Alt` + `N` |

Change them under **Settings → Hotkeys**: click the field and press the new combination (`Del` = remove, `Esc` = cancel). If a combination is already used by another program, GameBoost tells you.

---

## ⚠️ Important notes

- **Borderless window:** the overlay is only visible when the game runs in **borderless window / windowed fullscreen** mode – **not in exclusive fullscreen**.
- **Anti-cheat:** games with strict anti-cheat (e.g. Valorant/Vanguard, FACEIT, ESEA) or tournaments may disallow overlays. **Use at your own risk.** GameBoost does not touch games (no injection, no memory access) – it only draws its own transparent window.
- **Closing programs:** unsaved data in closed programs is lost. Only select programs you don't need while gaming.
- **Notifications:** GameBoost turns off the Windows “Notifications” setting for your user. On some Windows versions this only applies to new notifications.
- **High performance on laptops:** if the “High performance” plan is missing, GameBoost creates a copy. If that isn't possible (some Modern Standby devices), the “Best performance” power mode is used instead.

---

## FAQ

**Does GameBoost need admin rights?**
No. Everything runs with normal user rights. Programs running as administrator can therefore not be closed.

**Where are my settings stored?**
In `%AppData%\GameBoost\settings.json`. Log files are in `%AppData%\GameBoost\logs`. Both can be opened from *Settings → Data & troubleshooting*.

**I can't see the crosshair.**
Set the game to borderless window mode, and pick the right screen under *Crosshair → Monitor*.

**How do I uninstall GameBoost?**
Turn off “Start with Windows”, quit GameBoost from the tray menu, then delete `GameBoost.exe` and the folder `%AppData%\GameBoost`.

---

## Build from source

Requirement: [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)

```powershell
# Run the tests and create the single .exe (output in .\publish)
powershell -ExecutionPolicy Bypass -File build.ps1
```

For development:

```powershell
dotnet build GameBoost.sln
dotnet test GameBoost.sln
dotnet run --project src/GameBoost
```

### Project structure

```
src/GameBoost/
├── App.xaml(.cs)          startup, single instance, global error handling
├── AppController.cs       wires services, tray, hotkeys and windows together
├── Models/                settings, profiles, crosshair, hotkeys
├── Services/              GameModeService, OverlayService, SettingsService, HotkeyService,
│                          TrayService, ProcessGuard (safety rules), PowerPlanManager, …
├── ViewModels/            MVVM view models for the three pages
├── Views/                 XAML UI + overlay window
├── Controls/              CrosshairRenderer, HotkeyBox, SliderRow
├── Infrastructure/        logger, localization (EN/DE), Win32 interop, converters
└── Themes/Theme.xaml      dark theme
tests/GameBoost.Tests/     xUnit tests
tools/make-icon.ps1        generates the app icon
website/                   landing page (static, deployable to Vercel)
```

**Tech:** C# 12, WPF, .NET 8, MVVM without external dependencies.

---

## Contributing

Found a bug or have an idea? Please open an issue and attach the log file from `%AppData%\GameBoost\logs`.
