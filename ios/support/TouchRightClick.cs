using System.Collections.Generic;
using Godot;

namespace Sts2Ios;

// Two-finger touch = right click.
//
// What the game does with a right press (verified in sts2.dll):
//   - a card selected for targeting is cancelled (NMouseCardPlay._Input)
//   - map drawing in mouse-mode stops (NMouseModeMapDrawingInput)
//
// Mechanism (confirmed against the Godot source behaviour after three
// failed attempts, see below): the root Window's own input handler
// emits the WindowInput signal for every event right before it pushes
// the event into the scene tree. Subscribing to that signal observes
// raw touches (and everything else) in order, without disturbing the
// stock delivery at all. The two-finger state machine runs there;
// synthetic right press / motion / release events are delivered with
// Root.PushInput, the same call the stock handler makes.
//
// Failed attempts, for the record:
//   - DisplayServer.WindowSetInputEventCallback replaces the main
//     window's stock callback (Window._window_input, which ends in
//     push_input), so merely registering it starved the game of all
//     input (run #15: title screen unresponsive).
//   - Re-feeding events with Input.ParseInputEvent from inside that
//     callback looped: the callback is invoked at the end of
//     _parse_input_event_impl, so each re-fed event was dispatched
//     straight back; the FrontBoard watchdog killed the app
//     (0x8BADF00D; device crash log StS2-2026-11-21-155924, run #17).
//   - Queueing the callback's events and re-feeding them from a
//     per-frame pump (with metadata stamps against the echo) avoided
//     the loop but still delivered nothing into the scene tree — and
//     re-feeding already-processed touches would double Input's state
//     and duplicate the emulated clicks anyway (run #20).
internal static class TouchRightClick
{
    private static readonly HashSet<int> FingersDown = new();
    private static Window _root;
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
        _root = tree.Root;
        _root.WindowInput += OnWindowInput;
        Hooks.Note("two-finger right click installed (WindowInput)");
    }

    private static void OnWindowInput(InputEvent ev)
    {
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
            _root?.PushInput(motion);
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
        _root?.PushInput(ev);
    }
}
