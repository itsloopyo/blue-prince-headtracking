// SPDX-License-Identifier: MIT
// Copyright (c) 2026 itsloopyo

using UnityEngine;

namespace BluePrinceHeadTracking.Legacy;

/// <summary>
/// The settings every published build read from
/// BepInEx\config\com.cameraunlock.blueprince.headtracking.cfg, with their defaults. Frozen: a
/// later change to the runtime settings must never change what an old .cfg, or one missing a
/// key, reads as.
/// </summary>
internal sealed class LegacyConfig
{
    public int UdpPort = 4242;

    public bool EnabledOnStartup = true;
    public bool ShowReticle = true;
    public bool WorldSpaceYaw = true;
    public bool PauseOnLostFocus = true;
    public bool DiagnosticLogging = false;

    public float LocalSmoothing = 0.0f;
    public float RemoteSmoothing = 0.15f;

    public bool PositionEnabled = true;
    public float PositionLimitX = 0.30f;
    public float PositionLimitY = 0.20f;
    public float PositionLimitYDown = 0.20f;
    public float PositionLimitZ = 0.40f;
    public float PositionLimitZBack = 0.10f;

    public bool CollisionEnabled = true;
    public float CollisionRadius = 0.12f;
    public float CollisionReleaseSmoothing = 0.9f;

    public KeyCode ToggleKey = KeyCode.End;
    public KeyCode CycleTrackingModeKey = KeyCode.PageUp;
    public KeyCode YawModeKey = KeyCode.PageDown;
}
