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
