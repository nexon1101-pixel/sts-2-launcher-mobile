using System;
using System.Collections.Generic;
using Godot;

namespace Sts2Ios;

// Two-finger touch = right click.
//
// On iOS the DisplayServer input-event callback REPLACES the engine's own
// input feed: events delivered to the callback never reach the Input
// singleton on their own. The first version of this file ignored events
// (all input died); the second re-fed them with Input.ParseInputEvent
// from inside the callback (the game wedged on the splash screen —
// re-entering the input system from the OS callback, before the main
// loop is in its normal frame flow, is not safe).
//
// So the callback now does nothing but enqueue. A per-frame pump,
// subscribed to SceneTree.ProcessFrame like FrameWatch, drains the
// queue in frame context: it re-feeds every real event in arrival
// order, then runs the two-finger state machine on the touch events
// and injects a synthetic right press / motion / release through
// Input.ParseInputEvent.
//
// The device crash log for the re-feed version (StS2-2026-11-21-155924)
// shows why the naive re-feed wedged: Input::flush_buffered_events ->
// _parse_input_event_impl -> DisplayServerAppleEmbedded::
// _dispatch_input_events -> _window_callback -> this callback. Every
// event Input processes is dispatched to the window callback again,
// so a re-fed event comes straight back here; re-feeding it again
// loops forever and the FrontBoard watchdog kills the app
// (0x8BADF00D, scene-update). Defence: every event the pump feeds is
// stamped with metadata first, and the callback drops stamped events
// instead of enqueueing them again.
//
// What the game does with a right press (verified in sts2.dll):
//   - a card selected for targeting is cancelled (NMouseCardPlay._Input)
//   - map drawing in mouse-mode stops (NMouseModeMapDrawingInput)
// The platform's own touch-emulated left press still happens alongside;
// the right press is the one the game acts on.
internal static class TouchRightClick
{
    private const string FedMark = "sts2_ios_fed";

    private static readonly Queue<InputEvent> Pending = new();
    private static readonly HashSet<int> FingersDown = new();
    private static int _primary = -1;
    private static Vector2 _cursor;
    private static bool _rightDown;

    public static void Install()
    {
        if (Engine.GetMainLoop() is not SceneTree tree)
        {
            Hooks.Note("two-finger right click: no SceneTree, not installed");
            return;
        }
        DisplayServer.WindowSetInputEventCallback(Callable.From<InputEvent>(OnInputEvent));
        tree.ProcessFrame += Pump;
        Hooks.Note("two-finger right click installed (queued feed)");
    }

    // OS callback context: enqueue only. Never touch Input or game state
    // from here. Events stamped by the pump are echoes of events Input
    // already processed (see the header note) — drop them, or the feed
    // loops forever.
    private static void OnInputEvent(InputEvent ev)
    {
        if (ev.HasMeta(FedMark))
            return;
        lock (Pending)
            Pending.Enqueue(ev);
    }

    private static void Pump()
    {
        List<InputEvent> batch;
        lock (Pending)
        {
            if (Pending.Count == 0)
                return;
            batch = new List<InputEvent>(Pending);
            Pending.Clear();
        }

        foreach (var ev in batch)
        {
            ev.SetMeta(FedMark, true);
            Input.ParseInputEvent(ev);
            switch (ev)
            {
                case InputEventScreenTouch touch:
                    OnTouch(touch);
                    break;
                case InputEventScreenDrag drag:
                    OnDrag(drag);
                    break;
            }
        }
    }

    private static void OnTouch(InputEventScreenTouch touch)
    {
        if (touch.Pressed)
        {
            if (FingersDown.Count == 0)
            {
                _primary = touch.Index;
                _cursor = touch.Position;
            }
            FingersDown.Add(touch.Index);
            if (FingersDown.Count == 2 && !_rightDown)
            {
                _rightDown = true;
                SendMouseButton(MouseButton.Right, true);
            }
        }
        else
        {
            FingersDown.Remove(touch.Index);
            if (touch.Index == _primary)
                _primary = -1;
            if (FingersDown.Count < 2 && _rightDown)
            {
                _rightDown = false;
                SendMouseButton(MouseButton.Right, false);
            }
            if (FingersDown.Count == 0)
                _primary = -1;
        }
    }

    private static void OnDrag(InputEventScreenDrag drag)
    {
        if (drag.Index == _primary)
            _cursor = drag.Position;
        if (_rightDown && drag.Index == _primary)
        {
            var motion = new InputEventMouseMotion
            {
                Position = _cursor,
                GlobalPosition = _cursor,
                Relative = drag.Relative,
                ButtonMask = MouseButtonMask.Right,
            };
            motion.SetMeta(FedMark, true);
            Input.ParseInputEvent(motion);
        }
    }

    private static void SendMouseButton(MouseButton button, bool pressed)
    {
        var ev = new InputEventMouseButton
        {
            ButtonIndex = button,
            Pressed = pressed,
            Position = _cursor,
            GlobalPosition = _cursor,
            ButtonMask = pressed ? MouseButtonMask.Right : (MouseButtonMask)0,
        };
        ev.SetMeta(FedMark, true);
        Input.ParseInputEvent(ev);
    }
}
