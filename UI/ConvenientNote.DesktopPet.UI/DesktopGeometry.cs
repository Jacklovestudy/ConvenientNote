using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace ConvenientNote.DesktopPet.UI;

internal static class DesktopGeometry
{
    public static Point? Cursor(Window window)
    {
        if (!GetCursorPos(out var point)) return null;
        return FromDevice(window).Transform(new Point(point.X, point.Y));
    }

    public static Rect WorkArea(Window window)
    {
        var monitor = MonitorFromWindow(new WindowInteropHelper(window).Handle, 2);
        var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        if (monitor == 0 || !GetMonitorInfo(monitor, ref info)) return SystemParameters.WorkArea;
        var matrix = FromDevice(window);
        return new Rect(matrix.Transform(new Point(info.Work.Left, info.Work.Top)), matrix.Transform(new Point(info.Work.Right, info.Work.Bottom)));
    }

    public static void Clamp(Window window)
    {
        var area = WorkArea(window);
        window.Left = Math.Clamp(window.Left, area.Left, Math.Max(area.Left, area.Right - window.Width));
        window.Top = Math.Clamp(window.Top, area.Top, Math.Max(area.Top, area.Bottom - window.Height));
    }

    private static Matrix FromDevice(Window window) => PresentationSource.FromVisual(window)?.CompositionTarget?.TransformFromDevice ?? Matrix.Identity;
    [StructLayout(LayoutKind.Sequential)] private struct NativePoint { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)] private struct NativeRect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] private struct MonitorInfo { public int Size; public NativeRect Monitor, Work; public uint Flags; }
    [DllImport("user32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetCursorPos(out NativePoint point);
    [DllImport("user32.dll")] private static extern nint MonitorFromWindow(nint window, uint flags);
    [DllImport("user32.dll", CharSet = CharSet.Auto)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetMonitorInfo(nint monitor, ref MonitorInfo info);
}
