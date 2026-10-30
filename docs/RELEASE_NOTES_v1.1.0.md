## GameBoost 1.1.0 – Performance overlay, PC check & new design 🚀

### New
- **In-game performance overlay** – FPS and frame time, GPU load and **GPU temperature**, VRAM, CPU and RAM always in view on top of your game. Choose the corner, size, background and which values to show. Toggle it with `Ctrl+Alt+H`, or let it appear automatically with game mode.
  - GPU temperature works for NVIDIA, AMD and Intel without extra drivers (same source as Task Manager).
  - FPS counting uses Windows event tracing and needs administrator rights – one click on “Restart as administrator”. Supports DirectX 9–12.
- **PC check** – analyzes your hardware and the Windows settings that matter for gaming, gives your PC a score and optimizes with one click (every change can be undone):
  - Windows Game Mode, background recording, mouse acceleration, refresh rate, optimizations for windowed games, power plan
  - Hints for GPU scheduling, outdated graphics drivers, RAM and free disk space
  - **Recommended in-game settings** matched to your GPU, monitor and memory (preset, textures, shadows, upscaling, Reflex/Anti-Lag, FPS limit …)
- **New dashboard** – quick toggles for game mode, crosshair and overlay, live system stats, your PC score and hardware at a glance.

### Improved
- Completely redesigned interface: new navigation, cleaner cards, smooth page transitions.
- Tray menu and hotkeys for the performance overlay.
- The Valorant example profile is no longer included (an unchanged existing one is removed automatically).

### Download
Download **`GameBoost-1.1.0-win-x64.zip`**, unzip it and run `GameBoost.exe`. No installation, no admin rights needed (only for the optional FPS counter).

> Windows SmartScreen warns on first launch because the `.exe` is not signed → **More info → Run anyway**.

### Notes
- Overlays are only visible in **borderless window** mode, not in exclusive fullscreen.
- Games with strict anti-cheat may disallow overlays – **use at your own risk**. GameBoost never touches game files or game memory.
