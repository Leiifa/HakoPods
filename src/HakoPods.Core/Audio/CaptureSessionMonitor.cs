using System.Runtime.InteropServices;

namespace HakoPods.Core.Audio;

/// <summary>
/// Reports whether any application is currently capturing audio from any capture endpoint.
///
/// This is the signal mic-on-demand needs: while something records, the hands-free profile must
/// be up (so the AirPods' mic exists), and the moment it stops, playback should return to stereo.
/// Windows exposes it through the per-device session enumerator.
/// </summary>
public sealed class CaptureSessionMonitor
{
    private const int Capture = 1;      // EDataFlow.eCapture
    private const int Active = 1;       // DEVICE_STATE_ACTIVE
    private const int AudioSessionActive = 1;

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
                if (devices.Item(index, out var device) != 0)
                {
                    continue;
                }

                if (HasActiveSession(device))
                {
                    return true;
                }
            }

            return false;
        }
        catch (COMException)
        {
            // An audio service hiccup must not make the app think a call started.
            return false;
        }
    }

    private static bool HasActiveSession(IMMDevice device)
    {
        var sessionManagerId = typeof(IAudioSessionManager2).GUID;

        if (device.Activate(ref sessionManagerId, 0x17 /* CLSCTX_ALL */, IntPtr.Zero, out var managerObject) != 0
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
            if (sessions.GetSession(index, out var control) != 0 || control is not IAudioSessionControl2 session)
            {
                continue;
            }

            if (session.GetState(out var state) == 0 && state == AudioSessionActive)
            {
                return true;
            }
        }

        return false;
    }

    [ComImport, Guid("77AA99A0-1BD6-484F-8BC7-2C654C9A9B6F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioSessionManager2
    {
        // IAudioSessionManager
        int GetAudioSessionControl(IntPtr format, uint flags, out IntPtr session);
        int GetSimpleAudioVolume(IntPtr format, uint flags, out IntPtr volume);
        // IAudioSessionManager2
        int GetSessionEnumerator(out IAudioSessionEnumerator sessions);
        int RegisterSessionNotification(IntPtr notification);
        int UnregisterSessionNotification(IntPtr notification);
        int RegisterDuckNotification(string sessionId, IntPtr notification);
        int UnregisterDuckNotification(IntPtr notification);
    }

    [ComImport, Guid("E2F5BB11-0570-40CA-ACDD-3AA01277DEE8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioSessionEnumerator
    {
        int GetCount(out int count);
        int GetSession(int index, out object session);
    }

    [ComImport, Guid("BFB7FF88-7239-4FC9-8FA2-07C950BE9C6D"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioSessionControl2
    {
        // IAudioSessionControl
        int GetState(out int state);
        int GetDisplayName(out IntPtr name);
        int SetDisplayName(string name, IntPtr format);
        int GetIconPath(out IntPtr path);
        int SetIconPath(string path, IntPtr format);
        int GetGroupingParam(out Guid grouping);
        int SetGroupingParam(ref Guid grouping, Guid format);
        int RegisterAudioSessionNotification(IntPtr notification);
        int UnregisterAudioSessionNotification(IntPtr notification);
        // IAudioSessionControl2
        int GetSessionIdentifier(out IntPtr id);
        int GetSessionInstanceIdentifier(out IntPtr id);
        int GetProcessId(out uint processId);
        int IsSystemSoundsSession();
        int SetDuckingPreference(bool optOut);
    }
}
