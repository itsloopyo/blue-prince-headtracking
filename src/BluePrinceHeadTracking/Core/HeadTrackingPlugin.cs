// SPDX-License-Identifier: MIT
// Copyright (c) 2026 itsloopyo

using System;
using System.Collections.Generic;
using System.IO;
using BepInEx;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using BluePrinceHeadTracking.Camera;
using BluePrinceHeadTracking.Configuration;
using CameraUnlock.Core.Config;
using CameraUnlock.Core.Data;
using CameraUnlock.Core.Processing;
using CameraUnlock.Core.Protocol;
using Il2CppInterop.Runtime.Injection;
using UnityEngine;
using Object = UnityEngine.Object;

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
    private ConfigOwner<BluePrinceConfig>? _configOwner;

    public override void Load()
    {
        Logger = Log;

        // Attached before the first line is logged, so the session's log file holds
        // the whole of Load() rather than starting part way through it.
        _logFile = LogFile.Attach(PluginName, $"{PluginName} v{PluginVersion}");

        BluePrinceConfig config = LoadConfig();

        _receiver = new OpenTrackReceiver();
        TrackingProcessor processor = BuildRotationProcessor(config);
        PositionProcessor positionProcessor = BuildPositionProcessor(config);
        var positionInterpolator = new PositionInterpolator();

        CreateBehaviour(_receiver, processor, positionProcessor, positionInterpolator, config, SaveConfig);

        StartReceiver(_receiver, config.UdpPort);

        Logger.LogInfo($"{PluginName} v{PluginVersion} loaded - tracking is " +
                       $"{(config.EnableOnStartup ? "ENABLED" : "DISABLED")} on startup");
    }

    /// <summary>
    /// The settings live in BepInEx\config\CameraUnlock.ini, read and written by core's config
    /// owner, with rows set to default following the player's Defaults.ini. Nothing is bound on
    /// the plugin's Config, so ConfigurationManager does not list them. While CameraUnlock.ini is
    /// absent the owner imports the plugin's .cfg, the file every earlier build read, through the
    /// frozen reader on a ConfigFile of its own, and never writes that file.
    ///
    /// The mod has nothing on screen to show a message with, so the owner's messages for the
    /// player go to the log beside its other lines.
    /// </summary>
    private BluePrinceConfig LoadConfig()
    {
        ConfigOwnerOptions<BluePrinceConfig> options =
            BluePrinceConfig.Options(ConfigPath, Config.ConfigFilePath, DefaultsFile.PerUser());
        options.StatusSink = message => Logger.LogWarning(message);
        _configOwner = new ConfigOwner<BluePrinceConfig>(options);

        ConfigLoadResult<BluePrinceConfig> loaded = _configOwner.Load();

        // The owner writes each diagnostic as "<path>: <description>" among lines that only
        // report what it did, so the complaints are picked out by their text.
        var complaints = new HashSet<string>();
        foreach (CanonicalDiagnostic diagnostic in loaded.Diagnostics)
        {
            complaints.Add(ConfigPath + ": " + diagnostic.Describe());
        }
        bool usable = loaded.Status == ConfigLoadStatus.Canonical
                      || loaded.Status == ConfigLoadStatus.Migrated
                      || loaded.Status == ConfigLoadStatus.Created;
        foreach (string line in loaded.Log)
        {
            if (usable && !complaints.Contains(line)) Logger.LogInfo(line);
            else Logger.LogWarning(line);
        }
        Logger.LogInfo($"Config {ConfigPath}: {loaded.Status}");
        return loaded.Config;
    }

    private static string ConfigPath => Path.Combine(Paths.ConfigPath, "CameraUnlock.ini");

    /// <summary>
    /// Called after the new value is already applied. A save that fails is logged and the
    /// session keeps the new value.
    /// </summary>
    private void SaveConfig(Action<BluePrinceConfig> change)
    {
        ConfigSaveResult saved = _configOwner!.Save(change);
        if (saved.Status == ConfigSaveStatus.Saved)
        {
            // A row that held default and now holds a value, so it stops following
            // Defaults.ini in this game.
            foreach (string line in saved.Log) Logger.LogInfo(line);
            return;
        }
        foreach (string line in saved.Log) Logger.LogWarning(line);
        Logger.LogWarning($"{ConfigPath}: {saved.Status}: {saved.Reason} The change applies to this session only.");
    }

    /// <summary>
    /// The rotation pipeline runs at 1:1: no sensitivity, no deadzone, no inversion.
    /// The tracker owns pose shaping, and the axis signs this engine needs are
    /// applied once at the camera boundary rather than folded in here, where they
    /// would land ahead of the limits.
    /// </summary>
    private static TrackingProcessor BuildRotationProcessor(BluePrinceConfig config)
    {
        return new TrackingProcessor
        {
            LocalSmoothing = config.LocalSmoothing,
            RemoteSmoothing = config.RemoteSmoothing,
            Sensitivity = SensitivitySettings.Default,
            Deadzone = DeadzoneSettings.None
        };
    }

    private static PositionProcessor BuildPositionProcessor(BluePrinceConfig config)
    {
        return new PositionProcessor
        {
            Settings = new PositionSettings(
                1f, 1f, 1f,
                config.Position.LimitX,
                config.Position.LimitY,
                config.Position.LimitYDown,
                config.Position.LimitZ,
                config.Position.LimitZBack,
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
        PositionProcessor positionProcessor, PositionInterpolator positionInterpolator, BluePrinceConfig config,
        Action<Action<BluePrinceConfig>> saveConfig)
    {
        ClassInjector.RegisterTypeInIl2Cpp<RenderViewInjector>();
        ClassInjector.RegisterTypeInIl2Cpp<HeadTrackingBehaviour>();

        _behaviourObject = new GameObject("BluePrinceHeadTracking");
        _behaviourObject.hideFlags = HideFlags.DontSave;
        Object.DontDestroyOnLoad(_behaviourObject);

        _behaviour = _behaviourObject.AddComponent<HeadTrackingBehaviour>();
        _behaviour.Initialize(receiver, processor, positionProcessor, positionInterpolator, config, saveConfig);
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
