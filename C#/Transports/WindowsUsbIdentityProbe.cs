using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using Dnp.Core;

namespace Dnp.Transport.Windows;

internal static partial class WindowsUsbIdentityProbe
{
    public static WindowsUsbIdentity? TryFind(string? preferredPrinterName = null, string? modelHint = null)
    {
        if (!OperatingSystem.IsWindows())
        {
            return null;
        }

        var devices = EnumerateDevices();
        if (devices.Count == 0)
        {
            return null;
        }

        var preferred = preferredPrinterName?.Trim();
        var candidate = SelectPhysicalUsbCandidate(devices, preferred, modelHint);
        if (candidate is null)
        {
            return null;
        }

        var usbInstanceId = candidate.InstanceId.StartsWith("USB\\VID_", StringComparison.OrdinalIgnoreCase)
            ? candidate.InstanceId
            : FindUsbParentInstanceId(candidate.DevInst);

        if (string.IsNullOrWhiteSpace(usbInstanceId)
            || !usbInstanceId.StartsWith("USB\\VID_", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var (vendorId, productId) = ParseVidPid(usbInstanceId);
        if (string.IsNullOrWhiteSpace(vendorId) || string.IsNullOrWhiteSpace(productId))
        {
            return null;
        }

        if (!WindowsUsbVidPidCatalog.IsKnownDnpPair(vendorId, productId))
        {
            return null;
        }

        var usbPrintInstanceId = devices
            .Where(static x => x.InstanceId.StartsWith("USBPRINT\\", StringComparison.OrdinalIgnoreCase))
            .Where(x => FindUsbParentInstanceId(x.DevInst)?.Equals(usbInstanceId, StringComparison.OrdinalIgnoreCase) == true)
            .OrderByDescending(x => Score(x, preferred, modelHint))
            .Select(static x => x.InstanceId)
            .FirstOrDefault();

        var serial = ParseSerialFromUsbInstanceId(usbInstanceId);

        return new WindowsUsbIdentity(
            candidate.FriendlyName ?? candidate.DeviceDescription,
            candidate.InstanceId,
            usbPrintInstanceId,
            usbInstanceId,
            vendorId,
            productId,
            serial,
            candidate.DeviceDescription,
            candidate.FriendlyName);
    }

    private static WindowsPnPDeviceInfo? SelectPhysicalUsbCandidate(IReadOnlyList<WindowsPnPDeviceInfo> devices, string? preferredPrinterName, string? modelHint)
        => devices
            .Where(static x => IsPhysicalUsbCandidate(x))
            .OrderByDescending(x => Score(x, preferredPrinterName, modelHint))
            .FirstOrDefault();

    private static bool IsPhysicalUsbCandidate(WindowsPnPDeviceInfo device)
    {
        if (device.InstanceId.StartsWith("USB\\VID_", StringComparison.OrdinalIgnoreCase))
        {
            var (vendorId, productId) = ParseVidPid(device.InstanceId);
            return WindowsUsbVidPidCatalog.IsKnownDnpPair(vendorId, productId);
        }

        if (device.InstanceId.StartsWith("USBPRINT\\", StringComparison.OrdinalIgnoreCase))
        {
            var parent = FindUsbParentInstanceId(device.DevInst);
            if (string.IsNullOrWhiteSpace(parent)
                || !parent.StartsWith("USB\\VID_", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            var (vendorId, productId) = ParseVidPid(parent);
            return WindowsUsbVidPidCatalog.IsKnownDnpPair(vendorId, productId);
        }

        return false;
    }

    private static int Score(WindowsPnPDeviceInfo device, string? preferredPrinterName, string? modelHint)
    {
        var score = 0;
        if (!string.IsNullOrWhiteSpace(preferredPrinterName))
        {
            if (string.Equals(device.FriendlyName, preferredPrinterName, StringComparison.OrdinalIgnoreCase)
                || string.Equals(device.DeviceDescription, preferredPrinterName, StringComparison.OrdinalIgnoreCase))
            {
                score += 50;
            }

            if (Contains(device.InstanceId, preferredPrinterName))
            {
                score += 20;
            }
        }

        if (!string.IsNullOrWhiteSpace(modelHint)
            && DnpModelResolver.MatchesHint(modelHint, device.FriendlyName, device.DeviceDescription, device.InstanceId))
        {
            score += 30;
        }

        if (DnpModelResolver.IsPotentialDnpPrinterText(device.FriendlyName)
            || DnpModelResolver.IsPotentialDnpPrinterText(device.DeviceDescription)
            || DnpModelResolver.IsPotentialDnpPrinterText(device.InstanceId))
        {
            score += 10;
        }

        if (device.InstanceId.StartsWith("USBPRINT\\", StringComparison.OrdinalIgnoreCase))
        {
            score += 10;
        }

        if (device.InstanceId.StartsWith("USB\\VID_", StringComparison.OrdinalIgnoreCase))
        {
            score += 25;
        }

        return score;
    }

    private static bool Contains(string? value, string needle)
        => !string.IsNullOrWhiteSpace(value)
           && value.Contains(needle, StringComparison.OrdinalIgnoreCase);

    private static (string? VendorId, string? ProductId) ParseVidPid(string? usbInstanceId)
    {
        if (string.IsNullOrWhiteSpace(usbInstanceId))
        {
            return (null, null);
        }

        var match = VidPidRegex().Match(usbInstanceId);
        return !match.Success
            ? (null, null)
            : (match.Groups[1].Value.ToUpperInvariant(), match.Groups[2].Value.ToUpperInvariant());
    }

    private static string? ParseSerialFromUsbInstanceId(string? usbInstanceId)
    {
        if (string.IsNullOrWhiteSpace(usbInstanceId))
        {
            return null;
        }

        var parts = usbInstanceId.Split('\\', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length >= 3 ? parts[2] : null;
    }

    private static string? FindUsbParentInstanceId(uint childDevInst)
    {
        const int maxDepth = 8;
        var current = childDevInst;
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

    private static IReadOnlyList<WindowsPnPDeviceInfo> EnumerateDevices()
    {
        const uint flags = NativeMethods.DIGCF_ALLCLASSES | NativeMethods.DIGCF_PRESENT;
        var handle = NativeMethods.SetupDiGetClassDevsW(IntPtr.Zero, null, IntPtr.Zero, flags);
        if (handle == nint.Zero || handle == new nint(-1))
        {
            return Array.Empty<WindowsPnPDeviceInfo>();
        }

        try
        {
            var devices = new List<WindowsPnPDeviceInfo>();
            for (uint index = 0; ; index++)
            {
                var data = new NativeMethods.SP_DEVINFO_DATA();
                data.cbSize = (uint)Marshal.SizeOf<NativeMethods.SP_DEVINFO_DATA>();

                if (!NativeMethods.SetupDiEnumDeviceInfo(handle, index, ref data))
                {
                    var error = Marshal.GetLastWin32Error();
                    if (error == NativeMethods.ERROR_NO_MORE_ITEMS)
                    {
                        break;
                    }

                    throw new Win32Exception(error);
                }

                var instanceId = GetDeviceInstanceId(handle, ref data);
                var className = GetProperty(handle, ref data, NativeMethods.SPDRP_CLASS);
                var friendlyName = GetProperty(handle, ref data, NativeMethods.SPDRP_FRIENDLYNAME);
                var deviceDescription = GetProperty(handle, ref data, NativeMethods.SPDRP_DEVICEDESC);
                devices.Add(new WindowsPnPDeviceInfo(instanceId, className, friendlyName, deviceDescription, data.DevInst));
            }

            return devices;
        }
        finally
        {
            NativeMethods.SetupDiDestroyDeviceInfoList(handle);
        }
    }

    private static string GetDeviceInstanceId(nint handle, ref NativeMethods.SP_DEVINFO_DATA data)
    {
        var buffer = new StringBuilder(512);
        if (!NativeMethods.SetupDiGetDeviceInstanceIdW(handle, ref data, buffer, buffer.Capacity, out _))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error());
        }

        return buffer.ToString();
    }

    private static string? GetDeviceId(uint devInst)
    {
        var buffer = new StringBuilder(512);
        var result = NativeMethods.CM_Get_Device_IDW(devInst, buffer, buffer.Capacity, 0);
        return result == 0 ? buffer.ToString() : null;
    }

    private static string? GetProperty(nint handle, ref NativeMethods.SP_DEVINFO_DATA data, uint property)
    {
        var buffer = new byte[1024];
        if (!NativeMethods.SetupDiGetDeviceRegistryPropertyW(handle, ref data, property, out _, buffer, (uint)buffer.Length, out var requiredSize))
        {
            var error = Marshal.GetLastWin32Error();
            if (error == NativeMethods.ERROR_INVALID_DATA || error == NativeMethods.ERROR_FILE_NOT_FOUND)
            {
                return null;
            }

            return null;
        }

        if (requiredSize <= 2)
        {
            return null;
        }

        return Encoding.Unicode.GetString(buffer, 0, (int)requiredSize - 2).Trim();
    }

    [GeneratedRegex(@"VID_([0-9A-F]{4})&PID_([0-9A-F]{4})", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex VidPidRegex();

    private sealed record WindowsPnPDeviceInfo(string InstanceId, string? ClassName, string? FriendlyName, string? DeviceDescription, uint DevInst);

    private static class NativeMethods
    {
        public const uint DIGCF_PRESENT = 0x00000002;
        public const uint DIGCF_ALLCLASSES = 0x00000004;
        public const int ERROR_NO_MORE_ITEMS = 259;
        public const int ERROR_INVALID_DATA = 13;
        public const int ERROR_FILE_NOT_FOUND = 2;

        public const uint SPDRP_DEVICEDESC = 0x00000000;
        public const uint SPDRP_CLASS = 0x00000007;
        public const uint SPDRP_FRIENDLYNAME = 0x0000000C;

        [StructLayout(LayoutKind.Sequential)]
        public struct SP_DEVINFO_DATA
        {
            public uint cbSize;
            public Guid ClassGuid;
            public uint DevInst;
            public IntPtr Reserved;
        }

        [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern nint SetupDiGetClassDevsW(IntPtr classGuid, string? enumerator, IntPtr hwndParent, uint flags);

        [DllImport("setupapi.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool SetupDiEnumDeviceInfo(nint deviceInfoSet, uint memberIndex, ref SP_DEVINFO_DATA deviceInfoData);

        [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool SetupDiGetDeviceInstanceIdW(nint deviceInfoSet, ref SP_DEVINFO_DATA deviceInfoData, StringBuilder deviceInstanceId, int deviceInstanceIdSize, out int requiredSize);

        [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool SetupDiGetDeviceRegistryPropertyW(nint deviceInfoSet, ref SP_DEVINFO_DATA deviceInfoData, uint property, out uint propertyRegDataType, byte[] propertyBuffer, uint propertyBufferSize, out uint requiredSize);

        [DllImport("setupapi.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool SetupDiDestroyDeviceInfoList(nint deviceInfoSet);

        [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode)]
        public static extern int CM_Get_Device_IDW(uint devInst, StringBuilder buffer, int bufferLen, int flags);

        [DllImport("cfgmgr32.dll")]
        public static extern int CM_Get_Parent(out uint pdnDevInst, uint dnDevInst, int ulFlags);
    }
}
