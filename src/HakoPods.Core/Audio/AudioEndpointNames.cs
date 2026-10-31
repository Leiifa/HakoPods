using System.Runtime.InteropServices;

namespace HakoPods.Core.Audio;

/// <summary>
/// Reads an endpoint's friendly name ("Headphones (23's AirPods Pro)") out of its property
/// store. Without this a picker can only show GUIDs, and matching by the name string is the
/// only human-meaningful handle Windows offers.
/// </summary>
public static class AudioEndpointNames
{
    private const int StgmRead = 0;
    private const ushort VtLpwstr = 31;

    // PKEY_Device_FriendlyName
    private static readonly PropertyKey FriendlyName = new()
    {
        FormatId = new Guid("A45C254E-DF1C-4EFD-8020-67D146A850E0"),
        PropertyId = 14,
    };

    internal static string? TryGetFriendlyName(IMMDevice device)
    {
        ArgumentNullException.ThrowIfNull(device);

        if (device.OpenPropertyStore(StgmRead, out var storePointer) != 0 || storePointer == IntPtr.Zero)
        {
            return null;
        }

        var store = (IPropertyStore)Marshal.GetObjectForIUnknown(storePointer);

        try
        {
            var key = FriendlyName;
            if (store.GetValue(ref key, out var value) != 0)
            {
                return null;
            }

            try
            {
                return value.VariantType == VtLpwstr && value.PointerValue != IntPtr.Zero
                    ? Marshal.PtrToStringUni(value.PointerValue)
                    : null;
            }
            finally
            {
                PropVariantClear(ref value);
            }
        }
        finally
        {
            Marshal.Release(storePointer);
        }
    }

    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    private struct PropertyKey
    {
        public Guid FormatId;
        public int PropertyId;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PropVariant
    {
        public ushort VariantType;
        public ushort Reserved1;
        public ushort Reserved2;
        public ushort Reserved3;
        public IntPtr PointerValue;
        public IntPtr PointerValue2;
    }

    [ComImport, Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IPropertyStore
    {
        int GetCount(out int count);

        int GetAt(int index, out PropertyKey key);

        int GetValue(ref PropertyKey key, out PropVariant value);

        int SetValue(ref PropertyKey key, ref PropVariant value);

        int Commit();
    }

    [DllImport("ole32.dll", PreserveSig = false)]
    private static extern void PropVariantClear(ref PropVariant variant);
}
