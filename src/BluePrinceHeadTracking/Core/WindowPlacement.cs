// SPDX-License-Identifier: MIT
// Copyright (c) 2026 itsloopyo

using System;
using System.Runtime.InteropServices;
using System.Text;
using UnityEngine;

namespace BluePrinceHeadTracking.Core;

/// <summary>
/// Centres the game's window on the work area of the monitor it is already on
/// whenever Blue Prince is running windowed.
///
/// Keyed to a change of (width, height, fullscreen) rather than to the window
/// rect, so the placement is decided once per display mode and a window the
/// player has dragged somewhere deliberate stays where they put it. The game
/// opens in exclusive fullscreen and windowed is reached from its own settings
/// screen, so the decision that matters is the one taken on that switch rather
/// than one taken at startup.
///
/// The window is found by class name. BepInEx's console belongs to the same
/// process, is visible, is unowned and is larger than any size floor, so a search
/// that only filters on the process can return the console and centre that
/// instead while the log line claims the game moved. The render window is the one
/// whose class is <c>UnityWndClass</c>, which is what .lab/tools/gamewindow.ps1
/// matches against this build.
///
/// The work area, not the monitor bounds: centring against the full monitor puts
/// the title bar behind a top-docked taskbar, and the window cannot then be
/// dragged back out.
/// </summary>
internal sealed class WindowPlacement
{
    private const string UnityWindowClass = "UnityWndClass";

    private const float PollIntervalSeconds = 0.25f;

    // The window is up well before Unity has finished sizing and placing it, and
    // when it stops moving has not been measured here, so the wait is on a rect
    // that has held still rather than on a fixed delay that would be a guess.
    private const int SettlePolls = 2;
    private const int MaxPolls = 40;

    // A window that is centred to the eye but off by a pixel of integer rounding
    // is left alone rather than moved and then reported as a fix.
    private const int CentredTolerancePixels = 2;

    private const uint GwOwner = 4;
    private const uint MonitorDefaultToNearest = 0x0002;
    private const uint SwpNoSize = 0x0001;
    private const uint SwpNoZOrder = 0x0004;
    private const uint SwpNoActivate = 0x0010;

    private float _nextPollTime;
    private int _modeWidth = -1;
    private int _modeHeight = -1;
    private bool _modeFullScreen;
    private bool _modeHandled;
    private int _polls;
    private int _stablePolls;
    private bool _hasLastRect;
    private bool _sawWindow;
    private NativeRect _lastRect;

    /// <summary>
    /// Polls for a settled window and places it. Call every frame; it makes one
    /// decision per display mode and then does nothing until the mode changes.
    /// </summary>
    internal void Update()
    {
        float now = Time.unscaledTime;
        if (now < _nextPollTime) return;
        _nextPollTime = now + PollIntervalSeconds;

        int width = Screen.width;
        int height = Screen.height;
        bool fullScreen = Screen.fullScreen;

        // Screen reports 1x1 for a moment while the swapchain is being rebuilt,
        // both at startup and across a fullscreen toggle. Treating that as a mode
        // would put "running fullscreen at 1x1" in the log and invite a bug report
        // about a display mode the game was never in.
        if (width < 2 || height < 2) return;

        if (width != _modeWidth || height != _modeHeight || fullScreen != _modeFullScreen)
        {
            BeginMode(width, height, fullScreen);
        }

        if (_modeHandled) return;
        DecidePlacement(width, height, fullScreen);
    }

    /// <summary>Starts the one decision this display mode gets.</summary>
    private void BeginMode(int width, int height, bool fullScreen)
    {
        _modeWidth = width;
        _modeHeight = height;
        _modeFullScreen = fullScreen;
        _modeHandled = false;
        _polls = 0;
        _stablePolls = 0;
        _hasLastRect = false;
        _sawWindow = false;
    }

    /// <summary>
    /// Waits for the window's rect to hold still, then places it once. Sets
    /// <c>_modeHandled</c> on every path that reaches a decision, including the
    /// ones that decide to leave the window alone.
    /// </summary>
    private void DecidePlacement(int width, int height, bool fullScreen)
    {
        if (fullScreen)
        {
            _modeHandled = true;
            HeadTrackingPlugin.Logger.LogInfo(
                $"Window: running fullscreen at {width}x{height}, leaving its placement alone");
            return;
        }

        if (++_polls > MaxPolls)
        {
            _modeHandled = true;
            // The two ways this times out need different fixes, so they say
            // different things. Never matching a window points at the class or
            // process filter; a window that never settles points at the timing.
            HeadTrackingPlugin.Logger.LogWarning(_sawWindow
                ? $"Window: rect never held still within {MaxPolls * PollIntervalSeconds:F0}s, " +
                  "leaving its placement alone"
                : $"Window: no game window matched within {MaxPolls * PollIntervalSeconds:F0}s, " +
                  "leaving its placement alone");
            return;
        }

        IntPtr window = FindRenderWindow();
        if (window == IntPtr.Zero || !GetWindowRect(window, out NativeRect rect))
        {
            _hasLastRect = false;
            _stablePolls = 0;
            return;
        }

        _sawWindow = true;

        if (_hasLastRect && SameRect(rect, _lastRect))
        {
            if (++_stablePolls < SettlePolls) return;
            _modeHandled = true;
            CentreUnlessAlready(window, rect);
            return;
        }

        _lastRect = rect;
        _hasLastRect = true;
        _stablePolls = 0;
    }

