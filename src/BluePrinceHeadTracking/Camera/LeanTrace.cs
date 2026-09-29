// SPDX-License-Identifier: MIT
// Copyright (c) 2026 itsloopyo

using BluePrinceHeadTracking.Core;
using CameraUnlock.Core.Data;
using CameraUnlock.Core.Processing;
using UnityEngine;

namespace BluePrinceHeadTracking.Camera;

/// <summary>
/// The engine half of the lean clamp: asks Unity's physics what lies between the clean
/// eye and where the lean wants to put it. Core's <see cref="LeanClamp"/> owns what is
/// done with the answer.
///
/// Two queries, because each misses something the other catches, and the nearer answer
/// wins:
/// <list type="bullet">
/// <item>A sphere of the standoff's radius swept along the lean. It is the shape the eye
/// actually needs kept clear and it cannot thread a gap narrower than itself. Unity leaves
/// out every collider the sphere already overlaps where it starts, which is what keeps the
/// player's own capsule from blocking every lean, and also what hides a surface that is
/// already within the standoff of the eye.</item>
/// <item>Core's <see cref="LineSweep"/> over <c>Physics.Raycast</c>: a ray down the centre
/// of the lean and a ring of rays around it, each behind a sideways probe. A ray only skips
/// a collider its origin is inside, so these see that surface, on the centre line or beside
/// it, and still pass through the player's capsule.</item>
/// </list>
///
/// Distances come back in core's convention: the travel the eye may make, plus the
/// standoff, which the clamp then takes off as its skin.
/// </summary>
internal sealed class LeanTrace
{
    // How far past the corners of the near clip plane the standoff must reach at the least.
    // A turned head can put a corner of the near plane, not its centre, nearest the wall,
    // and geometry inside the near plane is culled.
    private const float NearPlaneStandoffFactor = 1.25f;

    private readonly float _configuredStandoff;
    private readonly int _mask;
    private readonly LineSweep _lineSweep;
    private bool _loggedStandoffRaise;

    internal LeanTrace(float standoff, int mask)
    {
        _configuredStandoff = standoff;
        _mask = mask;
        _lineSweep = new LineSweep(Cast);
        Query = Trace;
    }

    /// <summary>Held once, so handing the query to the clamp allocates nothing per frame.</summary>
    internal LeanQuery Query { get; }

    /// <summary>The standoff in use: the configured one, or more where the near plane needs it.</summary>
    internal float Standoff { get; private set; }

    /// <summary>
    /// Sets the standoff for this frame from the camera's live projection. Returns it, for
    /// the clamp's skin, which must be the same number.
    /// </summary>
    internal float UpdateStandoff(UnityEngine.Camera camera)
    {
        // m00 and m11 are 1 / tan of the horizontal and vertical half angles, so a corner of
        // the near plane sits near * sqrt(1 + tanH^2 + tanV^2) from the eye. Read off the
        // matrix the frame is projected with, so a zoom that narrows the view is followed.
        Matrix4x4 projection = camera.projectionMatrix;
        float tanH = 1f / projection.m00;
        float tanV = 1f / projection.m11;
        float corner = camera.nearClipPlane * Mathf.Sqrt(1f + tanH * tanH + tanV * tanV);
        float floor = corner * NearPlaneStandoffFactor;

        Standoff = _configuredStandoff >= floor ? _configuredStandoff : floor;
        LineSweepSettings sweep = LineSweepSettings.Default;
        sweep.Radius = Standoff;
        _lineSweep.Settings = sweep;

        if (_configuredStandoff >= floor) return Standoff;

        if (!_loggedStandoffRaise)
        {
            _loggedStandoffRaise = true;
            HeadTrackingPlugin.Logger.LogWarning(
                $"CollisionMargin {_configuredStandoff:F3}m would let a wall inside the corners of the " +
                $"camera's near clip plane ({corner:F3}m from the eye at near={camera.nearClipPlane:F3}m) " +
                $"be culled - holding walls {floor:F3}m off instead");
        }
        return Standoff;
    }

    private LeanObstruction Trace(Vec3 start, Vec3 direction, float maxDistance)
    {
        float radius = Standoff;
        float lean = maxDistance - radius;
        var origin = new Vector3(start.X, start.Y, start.Z);
        var along = new Vector3(direction.X, direction.Y, direction.Z);

        float travel = lean;

        // The swept hit's distance is where the sphere's CENTRE stopped, already one radius
        // off the surface, so it is the eye's travel as it stands.
        if (Physics.SphereCast(origin, radius, along, out RaycastHit sphere, lean, _mask,
                QueryTriggerInteraction.Ignore))
        {
            travel = Mathf.Min(travel, sphere.distance);
        }

        LeanObstruction lines = _lineSweep.Query(start, direction, maxDistance);
        if (!lines.Queried) return lines;
        if (lines.Blocked)
        {
            travel = Mathf.Min(travel, lines.Distance - radius);
        }

        if (travel >= lean) return LeanObstruction.Clear;
        return LeanObstruction.Hit(Mathf.Max(travel, 0f) + radius);
    }

    private LineHit Cast(Vec3 start, Vec3 direction, float length)
    {
        if (!Physics.Raycast(new Vector3(start.X, start.Y, start.Z), new Vector3(direction.X, direction.Y, direction.Z),
                out RaycastHit hit, length, _mask, QueryTriggerInteraction.Ignore))
        {
            return LineHit.Miss;
        }

        Vector3 normal = hit.normal;
        return LineHit.At(hit.distance, new Vec3(normal.x, normal.y, normal.z));
    }
}
