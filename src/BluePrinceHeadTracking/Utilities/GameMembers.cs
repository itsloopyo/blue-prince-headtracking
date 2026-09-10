// SPDX-License-Identifier: MIT
// Copyright (c) 2026 itsloopyo

using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Il2CppInterop.Runtime;
using UnityEngine;

namespace BluePrinceHeadTracking.Utilities;

/// <summary>
/// Reflection over Blue Prince's own types, which the mod never references at
/// compile time so the build stays game-independent.
///
/// Il2CppInterop turns some of the game's fields into properties wrapping the
/// native field offset and leaves others as fields, so the two lookups that resolve
/// a member by name alone - <see cref="ReadStatic"/> and <see cref="ReadMember"/> -
/// try the property first and fall back to the field.
/// <see cref="FindGetter"/> resolves a property getter only: it exists to hand a
/// per-frame path something it can cache, and every member read through it was
/// checked against the shipped build as a property.
/// </summary>
internal static class GameMembers
{
    private const BindingFlags InstanceMembers = BindingFlags.Public | BindingFlags.Instance;
    private const BindingFlags StaticMembers = BindingFlags.Public | BindingFlags.Static;

    private static MethodInfo? _getComponents;
    private static MethodInfo? _getIl2CppType;
    private static MethodInfo? _allCameras;
    private static MethodInfo? _il2CppTypeFullName;

    // Resolved accessors, including the ones that resolved to nothing. The camera
    // path reads a singleton every frame, and a name lookup scans the type's whole
    // member table each time. Written and read only from Unity's update callbacks,
    // so it needs no lock.
    private static readonly Dictionary<(Type Type, string Name), MemberInfo?> StaticAccessors = new();

    /// <summary>Reads a game type's static <c>Active</c> / <c>Instance</c> singleton.</summary>
    internal static object? ReadStatic(Type? type, string name)
    {
        if (type == null) return null;

        if (!StaticAccessors.TryGetValue((type, name), out MemberInfo? accessor))
        {
            accessor = (MemberInfo?)type.GetProperty(name, StaticMembers)?.GetGetMethod()
                       ?? type.GetField(name, StaticMembers);
            StaticAccessors[(type, name)] = accessor;
        }

        object? value = accessor switch
        {
            MethodInfo getter => getter.Invoke(null, null),
            FieldInfo field => field.GetValue(null),
            _ => null
        };

        // A singleton the game holds past its component's destruction comes back as
        // a wrapper that is not reference-null but is dead to Unity, and a plain
        // != null lets that stale wrapper through to the state gate. Unity's own ==
        // is the check that catches it, so it is applied once here rather than at
        // each caller.
        if (value is UnityEngine.Object unityObject && unityObject == null) return null;
        return value;
    }

    internal static MethodInfo? FindGetter(Type type, string name)
    {
        return type.GetProperty(name, InstanceMembers)?.GetGetMethod();
    }

    /// <summary>
    /// Reads a public instance member by name. Resolves on every call, so this is
    /// for one-shot dumps and startup searches; a per-frame path caches the
    /// accessor from <see cref="FindGetter"/> instead.
    /// </summary>
    internal static object? ReadMember(Type type, object instance, string name)
    {
        MethodInfo? getter = FindGetter(type, name);
        if (getter != null) return getter.Invoke(instance, null);
        return type.GetField(name, InstanceMembers)?.GetValue(instance);
    }

    /// <summary>
    /// Every component on a GameObject, for the rig dump. Enumerated as plain
    /// objects because the returned array's element type is only known at runtime;
    /// each element's <c>GetType().FullName</c> is the interop proxy's name, which
    /// is what the dump is for.
    ///
    /// The non-generic <c>GetComponents</c> takes a <c>System.Type</c> in the NuGet
    /// reference assemblies but an <c>Il2CppSystem.Type</c> in the interop ones the
    /// plugin binds to at runtime, so calling it directly compiles and then throws
    /// MissingMethodException. Invoking the interop signature reflectively is the
    /// route that works.
    /// </summary>
    internal static IEnumerable ComponentsOn(GameObject gameObject)
    {
        _getComponents ??= ResolveInteropMethod(gameObject, "GetComponents");
        return (IEnumerable)_getComponents.Invoke(
            gameObject, new object[] { Il2CppType.Of<Component>() })!;
    }

    /// <summary>
    /// The IL2CPP class name of a live object. Il2CppInterop hands back wrappers
    /// typed as the DECLARED element type when it returns an array, so a component
    /// list reads as a column of "UnityEngine.Component" and says nothing; the
    /// native type is the part worth logging.
    ///
    /// The name is read off the returned type's FullName, not its ToString():
    /// ToString() on the managed wrapper answers "Il2CppSystem.Type" for every
    /// component in the game, which is a column of noise that looks like a result.
    /// </summary>
    internal static string Il2CppTypeName(object instance)
    {
        _getIl2CppType ??= instance.GetType().GetMethod("GetIl2CppType", Type.EmptyTypes)
                           ?? throw new MissingMethodException("GetIl2CppType not found on an interop wrapper");

        object? type = _getIl2CppType.Invoke(instance, null);
        if (type == null) return "(unknown)";

        _il2CppTypeFullName ??= type.GetType().GetProperty("FullName", InstanceMembers)?.GetGetMethod()
                                ?? throw new MissingMethodException("Il2CppSystem.Type.FullName not found");

        return _il2CppTypeFullName.Invoke(type, null) as string ?? "(unknown)";
    }

    /// <summary>
    /// Every camera Unity currently considers active, in no particular order.
    ///
    /// <c>Camera.allCameras</c> is declared as <c>Camera[]</c> in the NuGet
    /// reference assemblies and as <c>Il2CppReferenceArray&lt;Camera&gt;</c> in the
    /// interop ones, so reading it directly compiles and then throws. Its elements
    /// come back typed as Camera - the array's declared element type - which is
    /// what the caller needs, so no re-wrapping is involved.
    /// </summary>
    internal static IEnumerable AllCameras()
    {
        if (_allCameras == null)
        {
            Type cameraType = AccessTools.TypeByName("UnityEngine.Camera")
                ?? throw new MissingMethodException("UnityEngine.Camera not found in the interop assemblies");
            _allCameras = cameraType.GetProperty("allCameras", StaticMembers)?.GetGetMethod()
                ?? throw new MissingMethodException("UnityEngine.Camera.allCameras not found in the interop assemblies");
        }

        return (IEnumerable)_allCameras.Invoke(null, null)!;
    }

    private static MethodInfo ResolveInteropMethod(GameObject gameObject, string name)
    {
        foreach (MethodInfo method in gameObject.GetType().GetMethods(InstanceMembers))
        {
            if (method.Name != name || method.IsGenericMethod) continue;
            ParameterInfo[] parameters = method.GetParameters();
            if (parameters.Length == 1 && parameters[0].ParameterType == typeof(Il2CppSystem.Type))
            {
                return method;
            }
        }

        throw new MissingMethodException(
            $"UnityEngine.GameObject.{name}(Il2CppSystem.Type) not found in the interop assemblies");
    }
}
