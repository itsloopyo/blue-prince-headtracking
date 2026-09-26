// SPDX-License-Identifier: MIT
// Copyright (c) 2026 itsloopyo

using CameraUnlock.Core.Config;

namespace BluePrinceHeadTracking.Legacy;

/// <summary>Every section and key <see cref="LegacyConfigReader"/> reads. Frozen with it.</summary>
internal static class LegacyConfigKeys
{
    public static LegacyKey[] All()
    {
        return new[]
        {
            new LegacyKey("Network", "UdpPort"),
            new LegacyKey("General", "EnabledOnStartup"),
            new LegacyKey("General", "ShowReticle"),
            new LegacyKey("General", "WorldSpaceYaw"),
            new LegacyKey("General", "PauseOnLostFocus"),
            new LegacyKey("General", "DiagnosticLogging"),
            new LegacyKey("Smoothing", "LocalSmoothing"),
            new LegacyKey("Smoothing", "RemoteSmoothing"),
            new LegacyKey("Position", "Enabled"),
            new LegacyKey("Position", "LimitX"),
            new LegacyKey("Position", "LimitY"),
            new LegacyKey("Position", "LimitYDown"),
            new LegacyKey("Position", "LimitZ"),
            new LegacyKey("Position", "LimitZBack"),
            new LegacyKey("Collision", "CollisionEnabled"),
            new LegacyKey("Collision", "CollisionRadius"),
            new LegacyKey("Collision", "CollisionReleaseSmoothing"),
            new LegacyKey("Hotkeys", "ToggleKey"),
            new LegacyKey("Hotkeys", "CycleTrackingModeKey"),
            new LegacyKey("Hotkeys", "YawModeKey"),
        };
    }
}
