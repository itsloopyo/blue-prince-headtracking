// SPDX-License-Identifier: MIT
// Copyright (c) 2026 itsloopyo

using System;
using System.Reflection;
using BluePrinceHeadTracking.Core;
using BluePrinceHeadTracking.Utilities;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace BluePrinceHeadTracking.State;

/// <summary>
/// Decides whether the player is in active gameplay, which is the only state the
/// mod moves the camera in.
///
/// Four signals, each covering a case the others miss:
/// <list type="bullet">
/// <item><c>ClickableRaycaster.Active</c> exists only inside a loaded run, so it
/// covers the main menu, the boot scene and every loading screen without a list of
/// scene names to keep up to date.</item>
/// <item><c>ClickableRaycaster.IsRaycasting()</c> is the game's own answer to
/// "is the player currently pointing at the world". It goes false for the opening
/// letter, for the day-start narration and for every scripted camera move, which
/// is the whole class of cutscene a scene name and a timescale both miss.</item>
/// <item><c>BluePrinceUIManager.OpenScreenCount</c> is non-zero whenever a screen
/// is up - the pause menu, the map, a note being read, the day summary.</item>
/// <item><c>Time.timeScale</c> at zero covers anything that freezes the game
/// without going through a UI screen.</item>
/// </list>
///
/// Polled on an interval rather than every frame: each reflected read is an IL2CPP
/// native call and no state here changes faster than a player can perceive.
/// </summary>
internal sealed class GameplayStateDetector : IDisposable
{
    private const int PollIntervalFrames = 6;

    // The state line is rate limited only so a value that flickers between two
    // readings cannot fill the log; it is written on change, not on a timer.
    private const float DiagnosticIntervalSeconds = 0.5f;

    private MethodInfo? _openScreenCount;
    private bool _loggedMissingUiManager;
    private bool _loggedMissingRaycaster;
    private MethodInfo? _isRaycasting;
    private MethodInfo? _drawCursor;
    private bool _resolvedUiManager;
    private bool _resolvedRaycaster;
    private bool _resolvedCursorRenderer;

    private int _framesUntilPoll;
    private float _nextDiagnosticTime;
    private string _lastDescribed = string.Empty;
    private bool _inGameplay;
    private bool _applicationFocused = true;

    // Last polled value of each signal, for the diagnostic line.
    private bool _hasRaycaster;
    private bool _isPointing;
    private int _openScreens = -1;
    private float _timeScale;

    /// <summary>Fired when gameplay is entered or left.</summary>
    internal event Action<bool>? OnGameplayStateChanged;

    internal bool IsInGameplay => _inGameplay;

    internal bool IsApplicationFocused => _applicationFocused;

    /// <summary>Whether diagnostic logging writes the periodic state line.</summary>
    internal bool DiagnosticLogging { get; set; }

    internal void Poll()
    {
        if (--_framesUntilPoll <= 0)
        {
            _framesUntilPoll = PollIntervalFrames;
            Evaluate();
        }

        if (!DiagnosticLogging || Time.unscaledTime < _nextDiagnosticTime) return;
        _nextDiagnosticTime = Time.unscaledTime + DiagnosticIntervalSeconds;

        // Written only when something in it moved. A line every two seconds
        // whatever the game was doing buried the transitions it exists to show,
        // and pushed the startup lines out of a tail.
        string described = Describe();
        if (described == _lastDescribed) return;
        _lastDescribed = described;

        HeadTrackingPlugin.Logger.LogInfo($"STATE {described}");
    }

    private void Evaluate()
    {
        _applicationFocused = Application.isFocused;
        _timeScale = Time.timeScale;

        if (GameTypes.ClickableRaycaster == null && !_loggedMissingRaycaster)
        {
            _loggedMissingRaycaster = true;
            HeadTrackingPlugin.Logger.LogError(
                "BluePrince.Inputs.ClickableRaycaster was not found - head tracking stays suppressed, " +
                "because this is the signal that says a run is loaded and the player is pointing at the world");
        }

        object? raycaster = GameMembers.ReadStatic(GameTypes.ClickableRaycaster, "Active");
        _hasRaycaster = raycaster != null;
        _isPointing = _hasRaycaster && ReadIsRaycasting(raycaster!);
        _openScreens = ReadOpenScreenCount();

        bool inGameplay = _isPointing && _timeScale > 0f && _openScreens == 0;
        if (inGameplay == _inGameplay) return;

        _inGameplay = inGameplay;
        OnGameplayStateChanged?.Invoke(inGameplay);
    }

