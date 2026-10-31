using System.Runtime.InteropServices;

namespace HakoPods.Core.Audio;

/// <summary>
/// Switches the default playback device through <c>IPolicyConfig</c>, the same undocumented API
/// the APW-017 spike measured (4–5 ms for the console role, 12–16 ms for all three, no elevation).
///
/// The router never guesses which endpoint is "the AirPods": endpoint ids are the only stable
/// handle Windows exposes, so the app switches to an id it has been told about. Learning that id
/// belongs to the device-picker work; this class just performs and reports.
/// </summary>
public sealed class AudioEndpointRouter
{
    private const int Render = 0;      // EDataFlow.eRender
    private const int Active = 1;      // DEVICE_STATE_ACTIVE
    private const int RoleConsole = 0;
    private const int RoleMultimedia = 1;
    private const int RoleCommunications = 2;

    public IReadOnlyList<string> ListRenderEndpoints()
    {
        var deviceEnumerator = (IMMDeviceEnumerator)new MMDeviceEnumeratorComObject();
        if (deviceEnumerator.EnumAudioEndpoints(Render, Active, out var collection) != 0)
        {
            return Array.Empty<string>();
        }

        collection.GetCount(out var count);
        var ids = new List<string>(count);

        for (var index = 0; index < count; index++)
        {
            if (collection.Item(index, out var device) == 0 && device.GetId(out var id) == 0 && !string.IsNullOrEmpty(id))
            {
                ids.Add(id);
            }
        }

        return ids;
    }

    /// <summary>Id plus the name a person would recognise; both are needed to build a picker.</summary>
    public IReadOnlyList<(string Id, string? Name)> ListRenderEndpointsDetailed()
    {
        var deviceEnumerator = (IMMDeviceEnumerator)new MMDeviceEnumeratorComObject();
        if (deviceEnumerator.EnumAudioEndpoints(Render, Active, out var collection) != 0)
        {
            return Array.Empty<(string, string?)>();
        }

        collection.GetCount(out var count);
        var results = new List<(string, string?)>(count);

        for (var index = 0; index < count; index++)
        {
            if (collection.Item(index, out var device) != 0 || device.GetId(out var id) != 0 || string.IsNullOrEmpty(id))
            {
                continue;
            }

            string? name = null;
            try
            {
                name = AudioEndpointNames.TryGetFriendlyName(device);
            }
            catch (Exception)
            {
                // A single unreadable property store must not hide the other endpoints.
            }

            results.Add((id, name));
        }

        return results;
    }

    public string? GetDefaultEndpointId(int role = RoleConsole)
    {
        try
        {
            var deviceEnumerator = (IMMDeviceEnumerator)new MMDeviceEnumeratorComObject();
            return deviceEnumerator.GetDefaultAudioEndpoint(Render, role, out var device) == 0
                && device.GetId(out var id) == 0
                ? id
                : null;
        }
        catch (COMException)
        {
            return null;
        }
    }

    /// <summary>
    /// All three roles, because switching only the console role leaves VoIP and media on the old
    /// device — the failure people describe as "music went to the AirPods but Teams did not".
    /// </summary>
    public bool TrySetDefault(string endpointId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(endpointId);

        try
        {
            var policy = (IPolicyConfig)new PolicyConfigClient();
            var console = policy.SetDefaultEndpoint(endpointId, RoleConsole);
            var multimedia = policy.SetDefaultEndpoint(endpointId, RoleMultimedia);
            var communications = policy.SetDefaultEndpoint(endpointId, RoleCommunications);

            return console == 0 && multimedia == 0 && communications == 0;
        }
        catch (COMException)
        {
            // Undocumented API: a Windows update may remove it. Report, never crash the tray.
            return false;
        }
        catch (InvalidCastException)
        {
            return false;
        }
    }
}
