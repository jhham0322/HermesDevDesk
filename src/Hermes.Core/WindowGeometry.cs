namespace Hermes.Core;
public sealed record SavedWindowBounds(int Left, int Top, int Width, int Height);
public static class WindowGeometry
{
    public static bool Valid(SavedWindowBounds bounds) => bounds.Width is >= 300 and <= 32000 && bounds.Height is >= 200 and <= 32000 && Math.Abs((long)bounds.Left) <= 1_000_000 && Math.Abs((long)bounds.Top) <= 1_000_000;
    public static SavedWindowBounds Fit(SavedWindowBounds bounds, DesktopPixelRect work)
    {
        if (!Valid(bounds) || work.Width < 1 || work.Height < 1) throw new HermesException("저장한 창 위치가 올바르지 않습니다.");
        var width = Math.Min(bounds.Width, work.Width); var height = Math.Min(bounds.Height, work.Height);
        return new(Math.Clamp(bounds.Left, work.Left, work.Right - width), Math.Clamp(bounds.Top, work.Top, work.Bottom - height), width, height);
    }
}
