namespace Dnp.Transport.Windows;

internal static class WindowsUsbDllFilterMap
{
    public static int Resolve(string? vendorId, string? productId)
    {
        if (string.Equals(vendorId, "1452", StringComparison.OrdinalIgnoreCase) &&
            string.Equals(productId, "9201", StringComparison.OrdinalIgnoreCase))
        {
            return 35; // QW410
        }

        return 0;
    }
}
