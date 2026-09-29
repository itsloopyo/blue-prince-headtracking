// SPDX-License-Identifier: MIT
// Copyright (c) 2026 itsloopyo

using System;
using System.Runtime.InteropServices;
using BepInEx.Unity.IL2CPP.Hook;
using BluePrinceHeadTracking.Core;
using Il2CppInterop.Runtime;
using UnityEngine;

namespace BluePrinceHeadTracking.Camera;

internal sealed class GameCursorPosition : IDisposable
{
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void ProcessVertex(IntPtr instance, int index, IntPtr methodInfo);

    private INativeDetour? _detour;
    private ProcessVertex _original = null!;
    private int _mousePositionOffset;
    private float _nextLogTime;

    internal bool Active { get; set; }
    internal Vector2 ScreenPoint { get; set; }

    internal void Initialize()
    {
        IntPtr renderer = IL2CPP.GetIl2CppClass("Assembly-CSharp.dll", "", "CustomCursorRenderer");
        if (renderer == IntPtr.Zero) throw new TypeLoadException("CustomCursorRenderer not found");

        IntPtr field = IL2CPP.GetIl2CppField(renderer, "_mousePos");
        if (field == IntPtr.Zero) throw new MissingFieldException("CustomCursorRenderer", "_mousePos");
        _mousePositionOffset = checked((int)IL2CPP.il2cpp_field_get_offset(field));

        IntPtr method = IL2CPP.il2cpp_class_get_method_from_name(renderer, "ProcessVertex", 1);
        if (method == IntPtr.Zero) throw new MissingMethodException("CustomCursorRenderer", "ProcessVertex");
        _detour = INativeDetour.CreateAndApply<ProcessVertex>(Marshal.ReadIntPtr(method), MoveVertex, out _original);
        HeadTrackingPlugin.Logger.LogInfo("Game cursor position hook ready");
    }

    private void MoveVertex(IntPtr instance, int index, IntPtr methodInfo)
    {
        if (!Active)
        {
            _original(instance, index, methodInfo);
            return;
        }

        // Only x and y are replaced, as raw 32-bit words: Marshal's structure copies
        // box a Vector3 each way, and this runs for every cursor vertex of every frame.
        IntPtr x = IntPtr.Add(instance, _mousePositionOffset);
        IntPtr y = IntPtr.Add(x, sizeof(float));
        int savedX = Marshal.ReadInt32(x);
        int savedY = Marshal.ReadInt32(y);
        Vector2 draw = ScreenPoint;
        // The game reads this anchor while submitting each cursor vertex. Restore
        // it before returning so input and subsequent cursor draws see its own value.
        Marshal.WriteInt32(x, BitConverter.SingleToInt32Bits(draw.x));
        Marshal.WriteInt32(y, BitConverter.SingleToInt32Bits(draw.y));
        try
        {
            _original(instance, index, methodInfo);
        }
        finally
        {
            Marshal.WriteInt32(x, savedX);
            Marshal.WriteInt32(y, savedY);
        }

        if (Diagnostics.RigProbe.Enabled && Time.unscaledTime >= _nextLogTime)
        {
            _nextLogTime = Time.unscaledTime + 2f;
            HeadTrackingPlugin.Logger.LogInfo(
                $"CURSOR original=({BitConverter.Int32BitsToSingle(savedX):F1},{BitConverter.Int32BitsToSingle(savedY):F1}) " +
                $"draw=({draw.x:F1},{draw.y:F1}) restored=True");
        }
    }

    public void Dispose()
    {
        Active = false;
        _detour?.Dispose();
        _detour = null;
    }
}
