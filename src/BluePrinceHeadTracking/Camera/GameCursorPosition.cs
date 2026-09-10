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

        IntPtr position = IntPtr.Add(instance, _mousePositionOffset);
        Vector3 saved = Marshal.PtrToStructure<Vector3>(position);
        // The game reads this anchor while submitting each cursor vertex. Restore
        // it before returning so input and subsequent cursor draws see its own value.
        Marshal.StructureToPtr(new Vector3(ScreenPoint.x, ScreenPoint.y, saved.z), position, false);
        try
        {
            _original(instance, index, methodInfo);
        }
        finally
        {
            Marshal.StructureToPtr(saved, position, false);
        }

        if (Diagnostics.RigProbe.Enabled && Time.unscaledTime >= _nextLogTime)
        {
            _nextLogTime = Time.unscaledTime + 2f;
            HeadTrackingPlugin.Logger.LogInfo(
                $"CURSOR original=({saved.x:F1},{saved.y:F1}) draw=({ScreenPoint.x:F1},{ScreenPoint.y:F1}) restored=True");
        }
    }

    public void Dispose()
    {
        Active = false;
        _detour?.Dispose();
        _detour = null;
    }
}
