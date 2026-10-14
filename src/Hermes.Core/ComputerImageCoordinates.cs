namespace Hermes.Core;

public static class ComputerImageCoordinates
{
    public static (int X, int Y) ToClient(double x, double y, int imageWidth, int imageHeight, int clientWidth, int clientHeight)
    {
        if (imageWidth <= 0 || imageHeight <= 0 || clientWidth <= 0 || clientHeight <= 0 ||
            !double.IsFinite(x) || !double.IsFinite(y) || x < 0 || y < 0 || x >= imageWidth || y >= imageHeight)
            throw new IOException("Coordinates are outside the observed client image");
        return (Math.Clamp((int)Math.Round(x * clientWidth / imageWidth), 0, clientWidth-1),
            Math.Clamp((int)Math.Round(y * clientHeight / imageHeight), 0, clientHeight-1));
    }
}
