// SPDX-License-Identifier: MIT
// Copyright (c) 2026 itsloopyo

using UnityEngine;

namespace BluePrinceHeadTracking.Camera;

/// <summary>
/// Owns everything the mod writes onto the camera.
///
/// Blue Prince renders through the built-in pipeline, so
/// <c>worldToCameraMatrix</c> alone decides both what is drawn and what is culled.
/// The lean therefore rides the same matrix as the rotation and the camera's
/// transform is never written at all: <c>camera.transform.position</c> and
/// <c>.rotation</c> stay exactly the pose <c>FirstPersonController</c>
/// set, so every piece of game code that reads the transform - movement, audio,
/// the interaction ray's origin - sees a camera with no head tracking in it.
///
/// The matrix is never written outside the render phase. <see cref="Write"/> only
/// computes and arms it; <see cref="RenderViewInjector"/> puts it on the camera in
/// OnPreCull and takes it off again in OnPostRender. That is what keeps every
/// query the game makes during its own Update and LateUpdate - the interaction
/// raycast above all - on the pose the mouse is aiming with, without depending on
/// script execution order, which Unity does not honour for a runtime-injected
/// class.
/// </summary>
internal sealed class CameraViewWriter
{
    private UnityEngine.Camera? _camera;
    private Transform? _transform;

    // Armed by Write for this frame's render, and applied only between OnPreCull
    // and OnPostRender.
    private Matrix4x4 _pendingView;
    private bool _pending;
    private bool _appliedToCamera;

    internal UnityEngine.Camera? Camera => _camera;

    internal Transform? Transform => _transform;

    /// <summary>Whether a live camera and its transform are both attached.</summary>
    internal bool HasCamera => _camera != null && _transform != null;

    internal void Attach(UnityEngine.Camera camera, Transform transform)
    {
        _camera = camera;
        _transform = transform;
    }

    internal void Detach()
    {
        _camera = null;
        _transform = null;
        _pending = false;
        _appliedToCamera = false;
    }

    /// <summary>
    /// Computes the head-tracked view, publishes it to <see cref="TrackedView"/>
    /// for everything that projects through it, and arms it for this frame's
    /// render. The camera itself is not touched here.
    /// </summary>
    internal void Write(Quaternion renderRotation, Vector3 renderPosition)
    {
        UnityEngine.Camera camera = _camera!;

        _pendingView = TrackedView.BuildViewMatrix(renderRotation, renderPosition);
        _pending = true;

        TrackedView.Publish(camera, _pendingView, renderRotation, renderPosition);
    }

    /// <summary>
    /// Puts the armed view on the camera. Called from OnPreCull, so the frame is
    /// culled and drawn with it.
    /// </summary>
    internal void ApplyForRender()
    {
        if (!_pending || _camera == null) return;

        _camera.worldToCameraMatrix = _pendingView;
        _appliedToCamera = true;
    }

    /// <summary>
    /// Hands the camera back to Unity the moment it has finished drawing, so it
    /// derives its view from the transform again for the whole of the next frame's
    /// game logic.
    /// </summary>
    internal void ClearAfterRender()
    {
        if (!_appliedToCamera) return;
        _appliedToCamera = false;

        if (_camera != null)
        {
            _camera.ResetWorldToCameraMatrix();
        }
    }

    /// <summary>
    /// Stops driving the camera: disarms this frame's view and takes off anything
    /// already applied. Called when tracking stops for any reason - out of
    /// gameplay, tracker data gone stale, camera swapped, mod torn down.
    /// </summary>
    internal void ResetView()
    {
        _pending = false;
        ClearAfterRender();
    }
}
