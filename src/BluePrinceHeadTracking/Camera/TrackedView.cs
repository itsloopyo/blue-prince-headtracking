// SPDX-License-Identifier: MIT
// Copyright (c) 2026 itsloopyo

using UnityEngine;

namespace BluePrinceHeadTracking.Camera;

/// <summary>
/// The head-tracked view the current frame is drawn with, published for the
/// consumers that run outside the tracking behaviour's own LateUpdate.
///
/// Everything the reticle needs is derived from the matrix that was actually
/// written onto the camera, never re-derived from the tracker's yaw/pitch/roll.
/// One derivation used twice cannot disagree with itself, which is what keeps the
/// reticle glued to its target on combined poses.
///
/// Plain static fields rather than auto-properties so reads are direct memory
/// loads in IL2CPP, not method-call getters.
/// </summary>
internal static class TrackedView
{
    /// <summary>Whether the view state below is valid this frame.</summary>
    internal static bool HasActiveViewMatrix;

    /// <summary>Head-tracked view matrix (rotation and lean).</summary>
    internal static Matrix4x4 RenderViewMatrix;

    /// <summary>Pre-computed <c>projectionMatrix * RenderViewMatrix</c>.</summary>
    internal static Matrix4x4 RenderVPMatrix;

    /// <summary>The camera's clean world rotation: the direction the player is aiming.</summary>
    internal static Quaternion CleanRotation;

    /// <summary>The camera's clean world position: where aim rays originate.</summary>
    internal static Vector3 CleanPosition;

    /// <summary>Final head-tracked world rotation of the rendered view.</summary>
    internal static Quaternion RenderRotation;

    /// <summary>Head-tracked world position of the rendered view.</summary>
    internal static Vector3 RenderPosition;

    /// <summary>Cached <c>pixelWidth</c>.</summary>
    internal static float ActivePixelWidth;

    /// <summary>Cached <c>pixelHeight</c>.</summary>
    internal static float ActivePixelHeight;

    /// <summary>
    /// Caches the frustum and viewport values the reticle consumes, so its hot
    /// path does no Camera property marshalling.
    ///
    /// The projection matrix, not fieldOfView plus aspect, is the field of view the
    /// frame is drawn with, and reading it back is also what keeps the reticle
    /// correct through any field-of-view change the game makes.
    ///
    /// Does NOT raise <see cref="HasActiveViewMatrix"/>: consumers gate on that
    /// flag and read this cache, so the caller sets it afterwards to guarantee they
    /// never see a torn cache.
    /// </summary>
    internal static void Publish(UnityEngine.Camera camera, Matrix4x4 view,
        Quaternion renderRotation, Vector3 renderPosition)
    {
        RenderViewMatrix = view;
        RenderRotation = renderRotation;
        RenderPosition = renderPosition;
        RenderVPMatrix = camera.projectionMatrix * view;

        ActivePixelWidth = camera.pixelWidth;
        ActivePixelHeight = camera.pixelHeight;
    }

    /// <summary>
    /// Builds a Unity view matrix from a world rotation and position. Unity's
    /// <c>worldToCameraMatrix</c> looks down -Z, so the third row is negated.
    /// </summary>
    internal static Matrix4x4 BuildViewMatrix(Quaternion rotation, Vector3 position)
    {
        Matrix4x4 m = Matrix4x4.Rotate(Quaternion.Inverse(rotation)) * Matrix4x4.Translate(-position);
        m.m20 = -m.m20;
        m.m21 = -m.m21;
        m.m22 = -m.m22;
        m.m23 = -m.m23;
        return m;
    }

    // Points at or behind the rendered view's plane have no meaningful screen
    // position; the reciprocal blows up as w approaches zero, and a reticle at 1e30
    // is a NaN on its way to a vertex buffer.
    private const float MinClipW = 1e-4f;

    /// <summary>
    /// Projects a world point through the head-tracked view. Returns false when the
    /// point is at or behind the rendered camera's plane.
    /// </summary>
    internal static bool TryProjectPoint(Vector3 worldPoint, out Vector2 screenPoint)
    {
        return Project(new Vector4(worldPoint.x, worldPoint.y, worldPoint.z, 1f), out screenPoint);
    }

    /// <summary>
    /// Projects a world DIRECTION through the head-tracked view - a target at
    /// infinity, which is what a definite no-hit down the aim ray is.
    /// </summary>
    internal static bool TryProjectDirection(Vector3 worldDirection, out Vector2 screenPoint)
    {
        return Project(new Vector4(worldDirection.x, worldDirection.y, worldDirection.z, 0f), out screenPoint);
    }

    private static bool Project(Vector4 homogeneous, out Vector2 screenPoint)
    {
        Vector4 clip = RenderVPMatrix * homogeneous;

        // Unity's managed projectionMatrix is OpenGL convention, so clip.w is
        // -viewSpaceZ and is positive for everything in front of the camera.
        if (clip.w <= MinClipW)
        {
            screenPoint = default;
            return false;
        }

        float invW = 1f / clip.w;
        screenPoint = new Vector2(
            (clip.x * invW * 0.5f + 0.5f) * ActivePixelWidth,
            (clip.y * invW * 0.5f + 0.5f) * ActivePixelHeight);
        return true;
    }
}
