// SPDX-License-Identifier: MIT
// Copyright (c) 2026 itsloopyo

using System;
using System.Text;
using BluePrinceHeadTracking.Core;
using BluePrinceHeadTracking.Utilities;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace BluePrinceHeadTracking.Camera;

/// <summary>
/// Locates the camera the player looks through, and holds on to it.
///
/// It is identified by the first-person controller on its own rig, never by
/// <c>Camera.main</c>. Blue Prince tags more than one camera MainCamera and
/// raises overlay cameras over the world - drafting a room brings up
/// <c>UI OVERLAY CAM</c> - so <c>Camera.main</c> is a coin flip the moment one of
/// them is up. The mod followed it there once: the world froze, because nothing
/// was driving the first-person camera any more, and turning the head slid the
/// overlay's own render around instead, showing black wedges at the edges of the
/// frame where it had nothing to draw. Worse, the overlay camera stays enabled
/// afterwards, so the old code never went looking again and head tracking was
/// broken for the rest of the session.
///
/// So the search walks the live cameras and picks the one carrying a first-person
/// controller on itself or an ancestor. The match is on the tail of the type name
/// rather than on a class picked in advance: the assembly carries
/// <c>RigidbodyFirstPersonController</c> and a search for it finds nothing, because
/// the shipped rig runs <c>UnityStandardAssets.Characters.FirstPerson.FirstPersonController</c>
/// instead.
///
/// A camera that is merely DISABLED is still the player's camera. The overlay
/// screens switch it off and put it back, so it is held across that rather than
/// replaced, and tracking simply stops while it is down. Only a destroyed camera -
/// a scene change - starts a new search.
///
/// The mansion's mirrors render through their own cameras, but those are created
/// and driven by <c>MirrorReflectionScript</c> from the player camera's matrices,
/// so they follow the head-tracked view without being touched.
/// </summary>
internal sealed class CameraFinder : IDisposable
{
    // The search reflects into the game and walks the scene, so re-running it every
    // frame while no camera exists - during a load, or while an overlay has the
    // player camera down - is wasteful. One retry every 30 frames is half a second
    // at worst, which nobody can see against a loading screen.
    private const int SearchRetryFrames = 30;

    // How far up a camera's parents to look for the controller. The player rig is
    // camera -> controller -> holder here, and a bound stops a deep UI hierarchy
    // from being walked in full on every search.
    private const int RigSearchDepth = 4;

    private Transform? _cached;
    private UnityEngine.Camera? _cachedCamera;
    private bool _attached;
    private int _framesUntilRetry;
    private int _lastLoggedCameraId;
    private string _dumpedScene = string.Empty;
    private bool _disposed;

    /// <summary>Fired when the camera reference changes: a new camera, or null when lost.</summary>
    internal event Action<Transform?>? OnCameraChanged;

    /// <summary>Drop the cached camera so the next <see cref="GetCamera"/> re-searches.</summary>
    internal void InvalidateCache()
    {
        if (!_attached) return;

        _cached = null;
        _cachedCamera = null;
        _attached = false;
        _framesUntilRetry = 0;
        OnCameraChanged?.Invoke(null);
    }

