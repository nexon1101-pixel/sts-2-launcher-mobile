using System;
using System.Runtime.InteropServices;
using System.Text;
using Godot;

namespace Sts2Ios;

// Reads this app's page in the iOS Settings app. The build ships a
// Settings.bundle inside the .app (ios/Settings.bundle); the Settings app
// writes the chosen values into this app's preferences domain, and they
// are read back here through CFPreferences at startup.
//
// Only controls the game's own settings don't offer live here: the render
// scale preset (the game cannot change its rendering resolution at all)
// and the two-finger right-click toggle. Every read failure falls back to
// the defaults below, so a problem here can never break startup.
internal static class IosSettings
{
    private const string CoreFoundation =
        "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";

    private const uint Utf8 = 0x08000100; // kCFStringEncodingUTF8

    [DllImport(CoreFoundation)]
    private static extern IntPtr CFPreferencesCopyAppValue(IntPtr key, IntPtr applicationId);

    [DllImport(CoreFoundation)]
    private static extern IntPtr CFStringCreateWithCString(
        IntPtr alloc,
        string value,
        uint encoding
    );

    [DllImport(CoreFoundation)]
    private static extern bool CFStringGetCString(
        IntPtr str,
        byte[] buffer,
        long bufferSize,
        uint encoding
    );

    [DllImport(CoreFoundation)]
    private static extern IntPtr CFGetTypeID(IntPtr obj);

    [DllImport(CoreFoundation)]
    private static extern IntPtr CFStringGetTypeID();

    [DllImport(CoreFoundation)]
    private static extern IntPtr CFBooleanGetTypeID();

    [DllImport(CoreFoundation)]
    private static extern bool CFBooleanGetValue(IntPtr boolean);

    [DllImport(CoreFoundation)]
    private static extern void CFRelease(IntPtr obj);

    private static bool? _twoFinger;

    public static bool TwoFingerEnabled
    {
        get
        {
            _twoFinger ??= ReadBool("two_finger_right_click") ?? true;
            return _twoFinger.Value;
        }
    }

    public static void Apply()
    {
        string preset = ReadString("quality_preset") ?? "off";
        Hooks.Note($"ios settings: quality_preset={preset}");
        switch (preset)
        {
            case "off":
                Hooks.Note("ios settings: render scale off");
                break;
            case "aggressive":
                RenderScale.Apply(2.0f); // renders at 0.5x
                break;
            default: // "light"
                RenderScale.Apply(1.3333f); // renders at ~0.75x
                break;
        }
    }

    private static string ReadString(string key)
    {
        try
        {
            var value = CopyAppValue(key);
            if (value == IntPtr.Zero)
                return null;
            try
            {
                if (CFGetTypeID(value) != CFStringGetTypeID())
                    return null;
                var buffer = new byte[512];
                if (!CFStringGetCString(value, buffer, buffer.Length, Utf8))
                    return null;
                int length = Array.IndexOf(buffer, (byte)0);
                return Encoding.UTF8.GetString(buffer, 0, length < 0 ? buffer.Length : length);
            }
            finally
            {
                CFRelease(value);
            }
        }
        catch (Exception e)
        {
            Hooks.Note($"ios settings: reading '{key}' failed: {e.Message}");
            return null;
        }
    }

    private static bool? ReadBool(string key)
    {
        try
        {
            var value = CopyAppValue(key);
            if (value == IntPtr.Zero)
                return null;
            try
            {
                if (CFGetTypeID(value) != CFBooleanGetTypeID())
                    return null;
                return CFBooleanGetValue(value);
            }
            finally
            {
                CFRelease(value);
            }
        }
        catch (Exception e)
        {
            Hooks.Note($"ios settings: reading '{key}' failed: {e.Message}");
            return null;
        }
    }

    // CFPreferencesCopyAppValue with kCFPreferencesCurrentApplication: the
    // constant's value is that literal string, and CFPreferences compares
    // it by content, so a created string with the same text selects the
    // current application's preferences domain.
    private static IntPtr CopyAppValue(string key)
    {
        var keyRef = CFStringCreateWithCString(IntPtr.Zero, key, Utf8);
        var appRef = CFStringCreateWithCString(
            IntPtr.Zero,
            "kCFPreferencesCurrentApplication",
            Utf8
        );
        try
        {
            return CFPreferencesCopyAppValue(keyRef, appRef);
        }
        finally
        {
            if (keyRef != IntPtr.Zero)
                CFRelease(keyRef);
            if (appRef != IntPtr.Zero)
                CFRelease(appRef);
        }
    }
}
