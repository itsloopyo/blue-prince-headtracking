// SPDX-License-Identifier: MIT
// Copyright (c) 2026 itsloopyo

using BluePrinceHeadTracking.Camera;
using BluePrinceHeadTracking.Configuration;
using BluePrinceHeadTracking.Diagnostics;
using BluePrinceHeadTracking.Input;
using BluePrinceHeadTracking.State;
using CameraUnlock.Core.Data;
using CameraUnlock.Core.Math;
using CameraUnlock.Core.Processing;
using CameraUnlock.Core.Protocol;
using UnityEngine;

namespace BluePrinceHeadTracking.Core;

/// <summary>
/// Drives head tracking for Blue Prince.
///
/// This LateUpdate reads the clean camera pose the game's own controller set and
/// computes the head-tracked view from it. It does not write that view onto the
/// camera: <see cref="RenderViewInjector"/> does, in the render phase, and takes it
/// straight back off afterwards.
///
/// The camera transform is never written either, in rotation or in position. The
/// two together are what decouple look from aim here. <c>ClickableRaycaster</c>
/// builds the interaction ray from the camera's <c>worldToCameraMatrix</c>, so the
/// game has to find that matrix untouched whenever it runs, and script execution
/// order cannot be used to arrange that - Unity bakes <c>[DefaultExecutionOrder]</c>
/// at build time and knows nothing about a class injected into IL2CPP at runtime.
/// </summary>
public class HeadTrackingBehaviour : MonoBehaviour
{
    // Applied after the core pipeline, so an extreme head pose cannot swing the
    // view somewhere the player cannot recover from. Yaw has no limit of its own:
    // the tangent round trip in ZoomCompensation.ScaleAngle already holds it inside
    // the pole, and every value it can return is under any yaw ceiling worth
    // setting, so a clamp here would never bind.
    private const float MaxPitch = 70f;
    private const float MaxRoll = 45f;

    // Below this the clean view has no heading left in its forward axis, which
    // happens only when the player is looking straight up or straight down.
    private const float MinFlatForwardSqrMagnitude = 1e-6f;

    private OpenTrackReceiver? _receiver;
    private TrackingProcessor? _processor;
    private PositionProcessor? _positionProcessor;
    private PositionInterpolator? _positionInterpolator;
    private CameraFinder? _cameraFinder;
    private HotkeyHandler? _hotkeyHandler;
    private GameplayStateDetector? _stateDetector;
    private LeanClamp? _leanClamp;
    private RenderViewInjector? _injector;

    private readonly CameraViewWriter _viewWriter = new();
    private readonly GameFieldOfView _fieldOfView = new();
    private readonly AimReticle _reticle = new();
    private readonly WindowPlacement _windowPlacement = new();

    private bool _trackingEnabled = true;
    private bool _positionEnabled = true;
    private bool _rotationEnabled = true;
    private bool _worldSpaceYaw = true;
    private bool _showReticle = true;
    private bool _pauseOnLostFocus = true;
    private bool _collisionEnabled = true;

    private bool _initialized;
    private bool _loggedFirstPacket;
    private bool _trackerReceiving;
    private bool _seenTrackerData;

    internal void Initialize(OpenTrackReceiver receiver, TrackingProcessor processor,
        PositionProcessor positionProcessor, PositionInterpolator positionInterpolator, PluginConfig config)
    {
        _receiver = receiver;
        _processor = processor;
        _positionProcessor = positionProcessor;
        _positionInterpolator = positionInterpolator;

        _trackingEnabled = config.EnabledOnStartup.Value;
        _worldSpaceYaw = config.WorldSpaceYaw.Value;
        _positionEnabled = config.PositionEnabled.Value;
        _showReticle = config.ShowReticle.Value;
        _pauseOnLostFocus = config.PauseOnLostFocus.Value;
        _collisionEnabled = config.CollisionEnabled.Value;

        _leanClamp = new LeanClamp(
            config.CollisionRadius.Value,
            Physics.DefaultRaycastLayers,
            config.CollisionReleaseSmoothing.Value);

        _cameraFinder = new CameraFinder();
        _cameraFinder.OnCameraChanged += OnCameraChanged;

        _hotkeyHandler = new HotkeyHandler(config, this);
        _hotkeyHandler.LogBindings();

        _stateDetector = new GameplayStateDetector();
        _stateDetector.OnGameplayStateChanged += OnGameplayStateChanged;

        bool diagnosticLogging = config.DiagnosticLogging.Value;
        RigProbe.Enabled = diagnosticLogging;
        _stateDetector.DiagnosticLogging = diagnosticLogging;

        _reticle.Initialize();

        _initialized = true;
        HeadTrackingPlugin.Logger.LogInfo("Head tracking behaviour initialized");
    }

