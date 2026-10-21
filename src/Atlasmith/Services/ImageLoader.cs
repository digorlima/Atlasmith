using System.Collections.Concurrent;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;

using Atlasmith.Core;

namespace Atlasmith.Services;

/// <summary>
/// Varre a pasta de entrada e carrega as imagens em BGRA32.
/// Formatos comuns saem pelo WIC (que já vem no Windows) e TGA tem um decoder próprio,
/// então o executável não depende de nenhuma biblioteca de imagem externa.
/// </summary>
public static class ImageLoader
{
    private static readonly string[] Extensions =
        [".png", ".jpg", ".jpeg", ".bmp", ".gif", ".tif", ".tiff", ".tga"];

    public static bool IsSupported(string path) =>
        Extensions.Contains(Path.GetExtension(path).ToLowerInvariant());

    public static string[] FindImages(string rootFolder) =>
        Directory.EnumerateFiles(rootFolder, "*", SearchOption.AllDirectories)
            .Where(IsSupported)
            .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
            .ToArray();

    /// <summary>
    /// Converte o caminho relativo no nome usado pelo .tpsheet.
    /// "Ally Bar/Ornament Borders m012.png" vira "Ally_Bar-Ornament_Borders_m012".
    /// </summary>
    public static string BuildSpriteName(string rootFolder, string fullPath)
    {
        string relative = Path.GetRelativePath(rootFolder, fullPath);
        relative = Path.ChangeExtension(relative, null) ?? relative;
        return relative
            .Replace(Path.DirectorySeparatorChar, '-')
            .Replace(Path.AltDirectorySeparatorChar, '-')
            .Replace(' ', '_');
    }

    public static LoadReport LoadFolder(
        string rootFolder,
        Action<int, int>? progress = null,
        CancellationToken ct = default)
    {
        var files = FindImages(rootFolder);
        var loaded = new SpriteSource?[files.Length];
        var failures = new ConcurrentBag<string>();
        int done = 0;

        var options = new ParallelOptions
        {
            CancellationToken = ct,
            MaxDegreeOfParallelism = Math.Max(1, Environment.ProcessorCount)
        };

        Parallel.For(0, files.Length, options, i =>
        {
            try
            {
                loaded[i] = Load(files[i], BuildSpriteName(rootFolder, files[i]));
            }
            catch (Exception ex)
            {
                failures.Add($"{Path.GetFileName(files[i])}: {ex.Message}");
            }
            finally
            {
                progress?.Invoke(Interlocked.Increment(ref done), files.Length);
            }
        });

        var sprites = loaded.Where(s => s is not null).Select(s => s!).ToArray();
        return new LoadReport(sprites, failures.ToArray());
    }

    public static SpriteSource Load(string path, string name)
    {
        if (Path.GetExtension(path).Equals(".tga", StringComparison.OrdinalIgnoreCase))
            return TgaDecoder.Decode(path, name);

        using var stream = File.OpenRead(path);
        var decoder = BitmapDecoder.Create(
            stream,
            BitmapCreateOptions.PreservePixelFormat | BitmapCreateOptions.IgnoreColorProfile,
            BitmapCacheOption.OnLoad);

        var frame = decoder.Frames[0];
        BitmapSource source = frame.Format == PixelFormats.Bgra32
            ? frame
            : new FormatConvertedBitmap(frame, PixelFormats.Bgra32, null, 0);

        int w = source.PixelWidth;
        int h = source.PixelHeight;
        if (w <= 0 || h <= 0) throw new InvalidDataException("Imagem sem dimensões válidas.");

        var pixels = new byte[(long)w * h * 4];
        source.CopyPixels(pixels, w * 4, 0);

        return new SpriteSource(name, w, h, pixels, path);
    }
}