    private static void CentreUnlessAlready(IntPtr window, NativeRect rect)
    {
        var info = new MonitorInfo { Size = Marshal.SizeOf(typeof(MonitorInfo)) };
        if (!GetMonitorInfo(MonitorFromWindow(window, MonitorDefaultToNearest), ref info))
        {
            HeadTrackingPlugin.Logger.LogWarning(
                $"Window: GetMonitorInfo failed ({Marshal.GetLastWin32Error()}), " +
                "leaving its placement alone");
            return;
        }

        int windowWidth = rect.Right - rect.Left;
        int windowHeight = rect.Bottom - rect.Top;
        int workWidth = info.Work.Right - info.Work.Left;
        int workHeight = info.Work.Bottom - info.Work.Top;

        // A window at least as large as the work area cannot be centred within it,
        // and moving it anyway would push its title bar out of reach.
        if (windowWidth >= workWidth || windowHeight >= workHeight)
        {
            HeadTrackingPlugin.Logger.LogInfo(
                $"Window: {windowWidth}x{windowHeight} fills the work area " +
                $"{workWidth}x{workHeight}, leaving it in place");
            return;
        }

        // Either reading counts as centred. Unity centres on the monitor where this
        // centres on the work area, and the two differ by half the taskbar; moving a
        // window that is already centred trades a visible jump for nothing.
        if (IsCentredOn(rect, info.Work) || IsCentredOn(rect, info.Monitor))
        {
            HeadTrackingPlugin.Logger.LogInfo(
                $"Window: {windowWidth}x{windowHeight} at ({rect.Left}, {rect.Top}) " +
                "is already centred, leaving it alone");
            return;
        }

        int x = CentredOrigin(info.Work.Left, workWidth, windowWidth);
        int y = CentredOrigin(info.Work.Top, workHeight, windowHeight);

        if (!SetWindowPos(window, IntPtr.Zero, x, y, 0, 0, SwpNoSize | SwpNoZOrder | SwpNoActivate))
        {
            HeadTrackingPlugin.Logger.LogWarning(
                $"Window: SetWindowPos failed ({Marshal.GetLastWin32Error()}), " +
                $"leaving it at ({rect.Left}, {rect.Top})");
            return;
        }

        HeadTrackingPlugin.Logger.LogInfo(
            $"Window: centred the {windowWidth}x{windowHeight} window at ({x}, {y}), " +
            $"was at ({rect.Left}, {rect.Top}) on work area {workWidth}x{workHeight}");
    }

    /// <summary>
    /// The render window, or <see cref="IntPtr.Zero"/> while the engine has not
    /// brought a visible one up yet.
    /// </summary>
    private static IntPtr FindRenderWindow()
    {
        s_processId = GetCurrentProcessId();
        s_found = IntPtr.Zero;
        EnumWindows(PickRenderWindowDelegate, IntPtr.Zero);
        return s_found;
    }

    private static bool PickRenderWindow(IntPtr window, IntPtr param)
    {
        GetWindowThreadProcessId(window, out uint owner);
        if (owner != s_processId) return true;
        if (!IsWindowVisible(window)) return true;
        if (GetWindow(window, GwOwner) != IntPtr.Zero) return true;

        s_className.Length = 0;
        GetClassName(window, s_className, s_className.Capacity);
        if (s_className.ToString() != UnityWindowClass) return true;

        s_found = window;
        return false;
    }

    // EnumWindows runs synchronously, so the enumeration state is a set of statics
    // rather than a marshalled lParam. The delegate is held in a static of its own
    // because a delegate created at the call site is collectable while native code
    // still holds the thunk.
    private static readonly EnumWindowsProc PickRenderWindowDelegate = PickRenderWindow;
    private static readonly StringBuilder s_className = new(64);
    private static uint s_processId;
    private static IntPtr s_found;

    private static int CentredOrigin(int areaStart, int areaExtent, int windowExtent)
    {
        return areaStart + (areaExtent - windowExtent) / 2;
    }

    private static bool IsCentredOn(NativeRect window, NativeRect area)
    {
        int dx = window.Left - CentredOrigin(
            area.Left, area.Right - area.Left, window.Right - window.Left);
        int dy = window.Top - CentredOrigin(
            area.Top, area.Bottom - area.Top, window.Bottom - window.Top);
        return Math.Abs(dx) <= CentredTolerancePixels && Math.Abs(dy) <= CentredTolerancePixels;
    }

    private static bool SameRect(NativeRect a, NativeRect b)
    {
        return a.Left == b.Left && a.Top == b.Top && a.Right == b.Right && a.Bottom == b.Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorInfo
    {
        public int Size;
        public NativeRect Monitor;
        public NativeRect Work;
        public uint Flags;
    }

    private delegate bool EnumWindowsProc(IntPtr window, IntPtr param);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc callback, IntPtr param);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "GetClassNameW")]
    private static extern int GetClassName(IntPtr window, StringBuilder buffer, int bufferSize);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr window);

    [DllImport("user32.dll")]
    private static extern IntPtr GetWindow(IntPtr window, uint command);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr window, out NativeRect rect);

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr window, uint flags);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "GetMonitorInfoW",
        SetLastError = true)]
    private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowPos(IntPtr window, IntPtr insertAfter,
        int x, int y, int width, int height, uint flags);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentProcessId();
}
