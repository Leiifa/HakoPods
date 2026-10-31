using System.Runtime.InteropServices;

namespace HakoPods.Core.Bluetooth;

/// <summary>
/// Win32 adapter over <c>bthprops.cpl</c>: radio handle, device info and service state.
///
/// Measured behaviour (2026-10-31): <c>SetServiceState(ENABLE)</c> blocks ~3 s and then a
/// connect is visible ~250 ms later; <c>DISABLE</c> removes the audio endpoint but leaves
/// <c>fConnected = true</c>, so that flag must never be used as a disconnect signal.
/// </summary>
public sealed class Win32BluetoothNative : IBluetoothNative, IDisposable
{
    public static readonly Guid A2dpSinkService = new("0000110B-0000-1000-8000-00805F9B34FB");

    public static readonly Guid HandsfreeService = new("0000111E-0000-1000-8000-00805F9B34FB");

    private IntPtr _radio;
    private IntPtr _find;
    private bool _disposed;

    public bool TryGetDevice(ulong address, out BluetoothDeviceSnapshot snapshot)
    {
        snapshot = default;
        if (!EnsureRadio())
        {
            return false;
        }

        var info = CreateDeviceInfo(address);
        if (BluetoothGetDeviceInfo(_radio, ref info) != 0)
        {
            return false;
        }

        snapshot = new BluetoothDeviceSnapshot(info.fConnected, info.fRemembered, info.szName ?? string.Empty);
        return true;
    }

    public uint SetServiceState(ulong address, Guid service, bool enable)
    {
        if (!EnsureRadio())
        {
            return 1168; // ERROR_NOT_FOUND: no radio to act on
        }

        var info = CreateDeviceInfo(address);
        if (BluetoothGetDeviceInfo(_radio, ref info) != 0)
        {
            return 1168;
        }

        return BluetoothSetServiceState(_radio, ref info, ref service, enable ? 1u : 0u);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        if (_find != IntPtr.Zero)
        {
            BluetoothFindRadioClose(_find);
            _find = IntPtr.Zero;
        }

        if (_radio != IntPtr.Zero)
        {
            CloseHandle(_radio);
            _radio = IntPtr.Zero;
        }
    }

    private bool EnsureRadio()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_radio != IntPtr.Zero)
        {
            return true;
        }

        var parameters = new BluetoothFindRadioParams
        {
            dwSize = (uint)Marshal.SizeOf<BluetoothFindRadioParams>(),
        };

        _find = BluetoothFindFirstRadio(ref parameters, out _radio);
        return _find != IntPtr.Zero && _radio != IntPtr.Zero;
    }

    private static BluetoothDeviceInfo CreateDeviceInfo(ulong address) => new()
    {
        dwSize = (uint)Marshal.SizeOf<BluetoothDeviceInfo>(),
        Address = address,
    };

    [DllImport("bthprops.cpl", SetLastError = true)]
    private static extern IntPtr BluetoothFindFirstRadio(ref BluetoothFindRadioParams parameters, out IntPtr radio);

    [DllImport("bthprops.cpl", SetLastError = true)]
    private static extern bool BluetoothFindRadioClose(IntPtr find);

    [DllImport("bthprops.cpl", SetLastError = true)]
    private static extern uint BluetoothGetDeviceInfo(IntPtr radio, ref BluetoothDeviceInfo info);

    [DllImport("bthprops.cpl", SetLastError = true)]
    private static extern uint BluetoothSetServiceState(IntPtr radio, ref BluetoothDeviceInfo info, ref Guid service, uint flags);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr handle);

    [StructLayout(LayoutKind.Sequential)]
    private struct BluetoothFindRadioParams
    {
        public uint dwSize;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct BluetoothDeviceInfo
    {
        public uint dwSize;
        public ulong Address;
        public uint ulClassofDevice;
        [MarshalAs(UnmanagedType.Bool)] public bool fConnected;
        [MarshalAs(UnmanagedType.Bool)] public bool fRemembered;
        [MarshalAs(UnmanagedType.Bool)] public bool fAuthenticated;
        public SystemTime stLastSeen;
        public SystemTime stLastUsed;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 248)] public string szName;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SystemTime
    {
        public ushort wYear;
        public ushort wMonth;
        public ushort wDayOfWeek;
        public ushort wDay;
        public ushort wHour;
        public ushort wMinute;
        public ushort wSecond;
        public ushort wMilliseconds;
    }
}
