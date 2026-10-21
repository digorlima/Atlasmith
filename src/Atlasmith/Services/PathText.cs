using System.IO;

using Atlasmith.Core;

namespace Atlasmith.Services;

/// <summary>Comparação e exibição de caminhos digitados ou colados pelo usuário.</summary>
public static class PathText
{
    /// <summary>
    /// Tira espaços e as aspas que o "Copiar como caminho" do Explorer coloca em volta.
    /// </summary>
    public static string Clean(string? path) =>
        string.IsNullOrWhiteSpace(path) ? "" : path.Trim().Trim('"').Trim();

    public static string Normalize(string? path)
    {
        string cleaned = Clean(path);
        if (cleaned.Length == 0) return "";

        try
        {
            cleaned = Path.GetFullPath(cleaned);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            // Caminho inválido: compara o texto como está.
        }

        return cleaned.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }

    public static bool Same(string? a, string? b) =>
        string.Equals(Normalize(a), Normalize(b), StringComparison.OrdinalIgnoreCase);

    /// <summary>"C:\Jogo\UI\Menu\Assets" com 2 segmentos vira "…\Menu\Assets".</summary>
    public static string Tail(string? path, int segments)
    {
        string cleaned = Clean(path);
        if (cleaned.Length == 0) return "";

        string[] parts = cleaned.Split(
            [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
            StringSplitOptions.RemoveEmptyEntries);

        return parts.Length <= segments
            ? string.Join(Path.DirectorySeparatorChar, parts)
            : "…" + Path.DirectorySeparatorChar + string.Join(Path.DirectorySeparatorChar, parts[^segments..]);
    }
}
