# bepinex (vendored)

This directory contains a bundled copy of the upstream mod loader. It is the install-time
source of truth: install.cmd extracts directly from here and never reaches out to the network.
Refresh manually with `pixi run update-deps`, then commit.

## Snapshot

- Asset: `BepInEx-Unity.IL2CPP-win-x64-6.0.0-be.785%2B6abdba4.zip`
- Vendored as: `BepInEx_UnityIL2CPP_x64.zip`
- Tag: `6.0.0-be.785`
- Commit: `6abdba4`
- Upstream URL: https://builds.bepinex.dev/projects/bepinex_be/785/BepInEx-Unity.IL2CPP-win-x64-6.0.0-be.785%2B6abdba4.zip
- SHA-256: `2a7cbf74d26abe4765c3e662db1721b923bac39849ebfef2ca5dc7de7e2d9b7f`
- Fetched at: 2026-08-26T12:59:26.0758021+01:00
- Source: direct-url

Do not edit this directory by hand. Run ``pixi run update-deps`` to refresh, then commit.
