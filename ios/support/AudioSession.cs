using System;
using System.Runtime.InteropServices;
using System.Text;

namespace Sts2Ios;

// Godot's default iOS audio session category is Ambient, which the system
// silences whenever the phone is in silent mode. When the Settings toggle
// is on (the default), switch the shared AVAudioSession to the Playback
// category at the first frame so game audio keeps playing in silent mode,
// like most games. When the toggle is off, nothing is touched and Godot's
// default behaviour stands.
//
// There is no managed binding for AVAudioSession in this build, so this
// goes through the Objective-C runtime directly (objc_msgSend), the same
// P/Invoke style IosSettings uses for CoreFoundation. The category
// constant AVAudioSessionCategoryPlayback is an NSString whose value is
// that literal text, and the session compares categories by content, so a
// created string with the same text selects it. The resulting category is
// read back and logged, so godot.log shows what the session ended up as.
//
// Known unknown: if the engine re-asserts its own category later in the
// session (for example after an audio interruption), the toggle would
// stop holding until the next launch. Only a device check can rule that
// out; the read-back log line is the evidence to look at.
internal static class AudioSession
{
    private const string LibObjC = "/usr/lib/libobjc.A.dylib";

    private const string CoreFoundation =
        "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";

    private const uint Utf8 = 0x08000100; // kCFStringEncodingUTF8

    [DllImport(LibObjC, EntryPoint = "objc_getClass")]
    private static extern IntPtr GetClass(string name);

    [DllImport(LibObjC, EntryPoint = "sel_registerName")]
    private static extern IntPtr RegisterSelector(string name);

    // id objc_msgSend(id self, SEL op)
    [DllImport(LibObjC, EntryPoint = "objc_msgSend")]
    private static extern IntPtr SendId(IntPtr self, IntPtr selector);

    // BOOL objc_msgSend(id self, SEL op, id value, NSError **error)
    [DllImport(LibObjC, EntryPoint = "objc_msgSend")]
    private static extern bool SendBoolWithId(
        IntPtr self,
        IntPtr selector,
        IntPtr value,
        IntPtr error
    );

    // BOOL objc_msgSend(id self, SEL op, BOOL value, NSError **error)
    [DllImport(LibObjC, EntryPoint = "objc_msgSend")]
    private static extern bool SendBoolWithBool(
        IntPtr self,
        IntPtr selector,
        bool value,
        IntPtr error
    );

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
    private static extern void CFRelease(IntPtr obj);

    public static void Apply()
    {
        if (!IosSettings.SoundInSilentModeEnabled)
        {
            Hooks.Note("audio session: silent-mode sound off in iOS Settings");
            return;
        }
        try
        {
            var sessionClass = GetClass("AVAudioSession");
            if (sessionClass == IntPtr.Zero)
            {
                Hooks.Note("audio session: AVAudioSession class not found");
                return;
            }
            var session = SendId(sessionClass, RegisterSelector("sharedInstance"));
            if (session == IntPtr.Zero)
            {
                Hooks.Note("audio session: no shared instance");
                return;
            }

            var category = CFStringCreateWithCString(
                IntPtr.Zero,
                "AVAudioSessionCategoryPlayback",
                Utf8
            );
            try
            {
                bool categorized = SendBoolWithId(
                    session,
                    RegisterSelector("setCategory:error:"),
                    category,
                    IntPtr.Zero
                );
                bool activated = SendBoolWithBool(
                    session,
                    RegisterSelector("setActive:error:"),
                    true,
                    IntPtr.Zero
                );
                Hooks.Note(
                    $"audio session: setCategory Playback={categorized}, "
                        + $"setActive={activated}, category now '{ReadCategory(session)}'"
                );
            }
            finally
            {
                if (category != IntPtr.Zero)
                    CFRelease(category);
            }
        }
        catch (Exception e)
        {
            Hooks.Note($"audio session: failed: {e.Message}");
        }
    }

    // The session's current category as text. The returned NSString is
    // autoreleased by the runtime, so it is read but never released here.
    private static string ReadCategory(IntPtr session)
    {
        var current = SendId(session, RegisterSelector("category"));
        if (current == IntPtr.Zero)
            return "?";
        var buffer = new byte[128];
        if (!CFStringGetCString(current, buffer, buffer.Length, Utf8))
            return "?";
        int length = Array.IndexOf(buffer, (byte)0);
        return Encoding.UTF8.GetString(
            buffer,
            0,
            length < 0 ? buffer.Length : length
        );
    }
}
