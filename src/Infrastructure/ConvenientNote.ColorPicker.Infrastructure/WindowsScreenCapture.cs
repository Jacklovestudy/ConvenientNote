using System.ComponentModel;
using System.Runtime.InteropServices;
using ConvenientNote.ColorPicker.Application;

namespace ConvenientNote.ColorPicker.Infrastructure;

public sealed class WindowsScreenCapture : IScreenCapture
{
    public ScreenSnapshot Capture()
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
        // Thread-local scope prevents DPI virtualization of virtual-screen bounds.
        var previousDpi = SetThreadDpiAwarenessContext(new nint(-4));
        nint screen = 0, memory = 0, bitmap = 0, previousBitmap = 0;
        try
        {
            int left = GetSystemMetrics(76), top = GetSystemMetrics(77);
            int width = GetSystemMetrics(78), height = GetSystemMetrics(79);
            var byteCount = (long)width * height * 4;
            if (width <= 0 || height <= 0 || byteCount > 512L * 1024 * 1024)
                throw new InvalidOperationException("屏幕尺寸过大或当前屏幕不可用。");
            screen = GetDC(0);
            if (screen == 0) throw new Win32Exception();
            memory = CreateCompatibleDC(screen);
            if (memory == 0) throw new Win32Exception();
            var info = new BitmapInfo
            {
                Size = 40, Width = width, Height = -height, Planes = 1, BitCount = 32,
                SizeImage = (uint)byteCount
            };
            bitmap = CreateDIBSection(screen, ref info, 0, out var pixels, 0, 0);
            if (bitmap == 0 || pixels == 0) throw new Win32Exception();
            previousBitmap = SelectObject(memory, bitmap);
            if (previousBitmap == 0 || previousBitmap == new nint(-1)) throw new Win32Exception();
            if (!BitBlt(memory, 0, 0, width, height, screen, left, top, 0x40CC0020))
                throw new Win32Exception();
            var bytes = new byte[(int)byteCount];
            Marshal.Copy(pixels, bytes, 0, bytes.Length);
            return new ScreenSnapshot(left, top, width, height, bytes);
        }
        finally
        {
            if (previousBitmap != 0 && previousBitmap != new nint(-1)) SelectObject(memory, previousBitmap);
            if (bitmap != 0) DeleteObject(bitmap);
            if (memory != 0) DeleteDC(memory);
            if (screen != 0) ReleaseDC(0, screen);
            if (previousDpi != 0) SetThreadDpiAwarenessContext(previousDpi);
        }
    }

    public (int X, int Y) GetCursorPosition()
    {
        if (!GetPhysicalCursorPos(out var point)) throw new Win32Exception();
        return (point.X, point.Y);
    }

    [StructLayout(LayoutKind.Sequential)] private struct Point { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)] private struct BitmapInfo
    {
        public uint Size;
        public int Width, Height;
        public ushort Planes, BitCount;
        public uint Compression, SizeImage;
        public int XPelsPerMeter, YPelsPerMeter;
        public uint ClrUsed, ClrImportant, Color;
    }
    [DllImport("user32.dll")] private static extern nint SetThreadDpiAwarenessContext(nint context);
    [DllImport("user32.dll")] private static extern int GetSystemMetrics(int index);
    [DllImport("user32.dll", SetLastError = true)] private static extern nint GetDC(nint window);
    [DllImport("user32.dll")] private static extern int ReleaseDC(nint window, nint dc);
    [DllImport("user32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetPhysicalCursorPos(out Point point);
    [DllImport("gdi32.dll", SetLastError = true)] private static extern nint CreateCompatibleDC(nint dc);
    [DllImport("gdi32.dll", SetLastError = true)] private static extern nint CreateDIBSection(nint dc, ref BitmapInfo info, uint usage, out nint bits, nint section, uint offset);
    [DllImport("gdi32.dll", SetLastError = true)] private static extern nint SelectObject(nint dc, nint value);
    [DllImport("gdi32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool DeleteObject(nint value);
    [DllImport("gdi32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool DeleteDC(nint dc);
    [DllImport("gdi32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool BitBlt(nint target, int x, int y, int width, int height, nint source, int sourceX, int sourceY, uint operation);
}
