// SPDX-License-Identifier: MIT
// Copyright (c) 2026 itsloopyo

using System;
using BluePrinceHeadTracking.Core;
using UnityEngine;

namespace BluePrinceHeadTracking.Camera;

internal sealed class AimReticle : IDisposable
{
    private const float EdgeMarginPixels = 24f;
    // A lean past a near surface can put the aim behind the eye while its direction remains centered.
    private const float MinDominantAxis = 1e-4f;

    private readonly AimTrace _aimTrace = new();
    private readonly GameCursorPosition _gameCursor = new();
    private bool _loggedUnusableTrace;

    internal Vector2 LastOffset { get; private set; }
    internal float LastAimDistance { get; private set; } = -1f;
    internal int LastAimLayer { get; private set; } = -1;
    internal bool LastApplied => _gameCursor.Active;

    internal void Initialize() => _gameCursor.Initialize();

    internal void Apply()
    {
        _gameCursor.Active = false;
        Vector3 aimOrigin = TrackedView.CleanPosition;
        Vector3 aimDirection = TrackedView.CleanRotation * Vector3.forward;
        if (!TryLocateAimOnScreen(aimOrigin, aimDirection, out Vector2 screenPoint)) return;

        LastOffset = new Vector2(
            screenPoint.x - TrackedView.ActivePixelWidth * 0.5f,
            screenPoint.y - TrackedView.ActivePixelHeight * 0.5f);
        _gameCursor.ScreenPoint = screenPoint;
        _gameCursor.Active = true;
    }

    internal void Restore() => _gameCursor.Active = false;

    private bool TryLocateAimOnScreen(Vector3 aimOrigin, Vector3 aimDirection, out Vector2 screenPoint)
    {
        AimTraceResult result = _aimTrace.Trace(aimOrigin, aimDirection);
        LastAimDistance = result.Outcome == AimTraceResult.Kind.Surface ? result.Distance : -1f;
        LastAimLayer = result.Layer;

        switch (result.Outcome)
        {
            case AimTraceResult.Kind.Surface:
                if (!TrackedView.TryProjectPoint(aimOrigin + aimDirection * result.Distance, out screenPoint))
                {
                    screenPoint = OffScreenEdgePoint(aimDirection);
                }
                break;

            case AimTraceResult.Kind.Infinity:
                // A definite no-hit is a target at infinity, which is the aim
                // DIRECTION projected for this frame - never a stand-in distance.
                if (!TrackedView.TryProjectDirection(aimDirection, out screenPoint))
                {
                    screenPoint = OffScreenEdgePoint(aimDirection);
                }
                break;

            default:
                // Say so once - a marker that has quietly stopped compensating looks
                // exactly like one that never started.
                Restore();
                if (!_loggedUnusableTrace)
                {
                    _loggedUnusableTrace = true;
                    HeadTrackingPlugin.Logger.LogWarning(
                        "Aim layers unavailable - the game's own pointer is left alone until " +
                        "ClickableRaycaster is up");
                }
                screenPoint = default;
                return false;
        }

        screenPoint.x = Mathf.Clamp(screenPoint.x, EdgeMarginPixels, TrackedView.ActivePixelWidth - EdgeMarginPixels);
        screenPoint.y = Mathf.Clamp(screenPoint.y, EdgeMarginPixels, TrackedView.ActivePixelHeight - EdgeMarginPixels);
        return true;
    }

    private static Vector2 OffScreenEdgePoint(Vector3 aimDirection)
    {
        Vector3 viewDirection = Quaternion.Inverse(TrackedView.RenderRotation) * aimDirection;
        float dominant = Mathf.Max(Mathf.Abs(viewDirection.x), Mathf.Abs(viewDirection.y));
        if (dominant < MinDominantAxis)
        {
            dominant = MinDominantAxis;
        }

        return new Vector2(
            TrackedView.ActivePixelWidth * (0.5f + viewDirection.x / dominant),
            TrackedView.ActivePixelHeight * (0.5f + viewDirection.y / dominant));
    }

    internal string Describe()
    {
        Vector2 point = _gameCursor.ScreenPoint;
        return $"cursor=({point.x:F1},{point.y:F1}) repositioned={_gameCursor.Active}";
    }

    internal void Reset()
    {
        Restore();
        _aimTrace.Reset();
        LastAimDistance = -1f;
        LastAimLayer = -1;
        LastOffset = Vector2.zero;
        _gameCursor.ScreenPoint = Vector2.zero;
    }

    public void Dispose() => _gameCursor.Dispose();
}
