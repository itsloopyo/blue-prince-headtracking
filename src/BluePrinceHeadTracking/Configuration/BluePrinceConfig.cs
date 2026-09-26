// SPDX-License-Identifier: MIT
// Copyright (c) 2026 itsloopyo

using BluePrinceHeadTracking.Legacy;
using CameraUnlock.Core.Config;

namespace BluePrinceHeadTracking.Configuration;

/// <summary>
/// Everything the mod reads from BepInEx\config\CameraUnlock.ini. Unity-free, so the test
/// project compiles it and holds the committed file to it.
///
/// There are deliberately no sensitivity, deadzone, response-curve or axis-inversion
/// settings here. The tracker owns pose shaping: OpenTrack, a phone app or a headset
/// each already have those controls, and configuring them once there is what makes a
/// single profile behave the same across every game. The axis signs this game needs
/// are a fixed conversion applied at the engine boundary, not a knob.
/// </summary>
internal sealed class BluePrinceConfig : HeadTrackingConfigData
{
    /// <summary>The game's name as data/games.json spells it.</summary>
    public const string DisplayName = "Blue Prince";

    public BluePrinceConfig()
    {
        CollisionMargin = 0.12f;
    }

    public bool PauseOnLostFocus { get; set; } = true;

    public bool DiagnosticLogging { get; set; }

    public static ConfigTable<BluePrinceConfig> Table()
    {
        return HeadTrackingConfigTable.Create<BluePrinceConfig>(
                ConfigConcepts.UdpPort,
                ConfigConcepts.EnableOnStartup,
                ConfigConcepts.WorldSpaceYaw,
                ConfigConcepts.RotationEnabled,
                ConfigConcepts.LocalSmoothing,
                ConfigConcepts.RemoteSmoothing,
                ConfigConcepts.PositionEnabled,
                ConfigConcepts.PositionLimitX,
                ConfigConcepts.PositionLimitY,
                ConfigConcepts.PositionLimitYDown,
                ConfigConcepts.PositionLimitZ,
                ConfigConcepts.PositionLimitZBack,
                ConfigConcepts.CollisionEnabled,
                ConfigConcepts.CollisionMargin,
                ConfigConcepts.CollisionReleaseSmoothing,
                ConfigConcepts.ToggleKey,
                ConfigConcepts.CycleTrackingModeKey,
                ConfigConcepts.YawModeKey)
            .Select(ConfigConcepts.WorldSpaceYaw).Writable()
            .Select(ConfigConcepts.RotationEnabled).Writable()
            .Select(ConfigConcepts.PositionEnabled).Writable()
            .Select(ConfigConcepts.CollisionMargin)
            .Comment("How far, in metres, the view is held off a wall when you lean into it.\n" +
                     "The mod holds it at least 1.25 times the camera's near clip distance.")
            .Local("General", "PauseOnLostFocus", c => c.PauseOnLostFocus, (c, v) => c.PauseOnLostFocus = v,
                new BoolCodec(),
                "true: head tracking stops moving the view while the game window is not focused.")
            .Local("Diagnostics", "DiagnosticLogging", c => c.DiagnosticLogging, (c, v) => c.DiagnosticLogging = v,
                new BoolCodec(),
                "true: write the camera rig, the aim geometry and the game-state signals to\n" +
                "HeadTracking.log. Verbose; turn it on when reporting a problem.");
    }

    /// <summary>
    /// The config owner's options for <paramref name="path"/>, importing the published builds'
    /// .cfg at <paramref name="legacyPath"/> while it is absent. The mod passes
    /// <see cref="DefaultsFile.PerUser"/>, and a test a scratch file.
    /// </summary>
    public static ConfigOwnerOptions<BluePrinceConfig> Options(string path, string legacyPath, DefaultsFile defaults)
    {
        return new ConfigOwnerOptions<BluePrinceConfig>
        {
            Path = path,
            Table = Table(),
            Import = LegacyConfigImport.Create(),
            LegacySourcePath = legacyPath,
            Header = new RenderHeader(DisplayName),
            Defaults = defaults,
        };
    }
}
