// SPDX-License-Identifier: MIT
// Copyright (c) 2026 itsloopyo

using UnityEngine;

namespace BluePrinceHeadTracking.Configuration;

/// <summary>
/// The settings the plugin runs on, read once at startup.
///
/// There are deliberately no sensitivity, deadzone, response-curve or axis-inversion
/// settings here. The tracker owns pose shaping: OpenTrack, a phone app or a headset
/// each already have those controls, and configuring them once there is what makes a
/// single profile behave the same across every game. The axis signs this game needs
/// are a fixed conversion applied at the engine boundary, not a knob.
/// </summary>
internal sealed class ModConfig
{
    public int UdpPort { get; set; }
    public bool EnabledOnStartup { get; set; }
    public bool ShowReticle { get; set; }
    public bool WorldSpaceYaw { get; set; }
    public bool PauseOnLostFocus { get; set; }
    public bool DiagnosticLogging { get; set; }
    public float LocalSmoothing { get; set; }
    public float RemoteSmoothing { get; set; }
    public bool PositionEnabled { get; set; }
    public float PositionLimitX { get; set; }
    public float PositionLimitY { get; set; }
    public float PositionLimitYDown { get; set; }
    public float PositionLimitZ { get; set; }
    public float PositionLimitZBack { get; set; }
    public bool CollisionEnabled { get; set; }
    public float CollisionRadius { get; set; }
    public float CollisionReleaseSmoothing { get; set; }
    public KeyCode ToggleKey { get; set; }
    public KeyCode CycleTrackingModeKey { get; set; }
    public KeyCode YawModeKey { get; set; }
}
