# Changelog

All notable changes to this project are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Added

- Added head tracking for Blue Prince: yaw, pitch and roll, plus positional
  lean, peek and duck.
- Repositioned the game's own pointer at the world aim point while head tracking
  is active, preserving its artwork and animation.
- Added a head tracking on/off toggle on `End` or `Ctrl+Shift+Y`.
- Added horizon-locked and view-local yaw modes, on `Page Down` or
  `Ctrl+Shift+H`.
- Added a three-position tracking-mode cycle on `Page Up` or `Ctrl+Shift+G`.
- Added a lean clamp: leaning is swept against the level, so leaning into a wall
  cannot put the view inside it.
- Added window centring, so running windowed the game window is centred on the
  monitor it is already on whenever the resolution or the fullscreen setting
  changes.
- Added field-of-view scaling, so head tracking moves the view by the same
  amount on screen whether or not the game has narrowed it, and follows the
  game's own `FIELD OF VIEW` setting without a restart.
- A setting set to `default` in `CameraUnlock.ini` takes its value from `Defaults.ini`, which every head tracking mod that keeps its settings in `CameraUnlock.ini` reads. Head tracking mods that keep their settings in another file do not read it, and neither do earlier versions of this mod. Writing a value in place of `default` changes that setting for this game only. When the mod saves a setting that a hotkey changed in game, it writes the new value in place of `default`, so that setting no longer follows `Defaults.ini` in this game until you set it to `default` again.
- `Defaults.ini` is `%AppData%\CameraUnlock\Defaults.ini` on Windows; `$XDG_CONFIG_HOME/CameraUnlock/Defaults.ini` on Linux, or `~/.config/CameraUnlock/Defaults.ini` where `XDG_CONFIG_HOME` is not set, under Wine and Proton too; and `~/Library/Application Support/CameraUnlock/Defaults.ini` on macOS. The mod's log, where it writes one, names the file it read.
- When the mod starts and finds no `Defaults.ini`, it creates one holding the built-in values, unless Windows runs the game as a packaged app, or the game runs on Linux or macOS without Wine or Proton. The mod never changes `Defaults.ini` after that.

### Changed

- Settings move to `BepInEx\config\CameraUnlock.ini`. Earlier versions of the mod kept these settings in `com.cameraunlock.blueprince.headtracking.cfg`, in the same folder. The first time this version starts and finds no `CameraUnlock.ini`, it reads your settings from `com.cameraunlock.blueprince.headtracking.cfg` and writes them into `CameraUnlock.ini`. It never changes `com.cameraunlock.blueprince.headtracking.cfg`, and does not read it again while `CameraUnlock.ini` exists.
- A setting that the defaults the README shows set to `default` is written as `default` when the value imported for it equals its default at that start, which is the value `Defaults.ini` gives it, or the built-in value where `Defaults.ini` gives none. It then follows `Defaults.ini`. Every other setting is written with the value imported for it.
- `RotationEnabled` and `PositionEnabled` are one setting here, the tracking mode, so both are written as `default` or neither is.
- Comments, and keys the mod never read, are not carried over. Nor are these, where your old file had them:
  - A sensitivity, scale, deadzone, response curve or axis inversion you changed from its default. Set these in your tracker instead.
  - Reticle settings, and a key that toggled the reticle.
  - The setting for a feature that earlier versions shipped switched off while it was untested. It now follows the mod's default.
- An older version of the mod reads `com.cameraunlock.blueprince.headtracking.cfg` and never reads `CameraUnlock.ini`, so a setting you change after updating is not in `com.cameraunlock.blueprince.headtracking.cfg`.
- Deleting only `CameraUnlock.ini` makes the next start read `com.cameraunlock.blueprince.headtracking.cfg` again. To go back to the defaults, replace everything in `CameraUnlock.ini` with the defaults the README shows. Every setting they set to `default` then follows `Defaults.ini`.
- BepInEx's ConfigurationManager no longer lists these settings. Edit `BepInEx\config\CameraUnlock.ini` with any text editor.
- Hotkeys are written as key names, and each hotkey lists every key that triggers it, the Ctrl+Shift chord included: `ToggleKey=End, Ctrl+Shift+Y`.
- A hotkey bound to a plain key no longer fires while Ctrl and Shift are both held, so Ctrl+Shift with that key reaches only a binding that names the chord.
- On Linux and macOS without Wine or Proton, this version reads its settings and saves none: it creates no `CameraUnlock.ini`, reads your settings from `com.cameraunlock.blueprince.headtracking.cfg` again at every start while there is no `CameraUnlock.ini`, and a change made in game lasts until the game closes.
- The tracking mode (`Page Up`) and the yaw mode (`Page Down`) you pick are saved to `CameraUnlock.ini` and are what the next start begins with. Earlier versions started every session from the file's settings. `End` still changes the current session only.
- Several settings have new names or places in `CameraUnlock.ini`: `EnabledOnStartup` is `EnableOnStartup`; `[Position] Enabled` is `PositionEnabled`, which with `RotationEnabled` sets the tracking mode at startup; `LimitX`, `LimitY`, `LimitYDown`, `LimitZ` and `LimitZBack` are `PositionLimitX` and so on; the `[Collision]` keys are under `[Position]`, with `CollisionRadius` now `CollisionMargin`; and `DiagnosticLogging` is under `[Diagnostics]`. The import carries each value to its new place.

### Removed

- `ShowReticle`. The game's pointer always follows the aim while head tracking moves the view; an imported `ShowReticle=false` is dropped and logged.
