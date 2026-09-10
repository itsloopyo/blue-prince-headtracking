// SPDX-License-Identifier: MIT
// Copyright (c) 2026 itsloopyo

using System;
using BluePrinceHeadTracking.Core;
using BluePrinceHeadTracking.Utilities;
using UnityEngine;

namespace BluePrinceHeadTracking.Camera;

/// <summary>
/// Finds the surface the player is pointing at, along the CLEAN camera's forward
/// axis, so the reticle can be drawn on the point rather than on the direction.
///
/// The layers come from the game rather than from a guess. Blue Prince's own
/// <c>ClickableRaycaster</c> carries the two masks that decide what a click can
/// reach: <c>RaycastMask</c> for the clickable objects themselves and
/// <c>BlockerMask</c> for the geometry that occludes them. Their union is exactly
/// the set of surfaces a player perceives their pointer as landing on, which is
/// why this reads them instead of filtering on object names - a name blacklist can
/// never be finished, and a trigger volume the player happens to be standing in
/// collapses the measured distance onto nothing.
/// </summary>
internal sealed class AimTrace
{
    // Blue Prince's rooms and its outdoor grounds both fit inside this, and the
    // parallax term the distance feeds is already under a pixel by the far end.
    private const float MaxDistance = 300f;

    // Below this the contact is the player's own body or a collider they are
    // standing inside, not a surface they are pointing at.
    private const float MinDistance = 0.05f;

    private const int MaskRetryFrames = 60;

    private int _framesUntilRetry;
    private bool _maskResolved;
    private int _mask;
    private int _loggedMask;
    private bool _hasLoggedMask;

    /// <summary>
    /// Casts along the clean aim and reports the distance to the surface it stops
    /// on.
    ///
    /// Three outcomes, and they are not interchangeable:
    /// <list type="bullet">
    /// <item>a hit, with the distance measured along the aim direction;</item>
    /// <item>a definite no-hit, which is a target at infinity - the caller projects
    /// the aim direction for that frame;</item>
    /// <item>an unusable cast, when the game's layers have not been read yet, which
    /// invalidates the reticle rather than substituting a distance.</item>
    /// </list>
    /// </summary>
    internal AimTraceResult Trace(Vector3 origin, Vector3 direction)
    {
        if (!ResolveMask())
        {
            return AimTraceResult.Unavailable;
        }

        if (!Physics.Raycast(origin, direction, out RaycastHit hit, MaxDistance, _mask,
                QueryTriggerInteraction.Ignore))
        {
            return AimTraceResult.NoHit;
        }

        // The contact's own world position projected onto the aim direction is a
        // distance by construction. hit.distance is that same quantity for a
        // zero-radius ray, and using it keeps the reticle on the current frame's
        // surface with no smoothing, rate limit or carried-over depth in the path.
        float distance = Vector3.Dot(hit.point - origin, direction);
        if (distance < MinDistance)
        {
            return AimTraceResult.NoHit;
        }

        return AimTraceResult.Hit(distance, hit.collider != null ? hit.collider.gameObject.layer : -1);
    }

    /// <summary>
    /// Reads the interaction layers off the game's raycaster. Retried on an
    /// interval because the raycaster does not exist on the menu.
    /// </summary>
    private bool ResolveMask()
    {
        if (_maskResolved) return true;

        if (--_framesUntilRetry > 0) return false;
        _framesUntilRetry = MaskRetryFrames;

        Type? raycasterType = GameTypes.ClickableRaycaster;
        if (raycasterType == null) return false;

        object? raycaster = GameMembers.ReadStatic(raycasterType, "Active");
        if (raycaster == null) return false;

        object? clickable = GameMembers.ReadMember(raycasterType, raycaster, "RaycastMask");
        object? blockers = GameMembers.ReadMember(raycasterType, raycaster, "BlockerMask");
        if (clickable == null || blockers == null) return false;

        _mask = ((LayerMask)clickable).value | ((LayerMask)blockers).value;
        _maskResolved = true;

        // Only when the layers themselves differ from the last set reported. The
        // cache is dropped on every gameplay entry so a new level's raycaster is
        // read again, and in this game that is every menu and note close, which
        // wrote this line with identical masks hundreds of times a session.
        if (!_hasLoggedMask || _mask != _loggedMask)
        {
            _hasLoggedMask = true;
            _loggedMask = _mask;
            HeadTrackingPlugin.Logger.LogInfo(
                $"Aim layers read from ClickableRaycaster: clickable=0x{((LayerMask)clickable).value:X8} " +
                $"blockers=0x{((LayerMask)blockers).value:X8} union=0x{_mask:X8}");
        }

        return true;
    }

    /// <summary>Drops the cached mask so a new level's raycaster is read again.</summary>
    internal void Reset()
    {
        _maskResolved = false;
        _framesUntilRetry = 0;
    }
}

/// <summary>Outcome of one aim cast. See <see cref="AimTrace.Trace"/>.</summary>
internal readonly struct AimTraceResult
{
    internal enum Kind
    {
        /// <summary>The cast could not run; the reticle has no valid position.</summary>
        Unusable,

        /// <summary>Nothing in range: the aim point is at infinity.</summary>
        Infinity,

        /// <summary>A surface, at <see cref="Distance"/> along the aim direction.</summary>
        Surface
    }

    internal Kind Outcome { get; }

    internal float Distance { get; }

    /// <summary>Layer of the contact, for the diagnostic histogram.</summary>
    internal int Layer { get; }

    private AimTraceResult(Kind outcome, float distance, int layer)
    {
        Outcome = outcome;
        Distance = distance;
        Layer = layer;
    }

    internal static AimTraceResult Unavailable => new(Kind.Unusable, 0f, -1);

    internal static AimTraceResult NoHit => new(Kind.Infinity, 0f, -1);

    internal static AimTraceResult Hit(float distance, int layer) => new(Kind.Surface, distance, layer);
}
