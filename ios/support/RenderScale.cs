using Godot;

namespace Sts2Ios;

// Renders the game at ~0.75x resolution and upscales, when the game's
// stretch setup allows it: content_scale_factor only lowers the rendering
// resolution in "viewport" stretch mode. In "canvas_items" mode the same
// factor would scale the UI up instead of lowering the pixel count, so
// the factor is left untouched there and the decision is logged.
public static class RenderScale
{
    private const float ExtraFactor = 1.3333f; // 1 / 0.75

    public static void Apply()
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
            Hooks.Note("render scale: not viewport stretch; resolution unchanged");
            return;
        }
        root.ContentScaleFactor *= ExtraFactor;
        Hooks.Note($"render scale: factor now {root.ContentScaleFactor} (renders at ~0.75x)");
    }
}
