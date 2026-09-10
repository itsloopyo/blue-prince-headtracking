// SPDX-License-Identifier: MIT
// Copyright (c) 2026 itsloopyo

using System;
using System.Reflection;
using BluePrinceHeadTracking.Core;
using BluePrinceHeadTracking.Utilities;
using HarmonyLib;
using UnityEngine;

namespace BluePrinceHeadTracking.Camera;

/// <summary>
/// The two fields of view the zoom correction is built from, read live from the
/// game every frame.
///
/// The live one comes off the projection matrix rather than off
/// <c>Camera.fieldOfView</c>, because the matrix is what the frame is projected
/// with whatever wrote it. Its m11 term is <c>1 / tan(fovVertical / 2)</c> by
/// construction, so the value is a VERTICAL half-angle tangent with no unit
/// assumption in the path.
///
/// The base is the field of view the game renders when nothing has narrowed it,
/// and in Blue Prince that is the player's own setting. Settings -> Controls ->
/// FIELD OF VIEW is an OFFSET rather than an absolute angle: the slider reads
/// "+0.0" at one end and "+12.0" at the other, the number lands on
/// <c>PlayerSettingsData.FieldOfView</c>, and the player camera renders that many
/// degrees more than its own <see cref="UnzoomedVerticalFov"/>. Reading it live
/// rather than latching it means moving the slider mid session moves the base
/// with it, and the correction stays 1.0 in ordinary play.
///
/// A base that cannot be read means no correction and one log line, never a
/// guessed one.
/// </summary>
internal sealed class GameFieldOfView
{
    // The settings singleton does not exist on the very first frames, and neither
    // does it after a teardown, so the search is retried on an interval.
    private const int SearchRetryFrames = 60;

    // The player camera's vertical field of view before the setting's offset is
    // added. Measured on the shipped build: the camera renders 68.000 at the
    // shipped default of +8.0 and 71.540 at a setting of +11.540, so the offset is
    // one degree per unit on top of 60, and the projection matrix agrees with
    // fieldOfView on that camera. The menu cameras never take the offset - the
    // main menu renders a flat 60 and the new-game camera an 82 degree projection
    // that fieldOfView does not describe at all - which is why the basis line
    // below is only written for the camera the mod actually drives.
    private const float UnzoomedVerticalFov = 60f;

    private MethodInfo? _currentDataGetter;
    private MethodInfo? _playerGetter;
    private FieldInfo? _fieldOfViewField;
    private Type? _settingsMasterType;
    private int _framesUntilRetry;
    private bool _loggedMissingSettings;
    private int _loggedBasisForCamera;

    /// <summary>Vertical field of view the current frame is projected with, in degrees.</summary>
    internal float LiveVerticalFov { get; private set; }

    /// <summary>The game's un-zoomed vertical field of view, in degrees, or -1.</summary>
    internal float BaseVerticalFov { get; private set; } = -1f;

    /// <summary>
    /// What yaw, pitch and the lean are scaled by this frame. Exactly 1.0 whenever
    /// the game is rendering its un-zoomed field of view, and 1.0 when the base
    /// cannot be read.
    /// </summary>
    internal float Factor { get; private set; } = 1f;

    /// <summary>
    /// Re-reads both fields of view and recomputes the factor. Call once per frame,
    /// from the camera path, before the pose is applied.
    /// </summary>
    internal void Update(UnityEngine.Camera camera, bool inGameplay)
    {
        float m11 = camera.projectionMatrix.m11;
        if (m11 <= 0f)
        {
            // An orthographic or otherwise degenerate projection has no half-angle
            // to compare, so nothing is scaled.
            LiveVerticalFov = -1f;
            Factor = 1f;
            return;
        }

        float tanHalfLive = 1f / m11;
        LiveVerticalFov = Mathf.Atan(tanHalfLive) * 2f * Mathf.Rad2Deg;

        float offset = ReadSettingsFieldOfViewOffset();
        if (offset < 0f)
        {
            BaseVerticalFov = -1f;
            Factor = 1f;
            return;
        }

        BaseVerticalFov = UnzoomedVerticalFov + offset;
        float tanHalfBase = Mathf.Tan(BaseVerticalFov * 0.5f * Mathf.Deg2Rad);
        Factor = ZoomCompensation.Factor(tanHalfLive, tanHalfBase);

        if (inGameplay)
        {
            LogBasisOnce(camera, tanHalfLive, tanHalfBase, offset);
        }
    }

