using System.Collections.Generic;
using Godot;

namespace Sts2Ios;

// Two fingers on the screen act as a right mouse click, mirroring the PC
// controls the touch build cannot express: a right press cancels a card
// being played (NMouseCardPlay cancels on a right-button press) and, in
// the map's held drawing mode, draws while held.
//
// Raw touches are observed through DisplayServer's input-event callback:
// the support assembly cannot register its own Node subclasses, so, like
// FrameWatch, everything hangs off engine-level hooks. Synthetic events go
// through Input.ParseInputEvent, which reaches the game's _Input handlers
// exactly like real mouse events. The platform's own touch-emulated left
// events still flow alongside; the right press is what the game acts on.
public static class TouchRightClick
{
    private static readonly HashSet<int> _fingers = new();
    private static int _primary = -1;
    private static Vector2 _cursor;
    private static bool _rightDown;
    private static bool _installed;

    public static void Install()
    {
        if (_installed) return;
        _installed = true;
        DisplayServer.WindowSetInputEventCallback(Callable.From<InputEvent>(OnInputEvent));
        Hooks.Note("two-finger right click installed");
    }

    private static void OnInputEvent(InputEvent ev)
    {
        // On iOS this callback REPLACES the engine's own input feed: events
        // delivered here never reach the Input singleton on their own, so
        // every event must be re-fed first. Without this the game gets no
        // input at all (title screen renders but ignores every tap).
        Input.ParseInputEvent(ev);

        switch (ev)
        {
            case InputEventScreenTouch touch:
                if (touch.Pressed)
                {
                    if (_fingers.Add(touch.Index))
                    {
                        if (_primary < 0)
                        {
                            _primary = touch.Index;
                            _cursor = touch.Position;
                        }
                        if (_fingers.Count == 2 && !_rightDown)
                        {
                            _rightDown = true;
                            SendButton(pressed: true);
                        }
                    }
                }
                else if (_fingers.Remove(touch.Index))
                {
                    if (_rightDown && _fingers.Count < 2)
                    {
                        _rightDown = false;
                        SendButton(pressed: false);
                    }
                    if (touch.Index == _primary)
                    {
                        _primary = -1;
                        foreach (var other in _fingers)
                        {
                            _primary = other;
                            break;
                        }
                    }
                    if (_fingers.Count == 0) _primary = -1;
                }
                break;

            case InputEventScreenDrag drag:
                if (drag.Index == _primary && _fingers.Count > 0)
                {
                    var relative = drag.Position - _cursor;
                    _cursor = drag.Position;
                    if (_rightDown) SendMotion(relative);
                }
                break;
        }
    }

    private static void SendButton(bool pressed)
    {
        Input.ParseInputEvent(new InputEventMouseButton
        {
            ButtonIndex = MouseButton.Right,
            Pressed = pressed,
            Position = _cursor,
            GlobalPosition = _cursor,
            ButtonMask = pressed ? MouseButtonMask.Right : (MouseButtonMask)0,
        });
    }

    private static void SendMotion(Vector2 relative)
    {
        Input.ParseInputEvent(new InputEventMouseMotion
        {
            Position = _cursor,
            GlobalPosition = _cursor,
            Relative = relative,
            ButtonMask = MouseButtonMask.Right,
        });
    }
}
