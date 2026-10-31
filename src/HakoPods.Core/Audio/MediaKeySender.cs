using System.Runtime.InteropServices;

namespace HakoPods.Core.Audio;

/// <summary>
/// Sends the media play/pause key, which is how the app pauses playback when a bud comes out and
/// resumes when it goes back in.
///
/// Deliberately not a per-app media-session integration: Windows' SMTC would let us pause only
/// "our" session, but the thing playing is the user's browser or player, and the key is what the
/// OS routes to it anyway. It is also the only mechanism that works for every player.
/// </summary>
public static class MediaKeySender
{
    private const byte VkMediaPlayPause = 0xB3;
    private const uint KeyEventKeyUp = 0x0002;

    /// <summary>Toggles playback. Safe to call from the UI thread; it posts and returns.</summary>
    public static void TogglePlayPause()
    {
        keybd_event(VkMediaPlayPause, 0, 0, UIntPtr.Zero);
        keybd_event(VkMediaPlayPause, 0, KeyEventKeyUp, UIntPtr.Zero);
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern void keybd_event(byte virtualKey, byte scanCode, uint flags, UIntPtr extraInfo);
}
