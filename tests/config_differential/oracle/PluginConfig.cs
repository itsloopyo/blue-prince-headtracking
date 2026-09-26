// SPDX-License-Identifier: MIT
// Copyright (c) 2026 itsloopyo

using BepInEx.Configuration;
using UnityEngine;

namespace BluePrinceHeadTracking.Configuration;

/// <summary>
/// Configuration bindings, saved to
/// <c>BepInEx/config/com.cameraunlock.blueprince.headtracking.cfg</c>.
///
/// There are deliberately no sensitivity, deadzone, response-curve or axis-inversion
/// settings here. The tracker owns pose shaping: OpenTrack, a phone app or a headset
/// each already have those controls, and configuring them once there is what makes a
/// single profile behave the same across every game. The axis signs this game needs
/// are a fixed conversion applied at the engine boundary, not a knob.
/// </summary>
internal sealed class PluginConfig
{
    /// <summary>UDP port the tracker sends to.</summary>
    internal ConfigEntry<int> UdpPort { get; private set; } = null!;

    /// <summary>Enable head tracking when the game starts.</summary>
    internal ConfigEntry<bool> EnabledOnStartup { get; private set; } = null!;

    /// <summary>Reposition the game's pointer at the tracked aim point.</summary>
    internal ConfigEntry<bool> ShowReticle { get; private set; } = null!;

    /// <summary>True = horizon-locked yaw. False = yaw about the camera's own up axis.</summary>
    internal ConfigEntry<bool> WorldSpaceYaw { get; private set; } = null!;

    /// <summary>Pause tracking when the game window loses focus.</summary>
    internal ConfigEntry<bool> PauseOnLostFocus { get; private set; } = null!;

    /// <summary>Write the per-frame camera rig and pose probes to the log.</summary>
    internal ConfigEntry<bool> DiagnosticLogging { get; private set; } = null!;

    /// <summary>Smoothing for a tracker on this machine (loopback). Covers rotation and position.</summary>
    internal ConfigEntry<float> LocalSmoothing { get; private set; } = null!;

    /// <summary>Smoothing for a tracker on a remote network device. Covers rotation and position.</summary>
    internal ConfigEntry<float> RemoteSmoothing { get; private set; } = null!;

    internal ConfigEntry<bool> PositionEnabled { get; private set; } = null!;
    internal ConfigEntry<float> PositionLimitX { get; private set; } = null!;
    internal ConfigEntry<float> PositionLimitY { get; private set; } = null!;
    internal ConfigEntry<float> PositionLimitYDown { get; private set; } = null!;
    internal ConfigEntry<float> PositionLimitZ { get; private set; } = null!;
    internal ConfigEntry<float> PositionLimitZBack { get; private set; } = null!;

    /// <summary>Sweep the lean against level geometry so it cannot end inside a wall.</summary>
    internal ConfigEntry<bool> CollisionEnabled { get; private set; } = null!;

    /// <summary>Standoff held off a surface, in metres. Must exceed the camera's near clip.</summary>
    internal ConfigEntry<float> CollisionRadius { get; private set; } = null!;

    /// <summary>How quickly a cleared obstruction gives the lean back. Tightening is never smoothed.</summary>
    internal ConfigEntry<float> CollisionReleaseSmoothing { get; private set; } = null!;

    internal ConfigEntry<KeyCode> ToggleKey { get; private set; } = null!;
    internal ConfigEntry<KeyCode> CycleTrackingModeKey { get; private set; } = null!;
    internal ConfigEntry<KeyCode> YawModeKey { get; private set; } = null!;

