using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;

namespace Dnp.Transport.Windows;

internal static class WindowsUsbDevicePathResolver
{
    // USB printer interface class GUID.
    private static readonly Guid PrinterInterfaceGuid = new("28d78fad-5a12-11D1-ae5b-0000f803a8c2");
    // Generic USB device interface class GUID.
    private static readonly Guid UsbDeviceInterfaceGuid = new("A5DCBF10-6530-11D2-901F-00C04FB951ED");

    public static string? ResolveDevicePath(WindowsUsbIdentity? identity)
    {
        if (!OperatingSystem.IsWindows() || string.IsNullOrWhiteSpace(identity?.UsbInstanceId))
        {
            return null;
        }

        if (!WindowsUsbVidPidCatalog.IsKnownDnpPair(identity.VendorId, identity.ProductId))
        {
            return null;
        }

        var candidates = EnumerateRelevantInterfaces().ToArray();

        foreach (var item in candidates)
        {
            if (MatchesUsbIdentity(item, identity))
            {
                return item.DevicePath;
            }
        }

        if (!string.IsNullOrWhiteSpace(identity.VendorId) && !string.IsNullOrWhiteSpace(identity.ProductId))
        {
            foreach (var item in candidates)
            {
                if (ContainsVidPid(item.DevicePath, identity.VendorId, identity.ProductId)
                    || ContainsVidPid(item.DeviceInstanceId, identity.VendorId, identity.ProductId))
                {
                    return item.DevicePath;
                }
            }
        }

        return null;
    }

