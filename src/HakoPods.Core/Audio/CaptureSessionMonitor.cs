using System.Runtime.InteropServices;

namespace HakoPods.Core.Audio;

/// <summary>
/// Reports whether any application is currently capturing audio from any capture endpoint.
///
/// This is the signal mic-on-demand needs: while something records, the hands-free profile must
/// be up (so the AirPods' mic exists); the moment it stops, playback should return to stereo.
///
/// The session enumerator hands back interface pointers, so it must be declared with the concrete
/// interface type — declaring it as <c>object</c> makes the marshaller throw "Specified OLE variant
/// is invalid" on every call, which is exactly what happened on the first attempt.
/// </summary>
public sealed class CaptureSessionMonitor
{
    private const int Capture = 1;  // EDataFlow.eCapture
    private const int Active = 1;   // DEVICE_STATE_ACTIVE
    private const int SessionActive = 1;
    private const int ClsCtxAll = 0x17;

    public bool IsAnyCaptureActive()
    {
        try
        {
            var enumerator = (IMMDeviceEnumerator)new MMDeviceEnumeratorComObject();
            if (enumerator.EnumAudioEndpoints(Capture, Active, out var devices) != 0)
            {
                return false;
            }

            devices.GetCount(out var deviceCount);

            for (var index = 0; index < deviceCount; index++)
            {
                if (devices.Item(index, out var device) == 0 && HasActiveSession(device))
                {
                    return true;
                }
            }

            return false;
        }
        catch (COMException)
        {
            // An audio-service hiccup must not look like a call starting.
            return false;
        }
    }

    private static bool HasActiveSession(IMMDevice device)
    {
        var managerId = typeof(IAudioSessionManager2).GUID;

        if (device.Activate(ref managerId, ClsCtxAll, IntPtr.Zero, out var managerObject) != 0
            || managerObject is not IAudioSessionManager2 manager)
        {
            return false;
        }

        if (manager.GetSessionEnumerator(out var sessions) != 0)
        {
            return false;
        }

        sessions.GetCount(out var count);

        for (var index = 0; index < count; index++)
        {
            if (sessions.GetSession(index, out var session) != 0 || session is null)
            {
                continue;
            }

            if (session.GetState(out var state) == 0 && state == SessionActive)
            {
                return true;
            }
        }

        return false;
    }

    [ComImport, Guid("BFB7FF88-7239-4FC9-8FA2-07C950BE9C6D"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioSessionControl
    {
        int GetState(out int state);
        int GetDisplayName(out IntPtr name);
        int SetDisplayName([MarshalAs(UnmanagedType.LPWStr)] string name, IntPtr format);
        int GetIconPath(out IntPtr path);
        int SetIconPath([MarshalAs(UnmanagedType.LPWStr)] string path, IntPtr format);
        int GetGroupingParam(out Guid grouping);
        int SetGroupingParam(ref Guid grouping, Guid format);
        int RegisterAudioSessionNotification(IntPtr notification);
        int UnregisterAudioSessionNotification(IntPtr notification);
    }

    [ComImport, Guid("E2F5BB11-0570-40CA-ACDD-3AA01277DEE8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioSessionEnumerator
    {
        int GetCount(out int count);

        int GetSession(int index, [MarshalAs(UnmanagedType.Interface)] out IAudioSessionControl session);
    }

    [ComImport, Guid("77AA99A0-1BD6-484F-8BC7-2C654C9A9B6F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioSessionManager2
    {
        int GetAudioSessionControl(IntPtr format, uint flags, out IntPtr session);
        int GetSimpleAudioVolume(IntPtr format, uint flags, out IntPtr volume);
        int GetSessionEnumerator(out IAudioSessionEnumerator sessions);
        int RegisterSessionNotification(IntPtr notification);
        int UnregisterSessionNotification(IntPtr notification);
        int RegisterDuckNotification([MarshalAs(UnmanagedType.LPWStr)] string sessionId, IntPtr notification);
        int UnregisterDuckNotification(IntPtr notification);
    }
}
