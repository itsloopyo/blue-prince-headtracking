// SPDX-License-Identifier: MIT
// Copyright (c) 2026 itsloopyo

using System.IO;
using BepInEx.Configuration;

namespace BluePrinceHeadTracking.Legacy;

/// <summary>
/// The plugin's BepInEx Bind calls as the published builds ran them, each definition's section,
/// key, type, description and acceptable values unchanged and its default taken from
/// <see cref="LegacyConfig"/>. Frozen for the life of the repo: it is how a player's .cfg is read,
/// whichever earlier build wrote it.
/// <para>
/// It writes nothing. BepInEx's ConfigFile read the .cfg in its constructor, before whoever calls
/// this held the file, so saving on set is turned off first and the file is read again before
/// anything is bound. A missing file reads as the defaults.
/// </para>
/// </summary>
internal static class LegacyConfigReader
{
    /// <summary>Reads <paramref name="config"/>'s file into a new <see cref="LegacyConfig"/>.</summary>
    /// <param name="found">Whether the file existed.</param>
    public static LegacyConfig Read(ConfigFile config, out bool found)
    {
        config.SaveOnConfigSet = false;
        found = File.Exists(config.ConfigFilePath);
        if (found)
        {
            config.Reload();
        }

        var read = new LegacyConfig();

        read.UdpPort = config.Bind(
            "Network",
            "UdpPort",
            read.UdpPort,
            new ConfigDescription(
                "UDP port the mod listens on for OpenTrack protocol packets. 4242 is the OpenTrack default.",
                new AcceptableValueRange<int>(1024, 65535))).Value;

        read.EnabledOnStartup = config.Bind(
            "General",
            "EnabledOnStartup",
            read.EnabledOnStartup,
            "Enable head tracking automatically when the game starts. End (or Ctrl+Shift+Y) toggles it in game.").Value;

        read.ShowReticle = config.Bind(
            "General",
            "ShowReticle",
            read.ShowReticle,
            "Move the game's pointer to your real aim point while tracking. " +
            "false leaves its position unchanged.").Value;

        read.WorldSpaceYaw = config.Bind(
            "General",
            "WorldSpaceYaw",
            read.WorldSpaceYaw,
            "true = horizon-locked yaw: turning your head rotates the view about the world's up axis. " +
            "false = the view's own up axis. Page Down (or Ctrl+Shift+H) toggles it in game.").Value;

        read.PauseOnLostFocus = config.Bind(
            "General",
            "PauseOnLostFocus",
            read.PauseOnLostFocus,
            "Stop applying head tracking while the game window is not focused.").Value;

        read.DiagnosticLogging = config.Bind(
            "General",
            "DiagnosticLogging",
            read.DiagnosticLogging,
            "Write the camera rig, the aim geometry and the game-state signals to HeadTracking.log. " +
            "Verbose; turn it on when reporting a problem.").Value;

        read.LocalSmoothing = config.Bind(
            "Smoothing",
            "LocalSmoothing",
            read.LocalSmoothing,
            new ConfigDescription(
                "Smoothing for a tracker sending from this machine over loopback (127.0.0.1). " +
                "0 = lightest, 1 = heaviest. Covers rotation and position.",
                new AcceptableValueRange<float>(0f, 1f))).Value;

        read.RemoteSmoothing = config.Bind(
            "Smoothing",
            "RemoteSmoothing",
            read.RemoteSmoothing,
            new ConfigDescription(
                "Smoothing for a tracker sending from another device on the network, such as a phone. " +
                "0 = lightest, 1 = heaviest. Covers rotation and position.",
                new AcceptableValueRange<float>(0f, 1f))).Value;

        read.PositionEnabled = config.Bind(
            "Position",
            "Enabled",
            read.PositionEnabled,
            "Whether leaning moves the view. Page Up (or Ctrl+Shift+G) cycles this in game.").Value;

        read.PositionLimitX = config.Bind(
            "Position",
            "LimitX",
            read.PositionLimitX,
            new ConfigDescription("Maximum sideways lean in metres, applied both ways.",
                new AcceptableValueRange<float>(0.01f, 0.5f))).Value;

        read.PositionLimitY = config.Bind(
            "Position",
            "LimitY",
            read.PositionLimitY,
            new ConfigDescription("Maximum upward movement in metres.",
                new AcceptableValueRange<float>(0.01f, 0.5f))).Value;

        read.PositionLimitYDown = config.Bind(
            "Position",
            "LimitYDown",
            read.PositionLimitYDown,
            new ConfigDescription("Maximum downward movement in metres.",
                new AcceptableValueRange<float>(0.01f, 0.5f))).Value;

        read.PositionLimitZ = config.Bind(
            "Position",
            "LimitZ",
            read.PositionLimitZ,
            new ConfigDescription("Maximum forward lean in metres.",
                new AcceptableValueRange<float>(0.01f, 0.5f))).Value;

        read.PositionLimitZBack = config.Bind(
            "Position",
            "LimitZBack",
            read.PositionLimitZBack,
            new ConfigDescription(
                "Maximum backward lean in metres. Deliberately tighter than LimitZ so the view " +
                "cannot pull back through the player.",
                new AcceptableValueRange<float>(0.01f, 0.5f))).Value;

        read.CollisionEnabled = config.Bind(
            "Collision",
            "CollisionEnabled",
            read.CollisionEnabled,
            "Sweep the lean against the level so leaning into a wall cannot put the view inside it.").Value;

        read.CollisionRadius = config.Bind(
            "Collision",
            "CollisionRadius",
            read.CollisionRadius,
            new ConfigDescription(
                "How far off a surface the view is held, in metres. Must be larger than the camera's " +
                "near clip distance, or the wall is still not drawn and you still see through it.",
                new AcceptableValueRange<float>(0.02f, 0.5f))).Value;

        read.CollisionReleaseSmoothing = config.Bind(
            "Collision",
            "CollisionReleaseSmoothing",
            read.CollisionReleaseSmoothing,
            new ConfigDescription(
                "How gently the lean opens back up once an obstruction clears. Tightening is always " +
                "instant. 0.9 is about a fifth of a second.",
                new AcceptableValueRange<float>(0f, 1f))).Value;

        read.ToggleKey = config.Bind(
            "Hotkeys",
            "ToggleKey",
            read.ToggleKey,
            "Turns head tracking on and off. Ctrl+Shift+Y does the same on keyboards with no nav cluster.").Value;

        read.CycleTrackingModeKey = config.Bind(
            "Hotkeys",
            "CycleTrackingModeKey",
            read.CycleTrackingModeKey,
            "Cycles full tracking -> rotation only -> position only. Ctrl+Shift+G does the same.").Value;

        read.YawModeKey = config.Bind(
            "Hotkeys",
            "YawModeKey",
            read.YawModeKey,
            "Switches between horizon-locked and view-local yaw. Ctrl+Shift+H does the same.").Value;

        return read;
    }
}
