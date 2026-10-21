#nullable enable

using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Atlasmith.Editor
{
    /// <summary>Conversão entre caminhos do AssetDatabase ("Assets/…") e caminhos do disco.</summary>
    internal static class ProjectPaths
    {
        /// <summary>A pasta do projeto (a que contém Assets, Library, ProjectSettings).</summary>
        public static string ProjectRoot => Path.GetDirectoryName(Path.GetFullPath(Application.dataPath))!;

        /// <summary>"Assets/UI/menu-atlas.png" → caminho completo no disco (também resolve "Packages/…").</summary>
        public static string ToFull(string assetPath) => Path.GetFullPath(assetPath);

        /// <summary>Caminho completo → "Assets/…", ou null se estiver fora da pasta Assets do projeto.</summary>
        public static string? ToAssetPath(string fullPath)
        {
            string root = ProjectRoot;
            string full = Path.GetFullPath(fullPath);
            if (!full.StartsWith(root + Path.DirectorySeparatorChar, PathComparison)) return null;

            string relative = full.Substring(root.Length + 1).Replace('\\', '/');
            return relative.StartsWith("Assets/", PathComparison) || relative == "Assets" ? relative : null;
        }

        /// <summary>Pasta de um caminho do AssetDatabase, sempre com "/".</summary>
        public static string FolderOf(string assetPath)
        {
            int slash = assetPath.LastIndexOf('/');
            return slash < 0 ? "" : assetPath.Substring(0, slash);
        }

        public static string Combine(string folder, string file) => folder.Length == 0 ? file : folder + "/" + file;

        /// <summary>Windows e macOS não diferenciam maiúsculas nos nomes de arquivo; Linux diferencia.</summary>
        public static StringComparison PathComparison =>
            Application.platform == RuntimePlatform.LinuxEditor ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;

        public static bool SameFile(string a, string b)
        {
            try
            {
                return string.Equals(Path.GetFullPath(a), Path.GetFullPath(b), PathComparison);
            }
            catch (Exception ex) when (ex is ArgumentException || ex is NotSupportedException || ex is PathTooLongException)
            {
                return false;
            }
        }

        public static bool AssetExists(string assetPath) => !string.IsNullOrEmpty(AssetDatabase.AssetPathToGUID(assetPath)) &&
                                                             File.Exists(ToFull(assetPath));
    }

    /// <summary>Dimensões de uma imagem lidas direto do arquivo, sem depender do importer.</summary>
    internal static class ImageInfo
    {
        private static readonly byte[] PngSignature = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };

        /// <summary>Largura e altura do cabeçalho IHDR de um PNG.</summary>
        public static bool TryReadPngSize(string fullPath, out int width, out int height)
        {
            width = height = 0;
            try
            {
                using var stream = File.OpenRead(fullPath);
                var header = new byte[24];
                if (stream.Read(header, 0, header.Length) != header.Length) return false;
                for (int i = 0; i < PngSignature.Length; i++)
                    if (header[i] != PngSignature[i]) return false;

                // Assinatura (8) + tamanho do bloco (4) + "IHDR" (4), depois largura e altura em big-endian.
                if (header[12] != 'I' || header[13] != 'H' || header[14] != 'D' || header[15] != 'R') return false;
                width = (header[16] << 24) | (header[17] << 16) | (header[18] << 8) | header[19];
                height = (header[20] << 24) | (header[21] << 16) | (header[22] << 8) | header[23];
                return width > 0 && height > 0;
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                return false;
            }
        }

        /// <summary>Tamanho original da textura: do PNG quando der, senão o que o importer conhece.</summary>
        public static bool TryGetSourceSize(string assetPath, TextureImporter? importer, out int width, out int height)
        {
            if (TryReadPngSize(ProjectPaths.ToFull(assetPath), out width, out height)) return true;

            width = height = 0;
            if (importer == null) return false;
            importer.GetSourceTextureWidthAndHeight(out width, out height);
            return width > 0 && height > 0;
        }
    }
}
