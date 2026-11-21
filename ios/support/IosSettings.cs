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
// scale slider (the game cannot change its rendering resolution at all),
// the touch gesture toggles, and the silent-mode sound toggle. Every read
// failure falls back to the defaults below, so a problem here can never
// break startup.
internal static class IosSettings
{
    private const string CoreFoundation =
        "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";

    private const uint Utf8 = 0x08000100; // kCFStringEncodingUTF8
    private const long NumberDoubleType = 13; // kCFNumberDoubleType

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
    private static extern IntPtr CFNumberGetTypeID();

    [DllImport(CoreFoundation)]
    private static extern bool CFNumberGetValue(IntPtr number, long type, out double value);

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

    private static bool? _threeFingerEsc;

    public static bool ThreeFingerEscEnabled
    {
        get
        {
            _threeFingerEsc ??= ReadBool("three_finger_esc") ?? true;
            return _threeFingerEsc.Value;
        }
    }

    private static bool? _soundInSilentMode;

    public static bool SoundInSilentModeEnabled
    {
        get
        {
            _soundInSilentMode ??= ReadBool("sound_in_silent_mode") ?? true;
            return _soundInSilentMode.Value;
        }
    }

    public static void Apply()
    {
        // The Settings page slider: 1.0 is the game's standard quality,
        // lower renders at that fraction of the resolution.
        double scale = ReadNumber("render_scale") ?? 1.0;
        if (scale < 0.4)
            scale = 0.4;
        if (scale > 1.0)
            scale = 1.0;
        Hooks.Note($"ios settings: render_scale={scale:0.00}");
        if (scale >= 0.999)
        {
            Hooks.Note("ios settings: render scale off");
            return;
        }
        RenderScale.Apply((float)(1.0 / scale));
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

    private static double? ReadNumber(string key)
    {
        try
        {
            var value = CopyAppValue(key);
            if (value == IntPtr.Zero)
                return null;
            try
            {
                if (CFGetTypeID(value) != CFNumberGetTypeID())
                    return null;
                return CFNumberGetValue(value, NumberDoubleType, out double result)
                    ? result
                    : null;
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
