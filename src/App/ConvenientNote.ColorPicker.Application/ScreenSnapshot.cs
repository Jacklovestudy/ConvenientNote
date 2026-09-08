using ConvenientNote.ColorPicker.Domain;

namespace ConvenientNote.ColorPicker.Application;

public interface IScreenCapture
{
    ScreenSnapshot Capture();
    (int X, int Y) GetCursorPosition();
}

/// <summary>Frozen virtual desktop in physical pixels. Sampling never reads the picker overlay.</summary>
public sealed class ScreenSnapshot
{
    private readonly byte[] _pixels;
    public int Left { get; }
    public int Top { get; }
    public int Width { get; }
    public int Height { get; }
    public int Stride => checked(Width * 4);

    public ScreenSnapshot(int left, int top, int width, int height, byte[] pixels)
    {
        if (width <= 0 || height <= 0 || pixels.LongLength != (long)width * height * 4)
            throw new ArgumentException("Invalid screenshot dimensions or pixel buffer.");
        Left = left; Top = top; Width = width; Height = height;
        _pixels = (byte[])pixels.Clone();
    }

    public byte[] CopyPixels() => (byte[])_pixels.Clone();

    public ColorValue Sample(int x, int y)
    {
        var localX = (long)x - Left;
        var localY = (long)y - Top;
        if (localX < 0 || localY < 0 || localX >= Width || localY >= Height)
            throw new ArgumentOutOfRangeException(nameof(x), "Cursor is outside the captured desktop.");
        var index = checked((int)((localY * Width + localX) * 4));
        return new ColorValue(_pixels[index + 2], _pixels[index + 1], _pixels[index]);
    }
}
