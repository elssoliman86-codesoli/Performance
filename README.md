# PERFORMANCE — Windows Gaming System Monitor

A native Windows 10/11 desktop utility with a GamerLoop-inspired dark-glass interface and a Dynamic Island-style live overlay.

## Features in this build

- Live CPU usage, CPU package temperature when exposed by firmware, RAM consumption, system-drive usage, NVIDIA GPU usage/temperature/VRAM through `nvidia-smi`, and hardware sensor fallback through LibreHardwareMonitor.
- Full-screen game session timer and FPS sampling through PresentMon.
- Process manager with PID, working-set RAM, sampled CPU percentage, session type and executable path; filter and end a selected user process.
- TCP connection table with owning process, local IP:port, remote IP:port and state.
- Installed-app inventory from Windows uninstall registry entries; launches the application's registered uninstaller.
- Residual-folder suggestions in user app-data locations. Folders must be selected and confirmed individually; nothing is removed automatically.
- Recursive storage scan for a chosen drive/folder; top 160 largest files and categories for images, video, audio, archives, installers, games/apps and other files.
- Compact top-centre overlay. It becomes click-through while a full-screen game is foregrounded; click it on the desktop to reopen the control panel. Close hides the dashboard to the system tray; choose Exit from the tray menu to stop the monitor.

## Download

Each successful push to `main` builds a self-contained `Performance.exe` for Windows x64 and publishes it to [GitHub Releases](https://github.com/elssoliman86-codesoli/Performance/releases). A GitHub Actions artifact is also attached to each build.

## Run

1. Download `Performance.exe` from the latest release.
2. Run it on Windows 10/11 x64. For the fullest sensor and frame-capture access, right-click and choose **Run as administrator**.
3. The small island stays on the desktop. Click it to open Performance; click × to hide it to the tray.
4. While gaming, use borderless fullscreen for the best chance of seeing the island.

## Accuracy and safety notes

- FPS is sampled from PresentMon frame events, not guessed from GPU utilization. ETW access can require elevation; protected/anti-cheat games may deny capture. Some exclusive-fullscreen modes can cover any ordinary desktop overlay.
- Temperature sensors depend on BIOS/EC support and access permissions. If a value is unavailable, it is shown as unavailable rather than fabricated. NVIDIA readings can fall back to `nvidia-smi`.
- The process table includes background processes, not only visible windows. Ending a process can lose unsaved work; Windows-protected/system-session entries are guarded.
- The network screen currently reports TCP endpoints; it does not claim to map a remote destination for every UDP flow.
- App removal is delegated to the registered Windows uninstaller. Residual folders are only candidates and are restricted to app-data roots; review the path and confirm deletion manually. This is not a substitute for the vendor uninstaller.
- The storage scanner only inspects the folder/drive the user selects. It lists large files; it does not automatically delete files or clear browser/game caches.

## Build locally

Install the .NET 8 SDK and Visual Studio 2022 (Desktop development with .NET). On Windows:

```powershell
dotnet publish Performance/Performance.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o publish
```

The CI workflow downloads PresentMon v2.6.0 from its official release and verifies its SHA-256 before embedding it into the application. PresentMon is third-party software; see [GameTechDev/PresentMon](https://github.com/GameTechDev/PresentMon) for upstream notices. LibreHardwareMonitor is distributed under MPL-2.0.
