// SPDX-License-Identifier: MIT
// Copyright (c) 2026 itsloopyo

using UnityEngine;

namespace BluePrinceHeadTracking.Utilities;

/// <summary>
/// Builds hierarchy paths from Unity transforms, for log lines that have to name
/// a specific object in a rig.
/// </summary>
internal static class TransformPath
{
    internal static string GetFullPath(Transform? transform)
    {
        if (transform == null) return "(null)";

        string path = transform.name;
        Transform? parent = transform.parent;
        while (parent != null)
        {
            path = parent.name + "/" + path;
            parent = parent.parent;
        }
        return path;
    }
}
