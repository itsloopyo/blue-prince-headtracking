// SPDX-License-Identifier: MIT
// Copyright (c) 2026 itsloopyo

using System;
using BluePrinceHeadTracking.Configuration;
using BluePrinceHeadTracking.Core;
using CameraUnlock.Core.Input;
using CameraUnlock.Core.Unity.Extensions;

namespace BluePrinceHeadTracking.Input;

/// <summary>
/// Fires the mod's hotkey actions from the key lists in CameraUnlock.ini: ToggleKey,
/// CycleTrackingModeKey and YawModeKey. Every binding in a list is an ordinary item, the
/// Ctrl+Shift chords included, so the defaults (End, Page Up and Page Down, each with its
/// chord) are rebindable like any other key.
///
/// Every action works in menus as well as in gameplay, so a player can always turn tracking
/// off.
/// </summary>
internal sealed class HotkeyHandler
{
    private readonly HeadTrackingBehaviour _behaviour;
    private readonly BluePrinceConfig _config;

    private readonly KeyBinding[] _toggle;
    private readonly KeyBinding[] _cycleTrackingMode;
    private readonly KeyBinding[] _yawMode;

    internal HotkeyHandler(BluePrinceConfig config, HeadTrackingBehaviour behaviour)
    {
        _behaviour = behaviour;
        _config = config;
        _toggle = Parse("ToggleKey", config.ToggleKeyName);
        _cycleTrackingMode = Parse("CycleTrackingModeKey", config.CycleTrackingModeKeyName);
        _yawMode = Parse("YawModeKey", config.YawModeKeyName);
    }

    internal void LogBindings()
    {
        HeadTrackingPlugin.Logger.LogInfo(
            $"Hotkeys: [{_config.ToggleKeyName}] toggle, [{_config.CycleTrackingModeKeyName}] cycle tracking mode, " +
            $"[{_config.YawModeKeyName}] yaw mode");
    }

    /// <summary>Call once per frame from Update.</summary>
    internal void ProcessInput()
    {
        if (KeyBindingInput.IsTriggered(_toggle))
        {
            _behaviour.ToggleTracking();
        }

        if (KeyBindingInput.IsTriggered(_cycleTrackingMode))
        {
            _behaviour.CycleTrackingMode();
        }

        if (KeyBindingInput.IsTriggered(_yawMode))
        {
            _behaviour.ToggleYawMode();
        }
    }

    // The table's hotkey codec has read every list the file holds, and the legacy import writes
    // only key lists, so a list that does not parse is a bug.
    private static KeyBinding[] Parse(string key, string text)
    {
        if (!KeyBindings.TryParse(text, out KeyBinding[] bindings, out string? error))
            throw new InvalidOperationException($"[Hotkeys] {key}={text}: {error}");
        return bindings;
    }
}
