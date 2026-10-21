using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Atlasmith.Services;

/// <summary>A ponte entre os pixels do núcleo (BGRA32) e o WPF.</summary>
public static class Bitmaps
{
    public static BitmapSource FromBgra(byte[] bgra, int width, int height)
    {
        var bmp = BitmapSource.Create(
            width, height, 96, 96,
            PixelFormats.Bgra32, null,
            bgra, width * 4);
        bmp.Freeze();
        return bmp;
    }
}
