using Godot;

namespace Sts2Ios;

// Renders the game at a lower resolution and upscales. The factor comes
// from the iOS Settings preset (see IosSettings).
//
// The game ships with "canvas_items" stretch, where content_scale_factor
// only scales the UI and the pixel count never changes (run #23's log on
// a real device: mode=CanvasItems, "resolution unchanged"). Switching the
// root window to "viewport" stretch is what actually lowers the rendering
// resolution: the whole frame, UI included, is rendered small and then
// upscaled, so text gets softer as the factor grows. Layout is preserved
// because the content scale size and aspect stay the game's own.
public static class RenderScale
{
    public static void Apply(float extraFactor)
    {
        var root = (Engine.GetMainLoop() as SceneTree)?.Root;
        if (root == null)
        {
            Hooks.Note("render scale: no root window");
            return;
        }
        Hooks.Note($"render scale: mode={root.ContentScaleMode} base={root.ContentScaleSize} factor={root.ContentScaleFactor} window={root.Size}");
        if (root.ContentScaleMode != Window.ContentScaleModeEnum.Viewport)
        {
            root.ContentScaleMode = Window.ContentScaleModeEnum.Viewport;
            Hooks.Note("render scale: switched stretch mode to viewport");
        }
        root.ContentScaleFactor *= extraFactor;
        Hooks.Note($"render scale: factor now {root.ContentScaleFactor} (renders at ~{1.0f / extraFactor:0.00}x)");
    }
}
