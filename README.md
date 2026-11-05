# Performance — Windows Gaming Monitor

A Windows 10/11 desktop monitor with a GamerLoop-inspired dark glass interface, a non-interactive in-game Dynamic Island overlay, hardware telemetry, process management, TCP connection inspection, installed-app uninstall review, and storage analysis.

## Build

Open `Performance.sln` in Visual Studio 2022, or run `dotnet publish Performance/Performance.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o publish` on Windows with the .NET 8 SDK.

The GitHub Actions workflow builds a portable self-contained Windows x64 executable and publishes it as a workflow artifact and GitHub Release asset.

## Notes

- FPS is measured with PresentMon. The release workflow bundles the upstream PresentMon command-line executable; ETW capture may require running Performance as Administrator and some protected/anti-cheat titles may block capture.
- CPU/GPU temperatures depend on sensor availability and may require Administrator privileges. NVIDIA GPU metrics use `nvidia-smi` when available.
- The overlay is click-through while a full-screen game is focused; it cannot be guaranteed above exclusive-fullscreen titles. Borderless fullscreen is recommended.
- Uninstalling software runs its registered uninstaller. Residual folders are shown for review and are never deleted automatically.
- TCP connections are shown with local/remote IP and port. UDP endpoints do not have a general remote destination table in Windows.