    /// <summary>
    /// The player camera's transform, or null while there is nothing to drive.
    /// Null covers both "not found yet" and "found, but the game has it switched
    /// off"; the caller stops tracking either way.
    /// </summary>
    internal Transform? GetCamera()
    {
        if (_disposed) return null;

        if (_attached)
        {
            // Unity's ==, which is null for a destroyed object and not for a merely
            // disabled one. Destroyed means the scene went, so search again.
            if (_cachedCamera == null)
            {
                InvalidateCache();
                // Rate limited like a search that has never found a camera.
                // InvalidateCache re-arms for an immediate retry, which is right
                // when gameplay is entered and wrong here: the object is gone and
                // its replacement will not exist for several frames.
                _framesUntilRetry = SearchRetryFrames;
                return null;
            }

            // Disabled but alive. Hold it, and let the caller stop.
            return _cachedCamera.isActiveAndEnabled ? _cached : null;
        }

        if (--_framesUntilRetry > 0)
        {
            return null;
        }
        _framesUntilRetry = SearchRetryFrames;

        UnityEngine.Camera? camera = FindPlayerCamera();
        if (camera == null || !camera.isActiveAndEnabled)
        {
            return null;
        }

        _cached = camera.transform;
        _cachedCamera = camera;
        _attached = true;

        // Compared by instance id rather than announced on every resolve. Entering
        // gameplay invalidates the cache, and in this game that happens every time a
        // note, the map or the pause menu closes, so the same camera resolves
        // hundreds of times a session and each copy of this line pushes the startup
        // lines further out of reach.
        int cameraId = _cached.GetInstanceID();
        if (cameraId != _lastLoggedCameraId)
        {
            _lastLoggedCameraId = cameraId;

            // Camera.main is on the line because it is what this used to trust, and
            // the two disagreeing is the whole of the bug that moved it off it. A
            // report of head tracking on the wrong camera is answerable from here.
            UnityEngine.Camera mainCamera = UnityEngine.Camera.main;
            string main = mainCamera != null ? TransformPath.GetFullPath(mainCamera.transform) : "(none)";
            HeadTrackingPlugin.Logger.LogInfo(
                $"Head tracking camera: {TransformPath.GetFullPath(_cached)} (Camera.main is {main})");
        }

        OnCameraChanged?.Invoke(_cached);
        return _cached;
    }

    // Matched on the tail of the IL2CPP type name so any first-person controller
    // the game ships answers to it, rather than one class name that has already
    // proved not to be the one in use.
    private const string ControllerTypeSuffix = "FirstPersonController";

    /// <summary>
    /// The camera whose rig carries the first-person controller, or null when none
    /// is in the scene - the main menu, a loading screen, or an overlay that has
    /// taken the player rig down.
    ///
    /// Every camera and the components on its rig are logged the first time the
    /// search runs, because a report of head tracking on the wrong camera is only
    /// answerable from a list of what the alternatives were.
    /// </summary>
    private UnityEngine.Camera? FindPlayerCamera()
    {
        UnityEngine.Camera? player = null;
        // Keyed on the scene, not latched on the first search. The first search
        // runs during the boot scene, where the player camera does not exist yet, so
        // a once-per-session dump describes two overlay cameras and nothing else -
        // which is exactly what it did, and why this needed a second run to answer.
        string scene = SceneManager.GetActiveScene().name;
        var dump = scene == _dumpedScene ? null : new StringBuilder($"Cameras in scene '{scene}':");

        foreach (object entry in GameMembers.AllCameras())
        {
            if (entry is not UnityEngine.Camera camera || camera == null) continue;

            string rig = DescribeRig(camera.transform, out bool hasController);
            if (hasController && player == null) player = camera;

            dump?.Append($"\n  {TransformPath.GetFullPath(camera.transform)} " +
                         $"tag={camera.tag} depth={camera.depth} enabled={camera.isActiveAndEnabled} " +
                         $"controller={hasController} rig=[{rig}]");
        }

        if (dump != null)
        {
            _dumpedScene = scene;
            HeadTrackingPlugin.Logger.LogInfo(dump.ToString());
        }

        return player;
    }

    /// <summary>
    /// The component type names on a camera's own object and its ancestors, and
    /// whether any of them is a first-person controller.
    /// </summary>
    private static string DescribeRig(Transform cameraTransform, out bool hasController)
    {
        hasController = false;
        var names = new StringBuilder();

        Transform? node = cameraTransform;
        for (int depth = 0; node != null && depth < RigSearchDepth; depth++)
        {
            foreach (object component in GameMembers.ComponentsOn(node.gameObject))
            {
                if (component == null) continue;

                string name = GameMembers.Il2CppTypeName(component);
                if (name.EndsWith(ControllerTypeSuffix, StringComparison.Ordinal)) hasController = true;

                if (names.Length > 0) names.Append(' ');
                names.Append(name);
            }
            node = node.parent;
        }

        return names.ToString();
    }

    public void Dispose()
    {
        _disposed = true;
        _cached = null;
        _cachedCamera = null;
        _attached = false;
    }
}