    private void OnCameraChanged(Transform? newCamera)
    {
        _viewWriter.ResetView();
        _reticle.Reset();
        _leanClamp!.Reset();
        _fieldOfView.Reset();
        RigProbe.Reset();

        if (newCamera == null)
        {
            DetachInjector();
            _viewWriter.Detach();
            TrackedView.HasActiveViewMatrix = false;
            ResetSmoothing();
            return;
        }

        _viewWriter.Attach(newCamera.GetComponent<UnityEngine.Camera>(), newCamera);
        AttachInjector(newCamera.gameObject);
        ResetSmoothing();
    }

    /// <summary>
    /// Puts the render-phase injector on the camera. OnPreCull and OnPostRender
    /// only reach a component sitting on the camera's own GameObject, so it rides
    /// the game's object rather than the mod's.
    ///
    /// Kept across a re-resolve of the same camera. Closing a note or the map
    /// invalidates the search, which in this game happens hundreds of times a
    /// session, and each one would otherwise add and destroy a component on an
    /// object the mod does not own.
    /// </summary>
    private void AttachInjector(GameObject cameraObject)
    {
        if (_injector != null && _injector.gameObject == cameraObject)
        {
            RenderViewInjector.Writer = _viewWriter;
            return;
        }

        DetachInjector();
        _injector = cameraObject.AddComponent<RenderViewInjector>();
        RenderViewInjector.Writer = _viewWriter;
    }

    private void DetachInjector()
    {
        RenderViewInjector.Writer = null;
        if (_injector == null) return;

        Object.Destroy(_injector);
        _injector = null;
    }

    private void OnGameplayStateChanged(bool inGameplay)
    {
        if (inGameplay)
        {
            HeadTrackingPlugin.Logger.LogInfo("Entered gameplay - head tracking active");
            _cameraFinder?.InvalidateCache();
        }
        else
        {
            HeadTrackingPlugin.Logger.LogInfo("Left gameplay - head tracking paused");
            Deactivate();
            _processor?.Reset();
            ResetSmoothing();
        }
    }

    private void Update()
    {
        if (!_initialized) return;

        // Hotkeys first. The state poll reflects into game code, and a throw there
        // would take the rest of this method with it, leaving tracking latched on
        // with no way for the player to switch it off.
        _hotkeyHandler!.ProcessInput();
        _stateDetector!.Poll();
        PollTrackerConnection();

        // Placement is about the window rather than about tracking, so it runs
        // whatever state the mod is in: a window that opened off to one side is
        // still off to one side with tracking toggled off or the game paused.
        _windowPlacement.Update();
    }

    /// <summary>
    /// Reports the tracker going quiet and coming back. Edge triggered, so the log
    /// carries one line per real change; the receiver only drops IsReceiving after
    /// five seconds of silence, which is far longer than any gap a working tracker
    /// leaves. The first connection is not reported here because the receiver
    /// already names the endpoint it accepted its first packet from.
    /// </summary>
    private void PollTrackerConnection()
    {
        bool receiving = _receiver!.IsReceiving;
        if (receiving == _trackerReceiving) return;
        _trackerReceiving = receiving;

        if (!_seenTrackerData)
        {
            _seenTrackerData = true;
            return;
        }

        HeadTrackingPlugin.Logger.LogInfo(receiving
            ? "Tracker data resumed"
            : "Tracker data stopped - nothing has arrived on the UDP port for five seconds");
    }