    private static bool MatchesUsbIdentity((string DevicePath, string? DeviceInstanceId, Guid InterfaceGuid) item, WindowsUsbIdentity identity)
    {
        if (string.IsNullOrWhiteSpace(item.DevicePath))
        {
            return false;
        }

        if (item.DevicePath.Equals(identity.UsbInstanceId, StringComparison.OrdinalIgnoreCase)
            || item.DevicePath.Equals(identity.UsbPrintInstanceId, StringComparison.OrdinalIgnoreCase)
            || item.DevicePath.Equals(identity.PnpInstanceId, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (!string.IsNullOrWhiteSpace(item.DeviceInstanceId))
        {
            if (item.DeviceInstanceId.Equals(identity.UsbInstanceId, StringComparison.OrdinalIgnoreCase)
                || item.DeviceInstanceId.Equals(identity.UsbPrintInstanceId, StringComparison.OrdinalIgnoreCase)
                || item.DeviceInstanceId.Equals(identity.PnpInstanceId, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            var parent = FindUsbParentInstanceId(item.DeviceInstanceId);
            if (!string.IsNullOrWhiteSpace(identity.UsbInstanceId)
                && parent?.Equals(identity.UsbInstanceId, StringComparison.OrdinalIgnoreCase) == true)
            {
                return true;
            }
        }

        return ContainsVidPid(item.DevicePath, identity.VendorId, identity.ProductId)
            && (string.IsNullOrWhiteSpace(identity.SerialNumber)
                || item.DevicePath.Contains(identity.SerialNumber, StringComparison.OrdinalIgnoreCase));
    }

    private static string? FindUsbParentInstanceId(string deviceInstanceId)
    {
        var devInst = GetDeviceInstance(deviceInstanceId);
        if (devInst is null)
        {
            return null;
        }

        const int maxDepth = 8;
        var current = devInst.Value;
        for (var depth = 0; depth < maxDepth; depth++)
        {
            var currentId = GetDeviceId(current);
            if (!string.IsNullOrWhiteSpace(currentId)
                && currentId.StartsWith("USB\\VID_", StringComparison.OrdinalIgnoreCase))
            {
                return currentId;
            }

            var result = NativeMethods.CM_Get_Parent(out var parent, current, 0);
            if (result != 0)
            {
                break;
            }

            current = parent;
        }

        return null;
    }

    private static uint? GetDeviceInstance(string instanceId)
    {
        var handle = NativeMethods.SetupDiGetClassDevsW(IntPtr.Zero, null, IntPtr.Zero, NativeMethods.DIGCF_ALLCLASSES | NativeMethods.DIGCF_PRESENT);
        if (handle == IntPtr.Zero || handle == new IntPtr(-1))
        {
            return null;
        }

        try
        {
            for (uint index = 0; ; index++)
            {
                var data = new NativeMethods.SP_DEVINFO_DATA { cbSize = (uint)Marshal.SizeOf<NativeMethods.SP_DEVINFO_DATA>() };
                if (!NativeMethods.SetupDiEnumDeviceInfo(handle, index, ref data))
                {
                    var error = Marshal.GetLastWin32Error();
                    if (error == NativeMethods.ERROR_NO_MORE_ITEMS)
                    {
                        break;
                    }

                    throw new Win32Exception(error);
                }

                var currentId = GetDeviceInstanceId(handle, ref data);
                if (string.Equals(currentId, instanceId, StringComparison.OrdinalIgnoreCase))
                {
                    return data.DevInst;
                }
            }
        }
        finally
        {
            NativeMethods.SetupDiDestroyDeviceInfoList(handle);
        }

        return null;
    }

    private static string GetDeviceInstanceId(IntPtr handle, ref NativeMethods.SP_DEVINFO_DATA data)
    {
        var sb = new StringBuilder(512);
        if (!NativeMethods.SetupDiGetDeviceInstanceIdW(handle, ref data, sb, sb.Capacity, out _))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error());
        }

        return sb.ToString();
    }

    private static string? GetDeviceId(uint devInst)
    {
        var buffer = new StringBuilder(512);
        var result = NativeMethods.CM_Get_Device_IDW(devInst, buffer, buffer.Capacity, 0);
        return result == 0 ? buffer.ToString() : null;
    }

    private static IEnumerable<(string DevicePath, string? DeviceInstanceId, Guid InterfaceGuid)> EnumerateRelevantInterfaces()
    {
        foreach (var item in EnumerateInterfaces(PrinterInterfaceGuid))
        {
            yield return item;
        }

        foreach (var item in EnumerateInterfaces(UsbDeviceInterfaceGuid))
        {
            yield return item;
        }
    }

    private static IEnumerable<(string DevicePath, string? DeviceInstanceId, Guid InterfaceGuid)> EnumerateInterfaces(Guid interfaceClassGuid)
    {
        var guid = interfaceClassGuid;
        var handle = NativeMethods.SetupDiGetClassDevsW(ref guid, null, IntPtr.Zero,
            NativeMethods.DIGCF_PRESENT | NativeMethods.DIGCF_DEVICEINTERFACE);
        if (handle == IntPtr.Zero || handle == new IntPtr(-1))
        {
            yield break;
        }

        try
        {
            for (uint index = 0; ; index++)
            {
                var ifData = new NativeMethods.SP_DEVICE_INTERFACE_DATA { cbSize = (uint)Marshal.SizeOf<NativeMethods.SP_DEVICE_INTERFACE_DATA>() };
                var loopGuid = interfaceClassGuid;
                if (!NativeMethods.SetupDiEnumDeviceInterfaces(handle, IntPtr.Zero, ref loopGuid, index, ref ifData))
                {
                    var error = Marshal.GetLastWin32Error();
                    if (error == NativeMethods.ERROR_NO_MORE_ITEMS)
                    {
                        break;
                    }

                    throw new Win32Exception(error);
                }

                var devInfo = new NativeMethods.SP_DEVINFO_DATA { cbSize = (uint)Marshal.SizeOf<NativeMethods.SP_DEVINFO_DATA>() };
                NativeMethods.SetupDiGetDeviceInterfaceDetailW(handle, ref ifData, IntPtr.Zero, 0, out var required, ref devInfo);
                var detailBuffer = Marshal.AllocHGlobal((int)required);
                try
                {
                    var cbSize = IntPtr.Size == 8 ? 8 : 6;
                    Marshal.WriteInt32(detailBuffer, cbSize);

                    if (!NativeMethods.SetupDiGetDeviceInterfaceDetailW(handle, ref ifData, detailBuffer, required, out _, ref devInfo))
                    {
                        throw new Win32Exception(Marshal.GetLastWin32Error());
                    }

                    var pDevicePath = detailBuffer + 4;
                    var devicePath = Marshal.PtrToStringUni(pDevicePath) ?? string.Empty;
                    var instanceId = GetDeviceId(devInfo.DevInst);
                    yield return (devicePath, instanceId, interfaceClassGuid);
                }
                finally
                {
                    Marshal.FreeHGlobal(detailBuffer);
                }
            }
        }
        finally
        {
            NativeMethods.SetupDiDestroyDeviceInfoList(handle);
        }
    }

    private static bool ContainsVidPid(string? value, string? vendorId, string? productId)
    {
        if (string.IsNullOrWhiteSpace(value) || string.IsNullOrWhiteSpace(vendorId) || string.IsNullOrWhiteSpace(productId))
        {
            return false;
        }

        return value.Contains($"vid_{vendorId}", StringComparison.OrdinalIgnoreCase)
            && value.Contains($"pid_{productId}", StringComparison.OrdinalIgnoreCase);
    }

    private static class NativeMethods
    {
        public const uint DIGCF_PRESENT = 0x00000002;
        public const uint DIGCF_ALLCLASSES = 0x00000004;
        public const uint DIGCF_DEVICEINTERFACE = 0x00000010;
        public const int ERROR_NO_MORE_ITEMS = 259;

        [StructLayout(LayoutKind.Sequential)]
        public struct SP_DEVINFO_DATA
        {
            public uint cbSize;
            public Guid ClassGuid;
            public uint DevInst;
            public IntPtr Reserved;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct SP_DEVICE_INTERFACE_DATA
        {
            public uint cbSize;
            public Guid InterfaceClassGuid;
            public uint Flags;
            public IntPtr Reserved;
        }

        [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "SetupDiGetClassDevsW")]
        public static extern IntPtr SetupDiGetClassDevsW(ref Guid classGuid, string? enumerator, IntPtr hwndParent, uint flags);

        [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "SetupDiGetClassDevsW")]
        public static extern IntPtr SetupDiGetClassDevsW(IntPtr classGuid, string? enumerator, IntPtr hwndParent, uint flags);

        [DllImport("setupapi.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool SetupDiEnumDeviceInfo(IntPtr deviceInfoSet, uint memberIndex, ref SP_DEVINFO_DATA deviceInfoData);

        [DllImport("setupapi.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool SetupDiEnumDeviceInterfaces(IntPtr deviceInfoSet, IntPtr deviceInfoData, ref Guid interfaceClassGuid, uint memberIndex, ref SP_DEVICE_INTERFACE_DATA deviceInterfaceData);

        [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool SetupDiGetDeviceInterfaceDetailW(IntPtr deviceInfoSet, ref SP_DEVICE_INTERFACE_DATA deviceInterfaceData, IntPtr deviceInterfaceDetailData, uint deviceInterfaceDetailDataSize, out uint requiredSize, ref SP_DEVINFO_DATA deviceInfoData);

        [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool SetupDiGetDeviceInstanceIdW(IntPtr deviceInfoSet, ref SP_DEVINFO_DATA deviceInfoData, StringBuilder deviceInstanceId, int deviceInstanceIdSize, out int requiredSize);

        [DllImport("setupapi.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool SetupDiDestroyDeviceInfoList(IntPtr deviceInfoSet);

        [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode)]
        public static extern int CM_Get_Device_IDW(uint devInst, StringBuilder buffer, int bufferLen, int flags);

        [DllImport("cfgmgr32.dll")]
        public static extern int CM_Get_Parent(out uint pdnDevInst, uint dnDevInst, int ulFlags);
    }
}
