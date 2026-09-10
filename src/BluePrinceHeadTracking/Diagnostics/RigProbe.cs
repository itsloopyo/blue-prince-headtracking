// SPDX-License-Identifier: MIT
// Copyright (c) 2026 itsloopyo

using System.Text;
using BluePrinceHeadTracking.Camera;
using BluePrinceHeadTracking.Core;
using BluePrinceHeadTracking.Utilities;
using UnityEngine;

namespace BluePrinceHeadTracking.Diagnostics;

/// <summary>
/// The diagnostic lines that answer a "head tracking is wrong" report from the log
/// alone.
///
/// Everything a reticle fault depends on lands on ONE line and on the same frame:
/// the pose the camera was actually written with, the lean, the distance the aim
/// cast returned and the resulting screen offset. Reading the distance off one
/// line and the pose off another is how a scaling fault and a distance fault come
/// to look alike.
/// </summary>
internal static class RigProbe
{
    private const int SampleIntervalFrames = 120;

    internal static bool Enabled;

    private static readonly GameAimProbe AimProbe = new();

    private static int _framesUntilSample;
    private static bool _dumpedRig;
    private static bool _verifiedProjection;

    /// <summary>
    /// Checks our projection against the engine's own, once, using the CLEAN view.
    /// Both sides then describe the same camera, so a disagreement is arithmetic
    /// rather than a difference of pose - and agreement rules out the whole class
    /// of handedness and clip-convention faults before any reticle maths is
    /// trusted.
    /// </summary>
    internal static void VerifyProjection(UnityEngine.Camera camera, Quaternion cleanRotation, Vector3 cleanPosition)
    {
        if (!Enabled || _verifiedProjection) return;
        _verifiedProjection = true;

        Matrix4x4 cleanView = TrackedView.BuildViewMatrix(cleanRotation, cleanPosition);
        Matrix4x4 vp = camera.projectionMatrix * cleanView;

        Vector3 forward = cleanRotation * Vector3.forward;
        Vector3 right = cleanRotation * Vector3.right;
        Vector3 up = cleanRotation * Vector3.up;

        var sb = new StringBuilder();
        sb.Append("PROJCHECK ");
        foreach (Vector3 probe in new[]
                 {
                     cleanPosition + forward * 5f,
                     cleanPosition + forward * 5f + right * 1.5f,
                     cleanPosition + forward * 5f + up * 1.5f
                 })
        {
            Vector4 clip = vp * new Vector4(probe.x, probe.y, probe.z, 1f);
            Vector2 ours = new(
                (clip.x / clip.w * 0.5f + 0.5f) * camera.pixelWidth,
                (clip.y / clip.w * 0.5f + 0.5f) * camera.pixelHeight);
            Vector3 engine = camera.WorldToScreenPoint(probe);
            sb.Append($"[ours=({ours.x:F1},{ours.y:F1}) engine=({engine.x:F1},{engine.y:F1}) w={clip.w:F3}] ");
        }

        HeadTrackingPlugin.Logger.LogInfo(sb.ToString());
    }

    /// <summary>
    /// One-shot dump of the rig the camera sits in and every component on it, so a
    /// camera that moved between game builds can be identified without attaching a
    /// debugger.
    /// </summary>
    internal static void DumpRig(Transform cameraTransform, UnityEngine.Camera camera)
    {
        if (!Enabled || _dumpedRig) return;
        _dumpedRig = true;

        var sb = new StringBuilder();
        sb.AppendLine($"RIG camera={TransformPath.GetFullPath(cameraTransform)}");
        sb.AppendLine($"RIG fov={camera.fieldOfView:F2} near={camera.nearClipPlane:F3} far={camera.farClipPlane:F1} " +
                      $"aspect={camera.aspect:F4} depth={camera.depth} mask=0x{camera.cullingMask:X8} " +
                      $"pixels={camera.pixelWidth}x{camera.pixelHeight} ortho={camera.orthographic}");
        sb.AppendLine($"RIG localPos={cameraTransform.localPosition} localRot={cameraTransform.localEulerAngles}");

        foreach (object component in GameMembers.ComponentsOn(cameraTransform.gameObject))
        {
            if (component == null) continue;
            sb.AppendLine($"RIG component {GameMembers.Il2CppTypeName(component)}");
        }

        Transform? parent = cameraTransform.parent;
        int depth = 0;
        while (parent != null && depth < 6)
        {
            var names = new StringBuilder();
            foreach (object component in GameMembers.ComponentsOn(parent.gameObject))
            {
                if (component == null) continue;
                names.Append(GameMembers.Il2CppTypeName(component)).Append(' ');
            }
            sb.AppendLine($"RIG ancestor[{depth}] {parent.name}: {names}");
            parent = parent.parent;
            depth++;
        }

        HeadTrackingPlugin.Logger.LogInfo(sb.ToString().TrimEnd());
    }

    /// <summary>
    /// The periodic line. Every term a reticle or axis fault can live in, on one
    /// line, for one frame.
    /// </summary>
    internal static void Sample(AimReticle reticle, LeanClamp leanClamp, bool collisionEnabled,
        UnityEngine.Camera camera, GameFieldOfView fieldOfView, Vector3 leanWorld,
        (float Yaw, float Pitch, float Roll) applied)
    {
        if (!Enabled) return;

        // Every frame, ahead of the interval gate: a target lost the instant a head
        // turn starts is invisible at one sample every two seconds.
        AimProbe.LogTargetChange(applied.Yaw, applied.Pitch, applied.Roll);

        if (--_framesUntilSample > 0) return;
        _framesUntilSample = SampleIntervalFrames;

        Vector3 lean = TrackedView.RenderPosition - TrackedView.CleanPosition;

        HeadTrackingPlugin.Logger.LogInfo(
            $"AIMGEO applied=(y{applied.Yaw:F2},p{applied.Pitch:F2},r{applied.Roll:F2}) " +
            $"fov={camera.fieldOfView:F2} " +
            $"live={fieldOfView.LiveVerticalFov:F2} base={fieldOfView.BaseVerticalFov:F2} " +
            $"zoom={fieldOfView.Factor:F4} " +
            $"lean={lean.magnitude:F3}m wanted={leanWorld.magnitude:F3}m " +
            $"dist={reticle.LastAimDistance:F2} layer={reticle.LastAimLayer} " +
            $"offset=({reticle.LastOffset.x:F1},{reticle.LastOffset.y:F1})px " +
            $"{reticle.Describe()} markerApplied={reticle.LastApplied} " +
            $"clamp(enabled={collisionEnabled} contact={leanClamp.InContact} " +
            $"allow={leanClamp.Allowance:F3}) " +
            $"screen={TrackedView.ActivePixelWidth}x{TrackedView.ActivePixelHeight} " +
            AimProbe.Describe());
    }

    /// <summary>Clears the one-shot latches so a new camera dumps its own rig.</summary>
    internal static void Reset()
    {
        _dumpedRig = false;
        _verifiedProjection = false;
    }
}
