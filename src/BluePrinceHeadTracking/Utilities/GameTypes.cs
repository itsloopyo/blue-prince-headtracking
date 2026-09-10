// SPDX-License-Identifier: MIT
// Copyright (c) 2026 itsloopyo

using System;
using HarmonyLib;

namespace BluePrinceHeadTracking.Utilities;

/// <summary>
/// Lazily resolved, cached handles on the Blue Prince types the mod reflects
/// into. Resolving through <c>AccessTools.TypeByName</c> rather than
/// <c>Type.GetType</c> keeps the lookup working whichever interop assembly
/// BepInEx generated the type into.
///
/// A missing type is cached as missing, so a type the game genuinely does not
/// have costs one search rather than one per frame.
/// </summary>
internal static class GameTypes
{
    private static readonly Lazy<Type?> LazyClickableRaycaster =
        new(() => AccessTools.TypeByName("BluePrince.Inputs.ClickableRaycaster"));

    private static readonly Lazy<Type?> LazyCursorRenderer =
        new(() => AccessTools.TypeByName("CustomCursorRenderer"));

    private static readonly Lazy<Type?> LazyUIManager =
        new(() => AccessTools.TypeByName("BluePrince.UI.BluePrinceUIManager"));

    /// <summary>Owns the interaction pick, and the layers it is allowed to hit.</summary>
    internal static Type? ClickableRaycaster => LazyClickableRaycaster.Value;

    /// <summary>Draws the on-screen pointer during the render phase.</summary>
    internal static Type? CursorRenderer => LazyCursorRenderer.Value;

    /// <summary>Tracks how many UI screens are open, which is the menu signal.</summary>
    internal static Type? UIManager => LazyUIManager.Value;
}
