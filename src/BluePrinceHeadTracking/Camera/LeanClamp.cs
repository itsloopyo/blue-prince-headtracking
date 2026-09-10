// SPDX-License-Identifier: MIT
// Copyright (c) 2026 itsloopyo

using BluePrinceHeadTracking.Core;
using UnityEngine;

namespace BluePrinceHeadTracking.Camera;

/// <summary>
/// Cuts a positional lean down to whatever the room leaves free, so leaning into a
/// doorframe cannot put the rendered eye inside the wood and show the corridor
/// beyond it.
///
/// The sweep starts from the CLEAN eye - the position the game itself put the
/// camera at - and clamps before the offset is applied. Clamping afterwards would
/// mean reading back a position that is already inside the geometry.
///
/// <c>SphereCast</c> is the query rather than a line, and its radius IS the
/// standoff: Unity reports how far the sphere travelled, so its centre at contact
/// already sits one radius off the surface and no separate skin is needed. Unity
/// also ignores colliders the sphere overlaps at the start, which is what stops
/// the player's own capsule blocking every lean.
/// </summary>
internal sealed class LeanClamp
{
    // Below this the lean has no direction to sweep along.
    private const float MinLeanMetres = 1e-5f;

    // How close the eased allowance has to get to the requested lean before the
    // clamp declares the obstruction gone and stops limiting at all. A fraction of
    // the lean rather than a fixed distance, which is the form core uses: a fixed
    // one is either far too tight or far too loose depending on the engine's units,
    // and a too-tight one leaves the clamp reporting contact it is not applying.
    private const float AllowanceSettledFraction = 1e-3f;

    // The standoff floor, as a multiple of the camera's near clip distance. A
    // wall held exactly at the near plane is on the boundary of being culled, so
    // the floor sits clear of it.
    private const float NearClipStandoffFactor = 1.25f;

    // The release ease maps onto the fleet's 0-1 smoothing scale: the same
    // endpoints every other smoothing value in the fleet uses.
    private const float LightestReleaseSpeed = 50f;
    private const float HeaviestReleaseSpeed = 0.1f;

    private readonly float _configuredRadius;
    private readonly int _mask;

    // The configured 0-1 smoothing already mapped onto the exponential's speed.
    // 0.9 is a 200ms time constant on that scale.
    private readonly float _releaseSpeed;

    // The allowance is in metres along the current lean direction. Tightening is
    // instant; easing INTO a smaller allowance would leave the eye inside the
    // geometry for the duration of the ease, which is the whole bug.
    private float _allowance = float.MaxValue;

    private bool _inContact;
    private bool _loggedRadiusRaise;

    internal LeanClamp(float radius, int mask, float releaseSmoothing)
    {
        _configuredRadius = radius;
        _mask = mask;
        _releaseSpeed = Mathf.Lerp(LightestReleaseSpeed, HeaviestReleaseSpeed, releaseSmoothing);
    }

    /// <summary>Whether the lean is currently resting against something.</summary>
    internal bool InContact => _inContact;

    /// <summary>The allowance in metres, or -1 while nothing is limiting the lean.</summary>
    internal float Allowance => _allowance == float.MaxValue ? -1f : _allowance;

    /// <summary>
    /// Returns the offset trimmed to what the level leaves room for.
    /// </summary>
    /// <param name="cleanEye">The camera position the game set, before the lean.</param>
    /// <param name="worldOffset">The lean the tracker asked for, in world space.</param>
    /// <param name="dt">Frame time, for the release ease.</param>
    /// <param name="nearClipPlane">
    /// The camera's near clip distance. Geometry closer to the eye than this is not
    /// drawn, so a standoff smaller than it holds the wall at a distance where the
    /// player still sees straight through it.
    /// </param>
    internal Vector3 Clamp(Vector3 cleanEye, Vector3 worldOffset, float dt, float nearClipPlane)
    {
        float wanted = worldOffset.magnitude;
        if (wanted <= MinLeanMetres)
        {
            Reset();
            return worldOffset;
        }

        float radius = EffectiveRadius(nearClipPlane);
        Vector3 direction = worldOffset / wanted;

        // Overreach the requested lean by the standoff, so the sweep can see the
        // surface the lean is about to come to rest against rather than stopping
        // exactly where the lean stops and only reacting once the eye is through it.
        float sweepLength = wanted + radius;

        bool blocked = Physics.SphereCast(cleanEye, radius, direction, out RaycastHit hit, sweepLength,
            _mask, QueryTriggerInteraction.Ignore);

        // The swept hit's distance is where the sphere's CENTRE stopped, already
        // held one radius off the surface. Reading hit.point instead would throw
        // that backing-off away.
        //
        // A surface further away than the lean is not an allowance. The sweep
        // deliberately overreaches by the standoff so it can see what the lean is
        // about to rest against, and taking that hit as a limit latched an
        // allowance that cut nothing and then settled again on the next frame,
        // flipping the logged allowance every other frame beside any wall.
        float limit = blocked && hit.distance < wanted ? hit.distance : float.MaxValue;

        if (limit < _allowance)
        {
            _allowance = limit;
        }
        else if (_allowance != float.MaxValue)
        {
            // Release slowly, so stepping one centimetre past a doorframe opens the
            // lean back up smoothly instead of popping the view.
            float target = limit == float.MaxValue ? wanted : limit;
            float t = 1f - Mathf.Exp(-_releaseSpeed * dt);
            _allowance = Mathf.Lerp(_allowance, target, t);

            // Both conditions. Nothing may be cutting the lean this frame, and the
            // ease must have caught up with what was asked for. Dropping the first
            // let the allowance release while a surface was still limiting it;
            // dropping the second flipped the allowance between a real distance and
            // unrestricted on alternate frames whenever a wall sat at about the
            // lean distance.
            if (limit == float.MaxValue && _allowance >= wanted - wanted * AllowanceSettledFraction)
            {
                _allowance = float.MaxValue;
            }
        }

        // Reported after the ease rather than from the sweep. Stepping past a
        // doorframe clears the sweep a full release ahead of the allowance, and a
        // contact flag taken from the sweep reads False while the lean is still
        // being cut.
        _inContact = _allowance != float.MaxValue && _allowance < wanted;

        if (_allowance == float.MaxValue || wanted <= _allowance)
        {
            return worldOffset;
        }

        return direction * Mathf.Max(_allowance, 0f);
    }

    /// <summary>
    /// The standoff actually used. A configured radius smaller than the near clip
    /// plane would hold a wall at a distance where it is still culled, which is the
    /// same complaint with extra steps, so the near plane raises it.
    /// </summary>
    private float EffectiveRadius(float nearClipPlane)
    {
        float floor = nearClipPlane * NearClipStandoffFactor;
        if (_configuredRadius >= floor) return _configuredRadius;

        if (!_loggedRadiusRaise)
        {
            _loggedRadiusRaise = true;
            HeadTrackingPlugin.Logger.LogWarning(
                $"CollisionRadius {_configuredRadius:F3}m is inside the camera's near clip " +
                $"({nearClipPlane:F3}m) - raising it to {floor:F3}m, or a wall held at that distance " +
                "would still not be drawn");
        }
        return floor;
    }

    /// <summary>
    /// Puts the allowance back to unlimited. Called on any camera cut - a new
    /// camera, a level load, leaving gameplay - and on any frame that applies no
    /// lean at all, so the previous room's wall is not carried into the new one.
    /// </summary>
    internal void Reset()
    {
        _allowance = float.MaxValue;
        _inContact = false;
    }
}