    private void LateUpdate()
    {
        if (!_initialized) return;

        if (_cameraFinder!.GetCamera() == null || !_viewWriter.HasCamera)
        {
            Deactivate();
            return;
        }

        // Read before the gameplay and freshness gates, so the field-of-view basis
        // is on the log with no tracker connected and no save loaded, and so a zoom
        // that starts while tracking is suppressed is already accounted for on the
        // frame tracking resumes.
        _fieldOfView.Update(_viewWriter.Camera!, _stateDetector!.IsInGameplay);

        if (!ShouldApplyHeadTracking() || !ApplyHeadTracking())
        {
            Deactivate();
        }
    }

    /// <summary>
    /// Leaves the camera exactly as the game set it for this frame, and drops the
    /// obstruction allowance so the previous room's wall is not carried forward.
    /// </summary>
    private void Deactivate()
    {
        _viewWriter.ResetView();
        _leanClamp?.Reset();
        _reticle.Restore();
        TrackedView.HasActiveViewMatrix = false;
    }

    /// <summary>
    /// Computes and applies head tracking for this frame. Returns false when there
    /// is no fresh tracking data, leaving the camera untouched.
    /// </summary>
    private bool ApplyHeadTracking()
    {
        TrackingPose rawPose = _receiver!.GetLatestPose();
        if (!rawPose.IsDataFresh)
        {
            return false;
        }

        LogFirstPoseOnce();

        float dt = Time.deltaTime;

        // Locality picks LocalSmoothing over RemoteSmoothing. Re-read every frame so
        // swapping a local tracker for a phone switches parameter without a restart.
        bool isRemote = _receiver.IsRemoteConnection;
        _processor!.IsRemoteConnection = isRemote;
        _positionProcessor!.IsRemoteConnection = isRemote;

        TrackingPose processed = _processor.Process(rawPose, dt);
        (float yaw, float pitch, float roll) = ToEngineAngles(processed);

        Transform cameraTransform = _viewWriter.Transform!;
        UnityEngine.Camera camera = _viewWriter.Camera!;

        // The camera's own world pose, after the game's controller has run and with
        // nothing of ours on it. This is what the game aims and picks with.
        Quaternion cleanRotation = cameraTransform.rotation;
        Vector3 cleanPosition = cameraTransform.position;
        TrackedView.CleanRotation = cleanRotation;
        TrackedView.CleanPosition = cleanPosition;

        RigProbe.VerifyProjection(camera, cleanRotation, cleanPosition);

        Quaternion renderRotation = ComposeRenderRotation(cleanRotation, yaw, pitch, roll);
        Vector3 wantedLean = ComputeLeanOffset(cleanRotation, processed, dt);
        Vector3 lean = ClampLean(cleanPosition, wantedLean, camera, dt);

        _viewWriter.Write(renderRotation, cleanPosition + lean);

        // Set last: the reticle gates on this flag and reads the cache the write
        // publishes, so raising it afterwards guarantees it never sees a torn cache.
        TrackedView.HasActiveViewMatrix = true;

        if (_showReticle)
        {
            _reticle.Apply();
        }

        RigProbe.DumpRig(cameraTransform, camera);
        // The clamp reports an unrestricted allowance both when the room is open and
        // when the sweep is switched off, so the line has to say which.
        RigProbe.Sample(_reticle, _leanClamp!, _collisionEnabled, camera, _fieldOfView, wantedLean,
            (yaw, pitch, roll));
        return true;
    }

    /// <summary>
    /// Latched once. The receiver logs its first accepted packet; this line is
    /// further down the chain and proves a fresh pose reached the camera, so a
    /// "no head tracking" report is answerable from the log alone.
    /// </summary>
    private void LogFirstPoseOnce()
    {
        if (_loggedFirstPacket) return;
        _loggedFirstPacket = true;

        HeadTrackingPlugin.Logger.LogInfo(
            $"First pose applied to the camera ({(_receiver!.IsRemoteConnection ? "remote" : "local")} source)");
    }

