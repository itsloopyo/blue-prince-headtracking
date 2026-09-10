// SPDX-License-Identifier: MIT
// Copyright (c) 2026 itsloopyo

using UnityEngine;

namespace BluePrinceHeadTracking.Camera;

/// <summary>
/// Puts the head-tracked view on the camera for the render, and takes it straight
/// back off. This is what actually decouples aim in this title.
///
/// Unity calls <c>OnPreCull</c> on a camera's own components after every script's
/// Update and LateUpdate have run, and <c>OnPostRender</c> once that camera has
/// drawn. Between those two calls the camera carries the tracked view, so the
/// frame is culled and drawn with it; outside them the camera is exactly what the
/// game's controller set, so every query the game makes - the interaction raycast
/// above all - is answered by the pose the mouse is aiming with.
///
/// The mod used to write the matrix in a LateUpdate at execution order 10000 and
/// clear it in another at -10000, on the assumption that those orders put the game
/// in between. They do not. <c>[DefaultExecutionOrder]</c> is baked by Unity at
/// build time, and these classes are injected into IL2CPP at runtime, so Unity has
/// no order recorded for them and they run at the default alongside the game's own
/// scripts. <c>ClickableRaycaster.LateUpdate</c> landed after the write, so the
/// game cast its interaction ray through the head-tracked matrix and the player's
/// aim followed their head: with the mouse held still, a target picked at rest was
/// lost by twenty degrees of yaw.
///
/// Rendering is the one phase whose position in the frame is fixed no matter what
/// order scripts run in, which is why the write lives here now and nowhere else.
/// </summary>
public class RenderViewInjector : MonoBehaviour
{
    internal static CameraViewWriter? Writer;

    private void OnPreCull()
    {
        Writer?.ApplyForRender();
    }

    private void OnPostRender()
    {
        Writer?.ClearAfterRender();
    }
}
