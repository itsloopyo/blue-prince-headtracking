// SPDX-License-Identifier: MIT
// Copyright (c) 2026 itsloopyo

using BluePrinceHeadTracking.Configuration;
using BluePrinceHeadTracking.Core;
using CameraUnlock.Core.Unity.Extensions;
using UnityEngine;

namespace BluePrinceHeadTracking.Input;

/// <summary>
/// Two equivalent binding sets, registered at once:
/// <list type="bullet">
/// <item>nav cluster - End toggles, Page Up cycles the tracking mode, Page Down
/// switches yaw mode;</item>
/// <item>chords for keyboards without one - Ctrl+Shift+Y, Ctrl+Shift+G,
/// Ctrl+Shift+H, in the same order every mod in the fleet uses.</item>
/// </list>
///
/// Either binding fires the same handler, and both work in menus as well as in
/// gameplay so a player can always turn tracking off.
/// </summary>
internal sealed class HotkeyHandler
{
    private readonly HeadTrackingBehaviour _behaviour;

    // Cached: each ConfigEntry<T>.Value read goes through BepInEx's boxing
    // accessor, and these are polled every frame.
    private readonly KeyCode _toggleKey;
    private readonly KeyCode _cycleModeKey;
    private readonly KeyCode _yawModeKey;

    internal HotkeyHandler(PluginConfig config, HeadTrackingBehaviour behaviour)
    {
        _behaviour = behaviour;
        _toggleKey = config.ToggleKey.Value;
        _cycleModeKey = config.CycleTrackingModeKey.Value;
        _yawModeKey = config.YawModeKey.Value;
    }

    internal void LogBindings()
    {
        HeadTrackingPlugin.Logger.LogInfo(
            $"Hotkeys: {_toggleKey} toggle, {_cycleModeKey} cycle tracking mode, {_yawModeKey} yaw mode; " +
            "chords Ctrl+Shift+Y / Ctrl+Shift+G / Ctrl+Shift+H");
    }

    /// <summary>Call once per frame from Update.</summary>
    internal void ProcessInput()
    {
        if (ChordHotkeys.IsActionPressed(_toggleKey, ChordHotkeys.ToggleLetter))
        {
            _behaviour.ToggleTracking();
        }

        if (ChordHotkeys.IsActionPressed(_cycleModeKey, ChordHotkeys.PositionLetter))
        {
            _behaviour.CycleTrackingMode();
        }

        if (ChordHotkeys.IsActionPressed(_yawModeKey, ChordHotkeys.FourthToggleLetter))
        {
            _behaviour.ToggleYawMode();
        }
    }
}