    /// <summary>
    /// The engine boundary for rotation, and the whole of it. The tracker reports
    /// positive yaw as looking right, positive pitch as looking up and positive
    /// roll as tilting left; Unity turns right for a positive rotation about world
    /// up and about its own z, but pitches the nose DOWN for a positive rotation
    /// about x. So yaw and roll pass through and pitch is mirrored.
    ///
    /// This is the conversion the shipped Unity mods in the fleet use with this
    /// exact composition - gone-home passes (yaw, -pitch, roll) into the same
    /// ApplyHeadRotationDecomposed maths, firewatch the same into the camera-local
    /// form - rather than the negate-yaw-and-roll form the C++ engines need, whose
    /// bases differ from Unity's. Never a user-facing setting, and never applied
    /// after the directional limits.
    ///
    /// The zoom correction rides on the same boundary. Yaw and pitch move the
    /// picture across the frame, so both are scaled by how much the game's current
    /// field of view magnifies it against the player's own setting; roll spins the
    /// picture about the view axis by the same angle at every field of view there
    /// is, so roll is left alone. The factor is 1.0 whenever nothing has narrowed
    /// the view, which is nearly always here.
    /// </summary>
    private (float Yaw, float Pitch, float Roll) ToEngineAngles(TrackingPose processed)
    {
        float zoom = _fieldOfView.Factor;
        return (
            ZoomCompensation.ScaleAngle(processed.Yaw, zoom),
            Mathf.Clamp(-ZoomCompensation.ScaleAngle(processed.Pitch, zoom), -MaxPitch, MaxPitch),
            Mathf.Clamp(processed.Roll, -MaxRoll, MaxRoll));
    }

    /// <summary>
    /// Composes the rendered rotation from the game's clean rotation and the head
    /// pose. World-space yaw turns the view about the world up axis, which keeps
    /// the horizon level; camera-local yaw turns it about the view's own up axis.
    /// </summary>
    private Quaternion ComposeRenderRotation(Quaternion cleanRotation, float yaw, float pitch, float roll)
    {
        if (!_rotationEnabled)
        {
            return cleanRotation;
        }

        if (_worldSpaceYaw)
        {
            return Quaternion.AngleAxis(yaw, Vector3.up) * cleanRotation * Quaternion.Euler(pitch, 0f, roll);
        }

        return cleanRotation * Quaternion.Euler(pitch, yaw, roll);
    }

    /// <summary>
    /// Runs the tracker's translation through the position pipeline and returns the
    /// lean as a world-space offset from the clean eye.
    ///
    /// The basis is horizon-locked: sideways and forward come from the clean view's
    /// heading flattened onto the ground plane, and up is world up. Taking the
    /// basis from the pitched view instead would send a forward lean into the floor
    /// whenever the player is looking down at something, which in this game is most
    /// of the time.
    /// </summary>
    private Vector3 ComputeLeanOffset(Quaternion cleanRotation, TrackingPose processed, float dt)
    {
        if (!_positionEnabled)
        {
            return Vector3.zero;
        }

        PositionData rawPosition = _receiver!.GetLatestPosition();
        PositionData interpolated = _positionInterpolator!.Update(rawPosition, dt);
        // The tracker-convention pose, not the engine-space one: the processor uses
        // it to subtract the arc a face point traces about the neck, which is a
        // physical property of the tracker rather than of the engine.
        Quat4 headRotation = QuaternionUtils.FromYawPitchRoll(processed.Yaw, processed.Pitch, processed.Roll);

        // Already box-clamped by the processor against the configured asymmetric
        // limits ([-LimitYDown, +LimitY], [-LimitZ, +LimitZBack]).
        Vec3 offset = _positionProcessor!.Process(interpolated, headRotation, dt);

        // The engine boundary for translation. The tracker and Unity agree that
        // positive x is the player's right and positive y is up, so those pass
        // through; the core pipeline's forward lean is NEGATIVE z while Unity's
        // forward is positive, so z flips. It flips HERE, after the processor's
        // clamp, because doing it through PositionSettings.InvertZ would land ahead
        // of the clamp and put the generous 0.40m forward budget on leaning back.
        //
        // Scaled for zoom in the same place and for the same reason as the angles:
        // a head offset seen at a given depth lands at d / (2 * D * tan(fov / 2))
        // of the frame, so the screen displacement a lean buys is exactly linear in
        // the factor. Applied after the processor's clamp, so the configured limits
        // stay a statement about how far a head may move rather than about pixels.
        float zoom = _fieldOfView.Factor;
        float localX = offset.X * zoom;
        float localY = offset.Y * zoom;
        float localZ = -offset.Z * zoom;

        Vector3 flatForward = Vector3.ProjectOnPlane(cleanRotation * Vector3.forward, Vector3.up);
        if (flatForward.sqrMagnitude < MinFlatForwardSqrMagnitude)
        {
            // Looking straight up or down leaves no heading in the forward axis, so
            // take it from the view's up axis, which is horizontal in that pose. It
            // points along the heading when looking down and against it when looking
            // up, so it carries the sign of the pitch it stood in for.
            Vector3 viewForward = cleanRotation * Vector3.forward;
            flatForward = Vector3.ProjectOnPlane(cleanRotation * Vector3.up, Vector3.up)
                          * -Mathf.Sign(Vector3.Dot(viewForward, Vector3.up));
        }
        flatForward.Normalize();
        Vector3 flatRight = Vector3.Cross(Vector3.up, flatForward);

        return flatRight * localX + Vector3.up * localY + flatForward * localZ;
    }

