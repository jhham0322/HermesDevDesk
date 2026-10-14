namespace Hermes.Core;

public static class ComputerCaptureGuard
{
    // A blank/black capture is ambiguous (legitimate dark content or blocked capture).
    // Fail closed rather than trying another capture path or claiming DRM support.
    public static bool IsBlackOrBlank(ReadOnlySpan<byte> sample)
    {
        if (sample.IsEmpty) return true;
        foreach (var pixel in sample) if (pixel > 2) return false;
        return true;
    }
}
