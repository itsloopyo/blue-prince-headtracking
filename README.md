# Blue Prince Head Tracking

![Blue Prince running with this mod](https://raw.githubusercontent.com/itsloopyo/blue-prince-headtracking/main/assets/readme-clip.gif)

An unofficial head tracking mod for Blue Prince that moves the view with your head while your mouse or controller keeps control of look and interaction, driven by a webcam, phone, or any OpenTrack compatible tracker, with no VR headset required.

> **Settings have moved.** This version keeps its settings in `BepInEx\config\CameraUnlock.ini`.
> The first time it starts it reads your settings from the old
> `BepInEx\config\com.cameraunlock.blueprince.headtracking.cfg` into the new file, and leaves the
> old file as it was. BepInEx's ConfigurationManager no longer lists the settings: edit
> `CameraUnlock.ini` with any text editor. [Configuration](#configuration) has the details.

## Features

- **Decoupled look and aim** - head tracking moves the view, your mouse or controller keeps the aim
- **6DOF tracking** - yaw, pitch and roll plus positional lean, peek and duck
- **Works with any OpenTrack compatible tracker** - free options available for PC, iOS and Android

## Requirements

- [Blue Prince](https://store.steampowered.com/app/1569580/Blue_Prince/) on Windows. Built and tested against the Xbox Game Pass build, package version 1.1.11.0, installed at `C:\XboxGames\Blue Prince\Content`. The Steam and Epic builds have not been run here.
- A tracking source: [OpenTrack](https://github.com/opentrack/opentrack/releases) with a webcam, a phone app, or any other tracker that sends the OpenTrack UDP protocol.
- Windows 10 or 11, 64-bit.

BepInEx 6 (IL2CPP) is bundled with the installer and set up for you; there is nothing to download separately.

The first launch after installing takes noticeably longer than usual. BepInEx generates its interop assemblies from the game's IL2CPP metadata once, and downloads a set of Unity reference libraries to do it, so that launch needs a working internet connection. Every launch after it is normal speed.

## Installation

### Lopari

Download [Lopari](https://lopari.app), choose **Blue Prince**, and click
**Play with head tracking**.

### Standalone Installer

1. Download `BluePrinceHeadTracking-vX.Y.Z-installer.zip` from the [Releases page](https://github.com/itsloopyo/blue-prince-headtracking/releases).
2. Extract it anywhere.
3. Double-click `install.cmd`. It finds the game, lays down BepInEx and copies the mod in. Auto-detection covers the Xbox Game Pass install; on Steam or Epic, pass the game folder to `install.cmd` as shown below.
4. Configure OpenTrack (or your phone app) to send UDP to `127.0.0.1:4242`. See [Setting Up OpenTrack](#setting-up-opentrack).
5. Launch the game.

**Success looks like:** a `BepInEx` folder and a `winhttp.dll` next to `BLUE PRINCE.exe`, and a `HeadTracking.log` beside them after the first launch whose first lines name the version and say `Listening for tracker data on UDP port 4242`.

**If the installer cannot find your game**, tell it where the game is. Either pass the folder as an argument:

```powershell
install.cmd "D:\Games\Blue Prince\Content"
```

or set the override environment variable before running it:

```powershell
$env:BLUE_PRINCE_PATH = "D:\Games\Blue Prince\Content"
.\install.cmd
```

Mod managers do not deploy this mod. The payload is a loader plus plugin DLLs that have to land at the game root, and Vortex deploys only into the one subtree its per-game extension names; it ships no Blue Prince extension at all. Use `install.cmd`.

### Manual Installation

If you would rather not run the installer, the ZIP contains everything:

1. Extract `vendor/bepinex/BepInEx_UnityIL2CPP_x64.zip` into the game folder (the one holding `BLUE PRINCE.exe`).
2. Launch the game once and quit, so BepInEx creates `BepInEx/plugins`.
3. Copy `plugins/BluePrinceHeadTracking.dll`, `plugins/CameraUnlock.Core.dll` and `plugins/CameraUnlock.Core.Unity.dll` into `BepInEx/plugins`.

On the Xbox Game Pass build the game folder is `Blue Prince\Content` inside the `XboxGames` folder, on whichever drive you let the Xbox app install to.

## Setting Up OpenTrack

1. Install [OpenTrack](https://github.com/opentrack/opentrack/releases).
2. Set **Input** to your tracker.
3. Set **Output** to **UDP over network**.
4. In the output options set the address to `127.0.0.1` and the port to `4242`.
5. Center yourself with OpenTrack's own Center bind, then press **Start**.

Centering is done in the tracker: OpenTrack's Center bind, the CENTER button in a phone app, or SteamVR's reset.

### VR Headset Setup

1. Connect the headset over Air Link, Virtual Desktop or a link cable.
2. Start SteamVR.
3. Set OpenTrack's **Input** to the SteamVR tracker.
4. Leave **Output** on UDP `127.0.0.1`, port `4242`.

### Webcam Setup

Set OpenTrack's **Input** to the **neuralnet** tracker. It works from a plain webcam, with no markers, clips or IR hardware.

### Phone App Setup

This mod accepts one thing: the OpenTrack UDP protocol on port `4242`. A phone app is usable here if it sends that protocol itself, or ships a PC-side companion that does. Check your app against that first.

For an app that does send it, the deciding factor is how much filtering it does before the packet leaves the phone:

- An app that filters on-device can point straight at your PC's LAN address on port `4242`. I made [Headcam](https://headcam.app) so decent tracking was free for anybody with a phone already in their pocket; it filters on-device, so it can send direct. Any app that filters as well works identically.
- A raw or lightly filtered feed will jitter if you send it direct, because the mod's smoothing is sized to take the edge off a clean signal rather than to rescue a noisy one. Send that app into OpenTrack on the PC instead and let OpenTrack forward to the game, so its filters and curves can clean the feed up first.

The test rather than the list: try direct, hold your head still, and if the view drifts or shakes, route it through OpenTrack.

A tracker sending from another device on the network gets `RemoteSmoothing` rather than `LocalSmoothing`. That is decided by the address the packets arrive from, not by which machine they started on, so an OpenTrack instance on this PC sending to `192.168.x.x` instead of `127.0.0.1` also counts as remote.

## Controls

Two equivalent binding sets. Use whichever your keyboard has.

| Action              | Nav-cluster | Chord          |
|---------------------|-------------|----------------|
| Toggle tracking     | `End`       | `Ctrl+Shift+Y` |
| Cycle tracking mode | `Page Up`   | `Ctrl+Shift+G` |
| Toggle yaw mode     | `Page Down` | `Ctrl+Shift+H` |

**Cycle tracking mode** steps through three positions in order:

1. Rotation and position, the normal state. Your head turns the view and leaning moves it.
2. Rotation only. Leaning does nothing, turning still works.
3. Position only. The view stops turning with your head but still leans.

The third press returns to the first.

**Toggle yaw mode** switches between horizon-locked yaw, where turning your head rotates the view about the world's up axis and the horizon stays level, and view-local yaw, where it rotates about the view's own up axis. Horizon-locked is the default.

The tracking mode and the yaw mode you pick are saved to `CameraUnlock.ini` and are what the next start begins with. `End` turns head tracking on and off for this session only; whether it is on at the next start is the `EnableOnStartup` setting.

These are the default keys. Each action reads a list of keys from `CameraUnlock.ini` (`ToggleKey`, `CycleTrackingModeKey`, `YawModeKey`), and any key in the list fires it, so you can add, rebind or remove any of them, the chords included.

## Configuration

<!-- cameraunlock:config -->
The mod reads its settings from `BepInEx\config\CameraUnlock.ini` in the game folder, and creates the file when it starts and finds none. Edit it with any text editor.

A setting set to `default` takes its value from `Defaults.ini`, which every head tracking mod that keeps its settings in `CameraUnlock.ini` reads. Head tracking mods that keep their settings in another file do not read it, and neither do earlier versions of this mod. Writing a value in place of `default` changes that setting for this game only. When the mod saves a setting that a hotkey changed in game, it writes the new value in place of `default`, so that setting no longer follows `Defaults.ini` in this game until you set it to `default` again.

`Defaults.ini` is `%AppData%\CameraUnlock\Defaults.ini` on Windows; `$XDG_CONFIG_HOME/CameraUnlock/Defaults.ini` on Linux, or `~/.config/CameraUnlock/Defaults.ini` where `XDG_CONFIG_HOME` is not set, under Wine and Proton too; and `~/Library/Application Support/CameraUnlock/Defaults.ini` on macOS. The mod's log, where it writes one, names the file it read.

When the mod starts and finds no `Defaults.ini`, it creates one holding the built-in values, unless Windows runs the game as a packaged app, or the game runs on Linux or macOS without Wine or Proton. The mod never changes `Defaults.ini` after that. Edit it with any text editor.

Earlier versions of the mod kept these settings in `com.cameraunlock.blueprince.headtracking.cfg`, in the same folder. The first time this version starts and finds no `CameraUnlock.ini`, it reads your settings from `com.cameraunlock.blueprince.headtracking.cfg` and writes them into `CameraUnlock.ini`. It never changes `com.cameraunlock.blueprince.headtracking.cfg`, and does not read it again while `CameraUnlock.ini` exists.

A setting that the defaults below set to `default` is written as `default` when the value imported for it equals its default at that start, which is the value `Defaults.ini` gives it, or the built-in value where `Defaults.ini` gives none. It then follows `Defaults.ini`. Every other setting is written with the value imported for it. `RotationEnabled` and `PositionEnabled` are one setting here, the tracking mode, so both are written as `default` or neither is.

Comments, and keys the mod never read, are not carried over. Nor are these, where your old file had them:

- Reticle settings, and a key that toggled the reticle.
- A sensitivity, scale, deadzone, response curve or axis inversion you changed from its default. Set these in your tracker instead.
- The setting for a feature that earlier versions shipped switched off while it was untested. It now follows the mod's default.

An older version of the mod reads `com.cameraunlock.blueprince.headtracking.cfg` and never reads `CameraUnlock.ini`, so a setting you change after updating is not in `com.cameraunlock.blueprince.headtracking.cfg`.

Deleting only `CameraUnlock.ini` makes the next start read `com.cameraunlock.blueprince.headtracking.cfg` again. To go back to the defaults, replace everything in `CameraUnlock.ini` with the defaults below. Every setting they set to `default` then follows `Defaults.ini`.

On Linux and macOS without Wine or Proton, this version reads its settings and saves none: it creates no `CameraUnlock.ini`, reads your settings from `com.cameraunlock.blueprince.headtracking.cfg` again at every start while there is no `CameraUnlock.ini`, and a change made in game lasts until the game closes.

BepInEx's ConfigurationManager no longer lists these settings.

The built-in value of each setting set to `default` below:

- `UdpPort=4242`
- `EnableOnStartup=true`
- `WorldSpaceYaw=true`
- `RotationEnabled=true`
- `LocalSmoothing=0.0`
- `RemoteSmoothing=0.15`
- `PositionEnabled=true`
- `PositionLimitX=0.3`
- `PositionLimitY=0.2`
- `PositionLimitYDown=0.2`
- `PositionLimitZ=0.4`
- `PositionLimitZBack=0.1`
- `CollisionEnabled=true`
- `CollisionReleaseSmoothing=0.9`
- `ToggleKey=End, Ctrl+Shift+Y`
- `CycleTrackingModeKey=PageUp, Ctrl+Shift+G`
- `YawModeKey=PageDown, Ctrl+Shift+H`

With every setting at its default, the file reads:

```ini
; Blue Prince head tracking settings.
; Comments start with ; and go on their own line. Text after a value is part of the value.
; Hotkeys are key names such as End, PageUp or Ctrl+Shift+Y. Separate several with commas; leave empty for none.
; A setting set to default takes its value from Defaults.ini, which every head tracking mod
; that keeps its settings in CameraUnlock.ini reads: %AppData%\CameraUnlock\Defaults.ini on
; Windows, $XDG_CONFIG_HOME/CameraUnlock/Defaults.ini (normally ~/.config/CameraUnlock) on
; Linux, under Wine and Proton too, and ~/Library/Application Support/CameraUnlock/Defaults.ini
; on macOS. The log names the file it read. Write a value instead of default to change that
; setting for this game only.

[CameraUnlock]
; Written by the mod. Leave this section in place.
ConfigFormat=1

[Network]
; UDP port the mod receives tracker data on (OpenTrack protocol).
UdpPort=default

[General]
; true: head tracking is on when the game starts. ToggleKey turns it on and off.
EnableOnStartup=default
; true: yaw turns around the world's up axis. false: around the camera's own up axis.
WorldSpaceYaw=default
; true: turning your head turns the view.
; Tracking mode at startup, with PositionEnabled. The mode hotkey changes both.
RotationEnabled=default
; true: head tracking stops moving the view while the game window is not focused.
PauseOnLostFocus=true

[Smoothing]
; Smoothing when the tracker runs on this PC. 0 is the least, 1 the most.
LocalSmoothing=default
; Smoothing when the tracker is another device on the network, such as a phone.
; 0 is the least, 1 the most.
RemoteSmoothing=default

[Position]
; true: moving your head moves the view.
; Tracking mode at startup, with RotationEnabled. The mode hotkey changes both.
PositionEnabled=default
; How far, in metres, leaning left or right can move the view.
PositionLimitX=default
; How far, in metres, raising your head can move the view.
PositionLimitY=default
; How far, in metres, lowering your head can move the view.
PositionLimitYDown=default
; How far, in metres, leaning forward can move the view.
PositionLimitZ=default
; How far, in metres, leaning back can move the view.
PositionLimitZBack=default
; true: leaning stops at walls instead of moving the view through them.
CollisionEnabled=default
; How far, in metres, the view is held off a wall when you lean into it.
; The mod holds it at least 1.25 times the camera's near clip distance.
CollisionMargin=0.12
; How gently the view eases back out after a wall stopped a lean.
; 0 is the quickest, 1 the slowest.
CollisionReleaseSmoothing=default

[Hotkeys]
; Turns head tracking on and off.
ToggleKey=default
; Changes the tracking mode: rotation and position, rotation only, position only.
CycleTrackingModeKey=default
; Switches yaw between the world's up axis and the camera's own (WorldSpaceYaw).
YawModeKey=default

[Diagnostics]
; true: write the camera rig, the aim geometry and the game-state signals to
; HeadTracking.log. Verbose; turn it on when reporting a problem.
DiagnosticLogging=false
```
<!-- /cameraunlock:config -->

Field of view is the game's own setting, under `Settings` then `Controls` then `FIELD OF VIEW`. The mod reads the field of view the game is rendering on every frame, so head tracking moves the view by the same amount on screen whether or not the game has narrowed it, and the game's pointer stays on target through the change.

## Troubleshooting

Read `HeadTracking.log` first. It sits next to `BLUE PRINCE.exe`, is rewritten on every launch, and always records the version, the port it bound, the first tracker packet it accepted and the first pose it applied to the camera.

**Mod not loading** (nothing happens at all, and there is no `HeadTracking.log`)

- Check `winhttp.dll` and the `BepInEx` folder are next to `BLUE PRINCE.exe`. If they are not, `install.cmd` did not find the game. Run it again and pass the game folder as an argument.
- Check `BepInEx/LogOutput.log`. If the first launch could not reach the internet, BepInEx could not download its Unity reference libraries and never got as far as loading plugins. Connect and launch again.

**No tracking response** (the log is there, the view does not move)

- If the log says the port could not be bound: `AddressAlreadyInUse` means another program is already listening on it, most often a second game with a head tracking mod that is still running. Close it and this mod takes the port over on its own within about half a second, so there is no need to restart Blue Prince. It retries twice a second for as long as the game is running, and writes a line every 30 seconds saying it is still waiting.
- Any other bind error means nothing is holding the port and closing another game will not help. `AccessDenied` is a port Windows has reserved for itself; `netsh interface ipv4 show excludedportrange protocol=udp` lists the reserved ranges. Set `UdpPort` to a port outside them and point the tracker at it.
- If the log says the port bound but no packet ever arrived, the tracker is sending somewhere else. Confirm the address and port in OpenTrack's output options, and that `UdpPort` in the config matches. On a phone, confirm the PC's LAN address and that your firewall allows the port inbound.
- The view is left alone outside gameplay, so the pause menu, the map, a note you are reading, the day summary and every scripted camera move render exactly as the game draws them.

**Jittery or unstable tracking**

- Raise `RemoteSmoothing` if the tracker is on another device, or `LocalSmoothing` if it is on this PC.
- If the tracker is a phone app sending direct, route it through OpenTrack instead and turn its filters up. See [Phone App Setup](#phone-app-setup).

**Wrong rotation axis** (yaw feels wrong when looking up or down at extreme angles)

- Toggle between horizon-locked and view-local yaw with `Page Down` (or `Ctrl+Shift+H`). Horizon-locked, the default, is horizon-stable: turning your head rotates the view about the world's up axis whatever the camera is pitched at. View-local follows the camera's current up axis, so at a steep pitch it reads more like a roll.
- If the view sits off center or drifts away from where you are looking, center in the tracker: OpenTrack's Center bind, or your phone app's center button.

**Pointer position while tracking**

The mod moves Blue Prince's existing pointer to the world point you are aiming at, keeping the game's artwork and animation. Leaning or turning your head moves the view around that point; mouse or controller input changes your aim. Menus and paused tracking use the game's normal pointer position. The pointer always follows your aim while head tracking moves the view; it has no setting.

**The game window jumped to the middle of the screen**

By design. Running windowed, the mod centers the window on the work area of the monitor it is already on, the screen minus the taskbar, so a window the game dropped in a corner is where you can see all of it. It decides once per display mode, so drag the window where you like and it stays there until you next change the resolution or switch between windowed and fullscreen. A window that is already centered is left alone, so is one too large to fit in the work area, and fullscreen is never touched. The log line says what it did either way.

**Leaning still puts the view through a wall**

- Check `CollisionEnabled` is `true`.
- Raise `CollisionMargin`. It has to be larger than the camera's near clip distance or the surface is culled and you see through it anyway; the log says so if the mod had to raise it for you.

## Updating

Download the new release and run `install.cmd` again. Your config is preserved.

## Uninstalling

Run `uninstall.cmd`. This removes the mod DLLs, and removes BepInEx too if the installer was what put it there. Use `uninstall.cmd /force` to remove BepInEx anyway. Either way `BepInEx\config\CameraUnlock.ini` and `BepInEx\config\com.cameraunlock.blueprince.headtracking.cfg` are left in place, so a reinstall keeps your settings.

One folder is left behind: `dotnet`, the .NET runtime BepInEx's IL2CPP build ships. It does nothing once `winhttp.dll` is gone, and you can delete it by hand.

## Building from Source

Prerequisites: [pixi](https://pixi.sh) and the .NET 8 SDK. No copy of the game is needed; every build reference comes from NuGet, and the tests' BepInEx from the vendored loader archive, through `scripts/setup-libs.ps1`.

```powershell
git clone --recursive https://github.com/itsloopyo/blue-prince-headtracking.git
cd blue-prince-headtracking
pixi run package
```

`pixi run package` builds, runs the config tests (`pixi run test`) and produces the installer ZIP in `release/`. `pixi run install` deploys a Release build straight into your own game folder.

## Community & Support

- [Discord](https://discord.com/invite/dxyZdyFNT9) - setup help, bug reports, and new-release announcements
- [Lopari](https://lopari.app) - free Windows launcher with one-click install and launch of head-tracking mods
- [Headcam](https://headcam.app) - free app that turns your phone into a head tracker

## License

MIT License - see [LICENSE](LICENSE) for details.

## Credits

- Blue Prince by [Dogubomb](https://www.dogubomb.com/), published by [Raw Fury](https://rawfury.com/).
- [BepInEx](https://github.com/BepInEx/BepInEx) (LGPL-2.1)
- [HarmonyX](https://github.com/BepInEx/HarmonyX) (MIT)
- [Il2CppInterop](https://github.com/BepInEx/Il2CppInterop) (LGPL-3.0)
- [OpenTrack](https://github.com/opentrack/opentrack) (ISC)

## Disclaimer

This mod is not affiliated with, endorsed by, or supported by Dogubomb or Raw Fury. Use at your own risk.