    private bool ReadIsRaycasting(object raycaster)
    {
        if (!_resolvedRaycaster)
        {
            _resolvedRaycaster = true;
            _isRaycasting = GameTypes.ClickableRaycaster!.GetMethod(
                "IsRaycasting", BindingFlags.Public | BindingFlags.Instance, null, Type.EmptyTypes, null);
            if (_isRaycasting == null)
            {
                HeadTrackingPlugin.Logger.LogError(
                    "ClickableRaycaster.IsRaycasting not found - cutscenes will not pause head tracking");
            }
        }

        // Without the game's own answer there is no cutscene signal at all, so the
        // remaining checks are all that gate tracking. Said once, above.
        if (_isRaycasting == null) return true;
        return (bool)_isRaycasting.Invoke(raycaster, null)!;
    }

    /// <summary>
    /// Whether the game is drawing its pointer. Diagnostic only: it tracks the same
    /// states <see cref="ReadIsRaycasting"/> does but lags them by the cursor's own
    /// fade, so it is reported rather than gated on, and read only when the
    /// diagnostic line is about to be written.
    /// </summary>
    private string DescribeDrawCursor()
    {
        Type? rendererType = GameTypes.CursorRenderer;
        if (rendererType == null) return "(type missing)";

        if (!_resolvedCursorRenderer)
        {
            _resolvedCursorRenderer = true;
            _drawCursor = rendererType.GetProperty("DrawCursor",
                BindingFlags.Public | BindingFlags.Static)?.GetGetMethod();
        }

        // A flag that cannot be read has to read differently from one that is off.
        // Both used to print False, which is the answer that says the mod suppressed
        // the pointer successfully.
        if (_drawCursor == null) return "(unreadable)";
        return $"{(bool)_drawCursor.Invoke(null, null)!}";
    }

    /// <summary>
    /// Number of UI screens the game currently has open, or -1 when the manager has
    /// not spawned. -1 suppresses tracking: a screen count that cannot be read is
    /// not evidence that no screen is open, and the raycaster check above already
    /// covers the states where the manager is legitimately absent.
    /// </summary>
    private int ReadOpenScreenCount()
    {
        Type? uiType = GameTypes.UIManager;
        if (uiType == null)
        {
            if (!_loggedMissingUiManager)
            {
                _loggedMissingUiManager = true;
                HeadTrackingPlugin.Logger.LogError(
                    "BluePrince.UI.BluePrinceUIManager was not found - head tracking stays suppressed, " +
                    "because a screen count that cannot be read is not evidence that no screen is open");
            }
            return -1;
        }

        object? manager = GameMembers.ReadStatic(uiType, "Active");
        if (manager == null) return -1;

        if (!_resolvedUiManager)
        {
            _resolvedUiManager = true;
            _openScreenCount = GameMembers.FindGetter(uiType, "OpenScreenCount");
            if (_openScreenCount == null)
            {
                HeadTrackingPlugin.Logger.LogError(
                    "BluePrinceUIManager.OpenScreenCount was not found - head tracking stays suppressed, " +
                    "because a screen count that cannot be read is not evidence that no screen is open");
            }
        }

        if (_openScreenCount == null) return -1;
        return (int)_openScreenCount.Invoke(manager, null)!;
    }

    /// <summary>
    /// The diagnostic line. The scene name and the pointer flag are read here
    /// rather than held from the last poll: neither gates anything, and reading
    /// them on the poll cost a string allocation and a reflected call six times a
    /// second in every session, diagnostics on or off.
    /// </summary>
    internal string Describe()
    {
        return $"gameplay={_inGameplay} scene='{SceneManager.GetActiveScene().name}' " +
               $"raycaster={_hasRaycaster} pointing={_isPointing} " +
               $"drawCursor={DescribeDrawCursor()} screens={_openScreens} timeScale={_timeScale:F2} " +
               $"focused={_applicationFocused} cursorLock={Cursor.lockState}";
    }

    public void Dispose()
    {
        OnGameplayStateChanged = null;
    }
}