    /// <summary>
    /// Binds every setting and writes the file once.
    ///
    /// <c>ConfigFile.Bind</c> truncates and rewrites the whole file each time it
    /// adds an entry it has not seen, and it has seen none of them at startup, so
    /// leaving BepInEx's default in place rewrites the config once per setting on
    /// every single launch. Suppressing that for the duration and saving at the end
    /// turns a session's twenty rewrites into one, and leaves the file whole rather
    /// than part written if the game dies mid startup.
    /// </summary>
    internal void Initialize(ConfigFile config)
    {
        config.SaveOnConfigSet = false;

        UdpPort = config.Bind(
            "Network",
            "UdpPort",
            4242,
            new ConfigDescription(
                "UDP port the mod listens on for OpenTrack protocol packets. 4242 is the OpenTrack default.",
                new AcceptableValueRange<int>(1024, 65535)));

        EnabledOnStartup = config.Bind(
            "General",
            "EnabledOnStartup",
            true,
            "Enable head tracking automatically when the game starts. End (or Ctrl+Shift+Y) toggles it in game.");

        ShowReticle = config.Bind(
            "General",
            "ShowReticle",
            true,
            "Move the game's pointer to your real aim point while tracking. " +
            "false leaves its position unchanged.");

        WorldSpaceYaw = config.Bind(
            "General",
            "WorldSpaceYaw",
            true,
            "true = horizon-locked yaw: turning your head rotates the view about the world's up axis. " +
            "false = the view's own up axis. Page Down (or Ctrl+Shift+H) toggles it in game.");

        PauseOnLostFocus = config.Bind(
            "General",
            "PauseOnLostFocus",
            true,
            "Stop applying head tracking while the game window is not focused.");

        DiagnosticLogging = config.Bind(
            "General",
            "DiagnosticLogging",
            false,
            "Write the camera rig, the aim geometry and the game-state signals to HeadTracking.log. " +
            "Verbose; turn it on when reporting a problem.");

        LocalSmoothing = config.Bind(
            "Smoothing",
            "LocalSmoothing",
            0.0f,
            new ConfigDescription(
                "Smoothing for a tracker sending from this machine over loopback (127.0.0.1). " +
                "0 = lightest, 1 = heaviest. Covers rotation and position.",
                new AcceptableValueRange<float>(0f, 1f)));

        RemoteSmoothing = config.Bind(
            "Smoothing",
            "RemoteSmoothing",
            0.15f,
            new ConfigDescription(
                "Smoothing for a tracker sending from another device on the network, such as a phone. " +
                "0 = lightest, 1 = heaviest. Covers rotation and position.",
                new AcceptableValueRange<float>(0f, 1f)));

        PositionEnabled = config.Bind(
            "Position",
            "Enabled",
            true,
            "Whether leaning moves the view. Page Up (or Ctrl+Shift+G) cycles this in game.");

        PositionLimitX = config.Bind(
            "Position",
            "LimitX",
            0.30f,
            new ConfigDescription("Maximum sideways lean in metres, applied both ways.",
                new AcceptableValueRange<float>(0.01f, 0.5f)));

        PositionLimitY = config.Bind(
            "Position",
            "LimitY",
            0.20f,
            new ConfigDescription("Maximum upward movement in metres.",
                new AcceptableValueRange<float>(0.01f, 0.5f)));

        PositionLimitYDown = config.Bind(
            "Position",
            "LimitYDown",
            0.20f,
            new ConfigDescription("Maximum downward movement in metres.",
                new AcceptableValueRange<float>(0.01f, 0.5f)));

        PositionLimitZ = config.Bind(
            "Position",
            "LimitZ",
            0.40f,
            new ConfigDescription("Maximum forward lean in metres.",
                new AcceptableValueRange<float>(0.01f, 0.5f)));

        PositionLimitZBack = config.Bind(
            "Position",
            "LimitZBack",
            0.10f,
            new ConfigDescription(
                "Maximum backward lean in metres. Deliberately tighter than LimitZ so the view " +
                "cannot pull back through the player.",
                new AcceptableValueRange<float>(0.01f, 0.5f)));

        CollisionEnabled = config.Bind(
            "Collision",
            "CollisionEnabled",
            true,
            "Sweep the lean against the level so leaning into a wall cannot put the view inside it.");

        CollisionRadius = config.Bind(
            "Collision",
            "CollisionRadius",
            0.12f,
            new ConfigDescription(
                "How far off a surface the view is held, in metres. Must be larger than the camera's " +
                "near clip distance, or the wall is still not drawn and you still see through it.",
                new AcceptableValueRange<float>(0.02f, 0.5f)));

        CollisionReleaseSmoothing = config.Bind(
            "Collision",
            "CollisionReleaseSmoothing",
            0.9f,
            new ConfigDescription(
                "How gently the lean opens back up once an obstruction clears. Tightening is always " +
                "instant. 0.9 is about a fifth of a second.",
                new AcceptableValueRange<float>(0f, 1f)));

        ToggleKey = config.Bind(
            "Hotkeys",
            "ToggleKey",
            KeyCode.End,
            "Turns head tracking on and off. Ctrl+Shift+Y does the same on keyboards with no nav cluster.");

        CycleTrackingModeKey = config.Bind(
            "Hotkeys",
            "CycleTrackingModeKey",
            KeyCode.PageUp,
            "Cycles full tracking -> rotation only -> position only. Ctrl+Shift+G does the same.");

        YawModeKey = config.Bind(
            "Hotkeys",
            "YawModeKey",
            KeyCode.PageDown,
            "Switches between horizon-locked and view-local yaw. Ctrl+Shift+H does the same.");

        config.Save();
        config.SaveOnConfigSet = true;
    }
}
