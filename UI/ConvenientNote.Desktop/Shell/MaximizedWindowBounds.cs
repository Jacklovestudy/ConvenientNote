using System.Runtime.InteropServices;

namespace ConvenientNote;

[StructLayout(LayoutKind.Sequential)]
internal struct NativeRectangle(int left, int top, int right, int bottom)
{
    public int Left = left;
    public int Top = top;
    public int Right = right;
    public int Bottom = bottom;
}

internal readonly record struct WindowBounds(int X, int Y, int Width, int Height);

internal static class MaximizedWindowBounds
{
    public static WindowBounds Calculate(NativeRectangle monitor, NativeRectangle workArea) => new(
        workArea.Left - monitor.Left,
        workArea.Top - monitor.Top,
        workArea.Right - workArea.Left,
        workArea.Bottom - workArea.Top);
}

internal static class WindowWorkAreaManager
{
    private const uint MonitorDefaultToNearest = 0x00000002;

    public static void Apply(IntPtr windowHandle, IntPtr minMaxInfoPointer)
    {
        var monitorHandle = MonitorFromWindow(windowHandle, MonitorDefaultToNearest);
        if (monitorHandle == IntPtr.Zero)
        {
            return;
        }

        var monitorInfo = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        if (!GetMonitorInfo(monitorHandle, ref monitorInfo))
        {
            return;
        }

        var bounds = MaximizedWindowBounds.Calculate(monitorInfo.Monitor, monitorInfo.WorkArea);
        var minMaxInfo = Marshal.PtrToStructure<MinMaxInfo>(minMaxInfoPointer);
        minMaxInfo.MaxPosition = new NativePoint(bounds.X, bounds.Y);
        minMaxInfo.MaxSize = new NativePoint(bounds.Width, bounds.Height);
        Marshal.StructureToPtr(minMaxInfo, minMaxInfoPointer, false);
    }

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr windowHandle, uint flags);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfo(IntPtr monitorHandle, ref MonitorInfo monitorInfo);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint(int x, int y)
    {
        public int X = x;
        public int Y = y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MinMaxInfo
    {
        public NativePoint Reserved;
        public NativePoint MaxSize;
        public NativePoint MaxPosition;
        public NativePoint MinTrackSize;
        public NativePoint MaxTrackSize;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct MonitorInfo
    {
        public int Size;
        public NativeRectangle Monitor;
        public NativeRectangle WorkArea;
        public uint Flags;
    }
}
