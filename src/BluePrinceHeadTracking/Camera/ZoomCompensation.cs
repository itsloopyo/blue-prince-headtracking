// SPDX-License-Identifier: MIT
// Copyright (c) 2026 itsloopyo

using UnityEngine;

namespace BluePrinceHeadTracking.Camera;

/// <summary>
/// Keeps head tracking's effect on the picture the same size whatever the game
/// does with its field of view.
///
/// A narrow field of view magnifies everything in the frame, head tracking
/// included: the head still turns ten degrees and the view still turns ten
/// degrees, the picture just moves further, by the ratio between the two fields
/// of view. Uncorrected, the player reads that as the mod's sensitivity changing
/// under them.
///
/// The correction is one number: scale the pose so its SCREEN displacement is
/// what it would have been at the game's un-zoomed field of view. It is exactly
/// 1.0 when nothing has narrowed the view, so ordinary play is untouched.
///
/// Yaw, pitch and the lean all TRANSLATE the image across the frame, so all
/// three scale. Roll ROTATES it about the view axis, and ten degrees of head
/// roll rolls the picture ten degrees at every field of view there is, so roll
/// is left alone.
///
/// This is a C# transcription of the fleet's shared statement of the policy,
/// <c>cameraunlock/camera/zoom_compensation.h</c>, which has no C# counterpart in
/// the core library yet. Nothing here is user-configurable: it is an
/// engine-boundary conversion in the same family as the axis signs, not a
/// sensitivity knob.
/// </summary>
internal static class ZoomCompensation
{
    // The tangent round trip below is only single valued on (-90, 90): past the
    // pole the tangent changes sign, so 100 degrees comes back as -80 and the view
    // swings hard the other way. The mod keeps no centre of its own, so a tracker
    // that has not been centred in its own app sends absolute angles that reach
    // there routinely. Angles are held just inside the pole instead; the caller's
    // own safety limits then take over, and nothing inside a head's usable range
    // is touched.
    private const float MaxScalableAngle = 89.9f;

    /// <summary>
    /// The factor a translation scales by, from the field of view being rendered
    /// now and the game's un-zoomed one, both as <c>tan(fov / 2)</c> IN THE SAME
    /// AXIS.
    ///
    /// The same axis is the whole of the difficulty. A vertical live value against
    /// a horizontal base is not an error anything catches: the ratio is merely off
    /// by a constant, so the whole of normal play runs at a fixed fraction of the
    /// pose and head tracking feels weak everywhere rather than wrong anywhere.
    /// Both of this mod's terms are vertical half-angle tangents read from the same
    /// projection, and the proof is that the logged factor is 1.0000 in ordinary
    /// play.
    /// </summary>
    internal static float Factor(float tanHalfFov, float tanHalfFovBase)
    {
        return tanHalfFov / tanHalfFovBase;
    }

    /// <summary>
    /// An angle in degrees, rescaled so it displaces the image by as much as the
    /// original angle did at the base field of view.
    ///
    /// The tangent round trip is what makes that exact rather than approximate: an
    /// angle's image displacement goes as <c>tan(angle) / tan(fov / 2)</c>, so
    /// holding that ratio fixed means <c>tan(out) = tan(in) * factor</c>.
    /// </summary>
    internal static float ScaleAngle(float angleDegrees, float factor)
    {
        float inDomain = Mathf.Clamp(angleDegrees, -MaxScalableAngle, MaxScalableAngle);
        return Mathf.Atan(Mathf.Tan(inDomain * Mathf.Deg2Rad) * factor) * Mathf.Rad2Deg;
    }
}
