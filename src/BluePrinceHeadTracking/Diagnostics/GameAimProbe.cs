// SPDX-License-Identifier: MIT
// Copyright (c) 2026 itsloopyo

using System;
using System.Reflection;
using BluePrinceHeadTracking.Core;
using BluePrinceHeadTracking.Utilities;
using UnityEngine;

namespace BluePrinceHeadTracking.Diagnostics;

/// <summary>
/// Reports what the GAME thinks the player is pointing at, so aim decoupling can
/// be checked against the game's own answer rather than against the mod's.
///
/// This is the shooter's "two shots, one hole" test for a game with no gun: hold
/// the mouse still, apply a large head pose, and the collider Blue Prince picks
/// and the screen point it puts the pointer at must not move. If they do, no
/// amount of reticle maths will help - the hook is wrong.
///
/// Diagnostic only. Nothing in the mod's behaviour reads this.
/// </summary>
internal sealed class GameAimProbe
{
    private MethodInfo? _getHitCollider;
    private MethodInfo? _getScreenPoint;
    private bool _resolved;
    private string _lastTarget = string.Empty;

    /// <summary>
    /// Logs the moment the game's pick changes, with the pose that was applied when
    /// it did. The periodic AIMGEO line samples every couple of seconds, which is
    /// far too coarse to catch a target being lost the instant a head turn starts -
    /// and that is the whole question aim decoupling turns on.
    /// </summary>
    internal void LogTargetChange(float yaw, float pitch, float roll)
    {
        string target = ReadTarget();
        if (target == _lastTarget) return;

        string previous = _lastTarget;
        _lastTarget = target;
        HeadTrackingPlugin.Logger.LogInfo(
            $"GAMEPICK '{previous}' -> '{target}' at pose (y{yaw:F1},p{pitch:F1},r{roll:F1})");
    }

    private string ReadTarget()
    {
        Type? raycasterType = GameTypes.ClickableRaycaster;
        if (raycasterType == null) return "(type missing)";

        object? raycaster = GameMembers.ReadStatic(raycasterType, "Active");
        if (raycaster == null) return "(no raycaster)";

        ResolveAccessors(raycasterType);
        if (_getHitCollider == null) return "(GetCurrentHitCollider missing)";

        Collider? collider = _getHitCollider.Invoke(raycaster, null) as Collider;
        return collider != null ? collider.name : "(none)";
    }

    private void ResolveAccessors(Type raycasterType)
    {
        if (_resolved) return;
        _resolved = true;
        _getHitCollider = raycasterType.GetMethod(
            "GetCurrentHitCollider", BindingFlags.Public | BindingFlags.Instance, null, Type.EmptyTypes, null);
        _getScreenPoint = raycasterType.GetMethod(
            "GetCurrentScreenPoint", BindingFlags.Public | BindingFlags.Instance, null, Type.EmptyTypes, null);
    }

    internal string Describe()
    {
        Type? raycasterType = GameTypes.ClickableRaycaster;
        if (raycasterType == null) return "raycaster=(type missing)";

        object? raycaster = GameMembers.ReadStatic(raycasterType, "Active");
        if (raycaster == null) return "raycaster=(none)";

        if (!_resolved)
        {
            _resolved = true;
            _getHitCollider = raycasterType.GetMethod(
                "GetCurrentHitCollider", BindingFlags.Public | BindingFlags.Instance, null, Type.EmptyTypes, null);
            _getScreenPoint = raycasterType.GetMethod(
                "GetCurrentScreenPoint", BindingFlags.Public | BindingFlags.Instance, null, Type.EmptyTypes, null);
        }

        // An accessor the game no longer has must read differently from a genuine
        // miss: "(none)" against empty space and "(none)" against a renamed method
        // are the same string, and this line is the evidence aim decoupling is
        // checked against.
        string target;
        if (_getHitCollider == null)
        {
            target = "(GetCurrentHitCollider missing)";
        }
        else
        {
            // Unity's ==, not a pattern match: a collider the game cached before the
            // room was torn down is reference-non-null and dead, and reading .name
            // off it throws.
            Collider? collider = _getHitCollider.Invoke(raycaster, null) as Collider;
            target = collider != null ? collider.name : "(none)";
        }

        string screenPoint = _getScreenPoint == null
            ? "(GetCurrentScreenPoint missing)"
            : $"{_getScreenPoint.Invoke(raycaster, null)}";
        return $"gameTarget='{target}' gameScreenPoint={screenPoint}";
    }
}
