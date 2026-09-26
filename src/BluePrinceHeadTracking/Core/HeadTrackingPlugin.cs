// SPDX-License-Identifier: MIT
// Copyright (c) 2026 itsloopyo

using BepInEx;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using BluePrinceHeadTracking.Camera;
using BluePrinceHeadTracking.Configuration;
using BluePrinceHeadTracking.Legacy;
using CameraUnlock.Core.Data;
using CameraUnlock.Core.Processing;
using CameraUnlock.Core.Protocol;
using Il2CppInterop.Runtime.Injection;
using UnityEngine;

namespace BluePrinceHeadTracking.Core;

/// <summary>
/// BepInEx IL2CPP entry point. Builds the tracking pipeline, hosts the behaviour
/// on a scene-independent GameObject, and starts listening for the tracker.
/// </summary>
[BepInPlugin(PluginGuid, PluginName, PluginVersion)]
public class HeadTrackingPlugin : BasePlugin
{
    internal const string PluginGuid = "com.cameraunlock.blueprince.headtracking";
    internal const string PluginName = "Blue Prince Head Tracking";
    internal const string PluginVersion = "0.0.0";

    internal static ManualLogSource Logger { get; private set; } = null!;

    // Held for the lifetime of the plugin: BepInEx roots the plugin instance, so
    // these fields keep the managed graph alive under IL2CPP's GC.
    private static GameObject? _behaviourObject;
    private static HeadTrackingBehaviour? _behaviour;

    private OpenTrackReceiver? _receiver;
    private LogFile? _logFile;

    public override void Load()
    {
        Logger = Log;

        // Attached before the first line is logged, so the session's log file holds
        // the whole of Load() rather than starting part way through it.
        _logFile = LogFile.Attach(PluginName, $"{PluginName} v{PluginVersion}");

        ModConfig config = LoadConfig();

        _receiver = new OpenTrackReceiver();
        TrackingProcessor processor = BuildRotationProcessor(config);
        PositionProcessor positionProcessor = BuildPositionProcessor(config);
        var positionInterpolator = new PositionInterpolator();

        CreateBehaviour(_receiver, processor, positionProcessor, positionInterpolator, config);

        StartReceiver(_receiver, config.UdpPort);

        Logger.LogInfo($"{PluginName} v{PluginVersion} loaded - tracking is " +
                       $"{(config.EnabledOnStartup ? "ENABLED" : "DISABLED")} on startup");
    }

    /// <summary>
    /// Reads BepInEx/config/com.cameraunlock.blueprince.headtracking.cfg through the frozen
    /// reader, then writes the file once, as every earlier build's Bind calls did at each start.
    ///
    /// <c>ConfigFile.Bind</c> truncates and rewrites the whole file each time it adds an entry it
    /// has not seen, and it has seen none of them at startup, so the reader binds with saving on
    /// set turned off and the single save here turns a session's twenty rewrites into one.
    /// </summary>
    private ModConfig LoadConfig()
    {
        LegacyConfig legacy = LegacyConfigReader.Read(Config, out _);
        Config.Save();
        Config.SaveOnConfigSet = true;

        return new ModConfig
        {
            UdpPort = legacy.UdpPort,
            EnabledOnStartup = legacy.EnabledOnStartup,
            ShowReticle = legacy.ShowReticle,
            WorldSpaceYaw = legacy.WorldSpaceYaw,
            PauseOnLostFocus = legacy.PauseOnLostFocus,
            DiagnosticLogging = legacy.DiagnosticLogging,
            LocalSmoothing = legacy.LocalSmoothing,
            RemoteSmoothing = legacy.RemoteSmoothing,
            PositionEnabled = legacy.PositionEnabled,
            PositionLimitX = legacy.PositionLimitX,
            PositionLimitY = legacy.PositionLimitY,
            PositionLimitYDown = legacy.PositionLimitYDown,
            PositionLimitZ = legacy.PositionLimitZ,
            PositionLimitZBack = legacy.PositionLimitZBack,
            CollisionEnabled = legacy.CollisionEnabled,
            CollisionRadius = legacy.CollisionRadius,
            CollisionReleaseSmoothing = legacy.CollisionReleaseSmoothing,
            ToggleKey = legacy.ToggleKey,
            CycleTrackingModeKey = legacy.CycleTrackingModeKey,
            YawModeKey = legacy.YawModeKey
        };
    }

    /// <summary>
    /// The rotation pipeline runs at 1:1: no sensitivity, no deadzone, no inversion.
    /// The tracker owns pose shaping, and the axis signs this engine needs are
    /// applied once at the camera boundary rather than folded in here, where they
    /// would land ahead of the limits.
    /// </summary>
    private static TrackingProcessor BuildRotationProcessor(ModConfig config)
    {
        return new TrackingProcessor
        {
            LocalSmoothing = config.LocalSmoothing,
            RemoteSmoothing = config.RemoteSmoothing,
            Sensitivity = SensitivitySettings.Default,
            Deadzone = DeadzoneSettings.None
        };
    }

    private static PositionProcessor BuildPositionProcessor(ModConfig config)
    {
        return new PositionProcessor
        {
            Settings = new PositionSettings(
                1f, 1f, 1f,
                config.PositionLimitX,
                config.PositionLimitY,
                config.PositionLimitYDown,
                config.PositionLimitZ,
                config.PositionLimitZBack,
                localSmoothing: config.LocalSmoothing,
                remoteSmoothing: config.RemoteSmoothing,
                invertX: false, invertY: false, invertZ: false)
        };
    }

    /// <summary>
    /// Hosts the behaviour on a persistent GameObject. For IL2CPP: register the
    /// injected types, create the object, set the DontSave flag, then call
    /// DontDestroyOnLoad. The behaviour is held in a static field so IL2CPP's GC
    /// cannot collect it.
    /// </summary>
    private static void CreateBehaviour(OpenTrackReceiver receiver, TrackingProcessor processor,
        PositionProcessor positionProcessor, PositionInterpolator positionInterpolator, ModConfig config)
    {
        ClassInjector.RegisterTypeInIl2Cpp<RenderViewInjector>();
        ClassInjector.RegisterTypeInIl2Cpp<HeadTrackingBehaviour>();

        _behaviourObject = new GameObject("BluePrinceHeadTracking");
        _behaviourObject.hideFlags = HideFlags.DontSave;
        Object.DontDestroyOnLoad(_behaviourObject);

        _behaviour = _behaviourObject.AddComponent<HeadTrackingBehaviour>();
        _behaviour.Initialize(receiver, processor, positionProcessor, positionInterpolator, config);
    }

    private static void StartReceiver(OpenTrackReceiver receiver, int port)
    {
        receiver.Log = msg => Logger.LogInfo(msg);
        if (receiver.Start(port))
        {
            Logger.LogInfo($"Listening for tracker data on UDP port {port}");
        }
    }

    public override bool Unload()
    {
        Logger.LogInfo($"Unloading {PluginName}...");

        _receiver?.Dispose();

        _behaviour = null;
        if (_behaviourObject != null)
        {
            Object.Destroy(_behaviourObject);
            _behaviourObject = null;
        }

        Logger.LogInfo($"{PluginName} unloaded.");

        _logFile?.Dispose();
        _logFile = null;
        return true;
    }
}