    /// <summary>
    /// The one line that proves the correction is built from two numbers in the
    /// same units. Written on the first gameplay frame of each camera the mod
    /// drives, rather than on the first frame a pose arrives, so the basis is on the
    /// log with no tracker connected at all. The latch holds one id, so a game that
    /// alternated between two cameras would write a line per alternation; this one
    /// re-resolves to the same camera on every gameplay entry. Per camera rather than per session
    /// because the basis is a property of the projection being read: a camera whose
    /// projection is written directly renders a different half-angle from the one
    /// its fieldOfView reports, and a single session-wide latch would leave that
    /// camera's factor as the only one on the log.
    ///
    /// The gate is that it reads 1.0000 in ordinary gameplay. A factor that is
    /// wrong by a constant reads exactly like a correct one from inside the game.
    /// </summary>
    private void LogBasisOnce(UnityEngine.Camera camera, float tanHalfLive, float tanHalfBase, float offset)
    {
        int cameraId = camera.GetInstanceID();
        if (_loggedBasisForCamera == cameraId) return;
        _loggedBasisForCamera = cameraId;

        HeadTrackingPlugin.Logger.LogInfo(
            $"FOVBASIS live={LiveVerticalFov:F4} deg vertical (projectionMatrix.m11={camera.projectionMatrix.m11:F5}, " +
            $"tan(half)={tanHalfLive:F5}) base={BaseVerticalFov:F4} deg vertical " +
            $"({UnzoomedVerticalFov:F1} + {offset:F1} from the FIELD OF VIEW setting, tan(half)={tanHalfBase:F5}) " +
            $"camera.fieldOfView={camera.fieldOfView:F4} aspect={camera.aspect:F4} factor={Factor:F4}");
    }

    /// <summary>
    /// The player's field-of-view offset in degrees, or -1 while the settings
    /// singleton has not been found.
    /// </summary>
    private float ReadSettingsFieldOfViewOffset()
    {
        if (!Resolve()) return -1f;

        object? master = GameMembers.ReadStatic(_settingsMasterType, "Instance");
        if (master == null) return -1f;

        object? data = _currentDataGetter!.Invoke(master, null);
        if (data == null) return -1f;

        object? player = _playerGetter!.Invoke(data, null);
        if (player == null) return -1f;

        return (float)_fieldOfViewField!.GetValue(player)!;
    }

    private bool Resolve()
    {
        if (_fieldOfViewField != null) return true;

        if (--_framesUntilRetry > 0) return false;
        _framesUntilRetry = SearchRetryFrames;

        _settingsMasterType ??= AccessTools.TypeByName("BluePrince.Settings.SettingsMaster");
        if (_settingsMasterType == null)
        {
            LogMissingSettings("BluePrince.Settings.SettingsMaster was not found");
            return false;
        }

        _currentDataGetter = GameMembers.FindGetter(_settingsMasterType, "CurrentData");
        if (_currentDataGetter == null)
        {
            LogMissingSettings("SettingsMaster.CurrentData is not readable");
            return false;
        }

        _playerGetter = GameMembers.FindGetter(_currentDataGetter.ReturnType, "Player");
        if (_playerGetter == null)
        {
            LogMissingSettings("SettingsData.Player is not readable");
            return false;
        }

        _fieldOfViewField = _playerGetter.ReturnType.GetField(
            "FieldOfView", BindingFlags.Public | BindingFlags.Instance);
        if (_fieldOfViewField == null)
        {
            LogMissingSettings("PlayerSettingsData.FieldOfView is not readable");
            return false;
        }

        return true;
    }

    /// <summary>
    /// Says once that the correction is off. A zoom correction that has quietly
    /// stopped correcting looks exactly like one that was never needed.
    /// </summary>
    private void LogMissingSettings(string reason)
    {
        if (_loggedMissingSettings) return;
        _loggedMissingSettings = true;

        HeadTrackingPlugin.Logger.LogWarning(
            $"Field-of-view setting unreadable ({reason}) - head tracking is applied at 1:1, so it will " +
            "feel stronger during anything that narrows the view");
    }

    /// <summary>
    /// Re-arms the retry counter so a scene that has not resolved the setting yet
    /// tries again on the next frame rather than waiting out the interval. Accessors
    /// that did resolve are kept: they are MethodInfo and FieldInfo on types that do
    /// not change between scenes. The basis latch is kept too, so re-entering
    /// gameplay on the same camera does not rewrite the same line - which happens on
    /// every menu and note close in this game.
    /// </summary>
    internal void Reset()
    {
        _framesUntilRetry = 0;
    }
}