    /// <summary>
    /// Trims the lean to what the room leaves free. The standoff has to exceed the
    /// camera's near clip distance or the surface is culled and the player sees
    /// through it anyway, so the near plane is read from the camera rather than
    /// assumed.
    /// </summary>
    private Vector3 ClampLean(Vector3 cleanPosition, Vector3 wantedLean, UnityEngine.Camera camera, float dt)
    {
        if (!_collisionEnabled || wantedLean == Vector3.zero)
        {
            _leanClamp!.Reset();
            return wantedLean;
        }

        return _leanClamp!.Clamp(cleanPosition, wantedLean, dt, camera.nearClipPlane);
    }

    private bool ShouldApplyHeadTracking()
    {
        if (!_trackingEnabled)
        {
            return false;
        }

        GameplayStateDetector detector = _stateDetector!;
        if (!detector.IsInGameplay)
        {
            return false;
        }

        return !_pauseOnLostFocus || detector.IsApplicationFocused;
    }

    /// <summary>Turns head tracking on and off, leaving the camera alone while off.</summary>
    internal void ToggleTracking()
    {
        _trackingEnabled = !_trackingEnabled;
        HeadTrackingPlugin.Logger.LogInfo($"Head tracking {(_trackingEnabled ? "ENABLED" : "DISABLED")}");
    }

    /// <summary>
    /// Advances the tracking-mode cycle one step: rotation and position, then
    /// rotation only, then position only, then back.
    /// </summary>
    internal void CycleTrackingMode()
    {
        if (_rotationEnabled && _positionEnabled)
        {
            _positionEnabled = false;
        }
        else if (_rotationEnabled)
        {
            _rotationEnabled = false;
            _positionEnabled = true;
        }
        else
        {
            _rotationEnabled = true;
            _positionEnabled = true;
        }

        if (!_positionEnabled)
        {
            _leanClamp?.Reset();
            _positionProcessor?.Reset();
            _positionInterpolator?.Reset();
        }

        HeadTrackingPlugin.Logger.LogInfo(
            $"Tracking mode: rotation={(_rotationEnabled ? "on" : "off")}, " +
            $"position={(_positionEnabled ? "on" : "off")}");
    }

    internal void ToggleYawMode()
    {
        _worldSpaceYaw = !_worldSpaceYaw;
        HeadTrackingPlugin.Logger.LogInfo(
            $"Yaw mode: {(_worldSpaceYaw ? "horizon-locked" : "view-local")}");
    }

    private void ResetSmoothing()
    {
        _processor?.ResetSmoothing();
        _positionProcessor?.Reset();
        _positionInterpolator?.Reset();
    }

    private void OnDestroy()
    {
        Deactivate();
        _reticle.Dispose();
        DetachInjector();
        _viewWriter.Detach();

        if (_cameraFinder != null)
        {
            _cameraFinder.OnCameraChanged -= OnCameraChanged;
            _cameraFinder.Dispose();
        }

        _stateDetector?.Dispose();

        _initialized = false;
        HeadTrackingPlugin.Logger.LogInfo("Head tracking behaviour destroyed");
    }
}
