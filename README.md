# Jelling Battery Monitor

Battery levels of your Bluetooth devices, one click away in the Windows 11 taskbar.

Jelling Battery Monitor puts a Bluetooth icon in the notification area. Click it and a flyout lists every paired device with its type and charge, styled like the Windows 11 quick settings. Devices that are switched off or out of range stay in the list, dimmed, with their last known level.

**[Download for Windows 11 (x64)](https://github.com/Gitsko/jelling/releases/latest/download/Jelling-win-x64.zip)** · [ARM64](https://github.com/Gitsko/jelling/releases/latest/download/Jelling-win-arm64.zip) · [all releases](https://github.com/Gitsko/jelling/releases)

![The flyout lists the paired devices with their battery level and warning threshold](assets/readme/flyout.png)

Pinned devices sit in the taskbar next to the notification area, here a headset at 100 % and a keyboard at 50 %:

![Two pinned devices in the Windows 11 taskbar](assets/readme/taskbar.png)

With "Show battery level as number in taskbar" turned off, only the symbols remain:

![The same two devices as symbols only](assets/readme/taskbar-symbols.png)

The screenshots show the German interface; on an English Windows the app is in English.

## Features

- Flyout with name, device type and battery level; the list updates by itself when a device connects, disconnects or reports a new level
- Devices in the taskbar: the pin button next to a device ("Show in taskbar") shows it in the taskbar while it is connected, left of the overflow arrow: the device symbol with its battery level to the right, black or white like the Windows icons, the level in red once it reaches the device's warning threshold. Hovering a device shows its name and level, clicking opens the flyout
- "Show battery level as number in taskbar" in the right-click menu: turned off, the taskbar shows only the symbols, and a symbol turns red at the warning threshold. The tooltip still names the level
- When the taskbar has no room left of the overflow arrow, or is vertical, the pinned devices get their own icons in the notification area instead. Jelling puts them in the taskbar rather than behind the overflow arrow; if you hide them under Settings > Personalization > Taskbar > Other system tray icons, they stay hidden. Windows remembers both as long as Jelling stays in the same folder
- Warning threshold per device: the "Warn at" list in the flyout sets the level (default 20 %, or Off) at which the device's level turns red and a notification appears. It warns once per discharge and again only after the device was charged more than 10 points above the threshold. Clicking it opens the flyout
- Start with Windows: right-click the icon. Exit is in the same menu
- Starting Jelling a second time opens the flyout of the running app instead of adding a second icon
- Light, dark and high-contrast taskbars
- English and German, following the Windows display language; other languages get English
- The flyout's bottom right shows the version and a link to this page. When a newer release is out, a small download arrow appears next to them and opens the releases page; Jelling never downloads or installs anything by itself

## Which devices show a level

Jelling shows the level Windows itself reads, the same value you see under Settings > Bluetooth & devices. Headsets, mice, keyboards and controllers that report their charge there work. A device without a level there shows "–" in Jelling as well.

## Install

Requires Windows 11 (x64 or ARM64). Nothing else: .NET and the Windows App SDK are inside the ZIP.

1. Download [`Jelling-win-x64.zip`](https://github.com/Gitsko/jelling/releases/latest/download/Jelling-win-x64.zip) (or [`Jelling-win-arm64.zip`](https://github.com/Gitsko/jelling/releases/latest/download/Jelling-win-arm64.zip)) from the latest release
2. Unzip it to a folder that stays, for example `%LOCALAPPDATA%\Programs\Jelling`. If you move the folder later, turn "Start with Windows" on again
3. Run `Jelling.App.exe`

The exe is not code-signed, so SmartScreen may warn on the first start. Choose "More info" and then "Run anyway". The warning does not appear if you open the ZIP's properties and tick "Unblock" before unzipping. With Smart App Control turned on, Windows blocks unsigned apps like this one and offers no "Run anyway".

To update, exit Jelling from the right-click menu, then replace the folder's contents with the new ZIP. Keep the folder where it is so autostart and the taskbar positions stay.

To uninstall, turn off "Start with Windows", exit the app and delete its folder. Settings (`settings.json`) and the error log (`crash.log`) live in `%LOCALAPPDATA%\Jelling`; delete that folder too.

## Privacy

Jelling reads the battery levels from Windows and keeps everything on your PC. The only network access is the update check: a minute after start and then once a day it asks the GitHub API (`api.github.com/repos/Gitsko/jelling/releases/latest`) for the newest version number. The request carries the app version in its User-Agent and nothing about your devices.

## Build from source

Needs the .NET 10 SDK on Windows.

```
dotnet build Jelling.slnx
dotnet test Jelling.slnx --no-build
```

A self-contained build like the release (for ARM64 use `-r win-arm64 -p:Platform=ARM64`):

```
dotnet publish src/Jelling.App/Jelling.App.csproj -c Release -r win-x64 -p:Platform=x64 -o artifacts/publish/win-x64
```

Pushing a tag like `v1.2.0` builds both ZIPs in GitHub Actions and attaches them to a release.

## Not planned

- Vendor protocols (Logitech, Sony, Bose): each needs its own reverse engineering and test hardware
- AirPods: Apple only exposes estimates in proprietary advertisements
- Connecting or disconnecting devices: Windows already does that well
- A widget for the Windows Widgets board: it cannot refresh often enough

## License

MIT, see [LICENSE](LICENSE).
