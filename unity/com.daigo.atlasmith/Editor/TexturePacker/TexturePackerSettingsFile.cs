#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;

namespace Atlasmith.Editor
{
    /// <summary>
    /// O SettingsTexturePackerImporter.txt: a lista de planilhas que o TexturePacker
    /// Importer conhece, em três listas paralelas (tpsheet, textura, normal map).
    ///
    /// O TexturePacker Importer 7.6 tira a entrada sozinho quando o .tpsheet é apagado
    /// pelo Unity. Se uma entrada ficar para trás apontando para um .tpsheet que não
    /// existe, toda importação da textura dispara um FileNotFoundException dentro dele —
    /// por isso a conversão confere o arquivo e remove o que sobrar.
    /// </summary>
    internal sealed class TexturePackerSettingsFile
    {
        public const string FileName = "SettingsTexturePackerImporter.txt";

        public sealed class Entry
        {
            public Entry(string tpSheet, string texture, string normalMap)
            {
                TpSheet = tpSheet;
                Texture = texture;
                NormalMap = normalMap;
            }

            public string TpSheet { get; }
            public string Texture { get; }
            public string NormalMap { get; }
        }

        public string AssetPath { get; private set; } = "";
        public string Version { get; private set; } = "2";
        public string DebugOutput { get; private set; } = "0";
        public List<Entry> Entries { get; } = new List<Entry>();

        private string _newLine = "\r\n";

        /// <summary>O arquivo de configurações do TexturePacker Importer no projeto, se houver.</summary>
        public static string? FindAssetPath()
        {
            string? found = null;
            foreach (string path in AssetDatabase.GetAllAssetPaths())
            {
                if (!path.EndsWith("/" + FileName, StringComparison.Ordinal) || !path.StartsWith("Assets/", StringComparison.Ordinal)) continue;
                // O importer usa o arquivo ao lado da DLL; prefere esse se houver mais de um.
                if (found == null || File.Exists(ProjectPaths.ToFull(ProjectPaths.Combine(ProjectPaths.FolderOf(path), "TexturePackerImporter.dll"))))
                    found = path;
            }
            return found;
        }

        public static TexturePackerSettingsFile? Load(string assetPath)
        {
            string full = ProjectPaths.ToFull(assetPath);
            if (!File.Exists(full)) return null;
            var file = Parse(File.ReadAllText(full, Encoding.UTF8));
            file.AssetPath = assetPath;
            return file;
        }

        public static TexturePackerSettingsFile Parse(string text)
        {
            var file = new TexturePackerSettingsFile { _newLine = text.Contains("\r\n") ? "\r\n" : "\n" };
            var lists = new Dictionary<string, List<string>>(StringComparer.Ordinal)
            {
                { "tpsheetFileNames", new List<string>() },
                { "textureFileNames", new List<string>() },
                { "normalmapFileNames", new List<string>() }
            };

            List<string>? current = null;
            foreach (string rawLine in text.Split('\n'))
            {
                string line = rawLine.TrimEnd('\r');
                if (line.StartsWith("-", StringComparison.Ordinal))
                {
                    current?.Add(line.Substring(1).Trim());
                    continue;
                }

                current = null;
                int colon = line.IndexOf(':');
                if (colon < 0) continue;

                string key = line.Substring(0, colon).Trim();
                string value = line.Substring(colon + 1).Trim();
                if (lists.TryGetValue(key, out var list)) current = list;
                else if (key == "version") file.Version = value;
                else if (key == "enableDebugOutput") file.DebugOutput = value;
            }

            var sheets = lists["tpsheetFileNames"];
            var textures = lists["textureFileNames"];
            var normals = lists["normalmapFileNames"];
            for (int i = 0; i < sheets.Count; i++)
            {
                file.Entries.Add(new Entry(
                    sheets[i],
                    i < textures.Count ? textures[i] : "",
                    i < normals.Count ? normals[i] : ""));
            }
            return file;
        }

        public bool Lists(string tpSheetPath, string texturePath) =>
            Entries.Any(e => Matches(e, tpSheetPath, texturePath));

        /// <summary>Remove as entradas da planilha ou da textura. Devolve quantas saíram.</summary>
        public int Remove(string tpSheetPath, string texturePath) =>
            Entries.RemoveAll(e => Matches(e, tpSheetPath, texturePath));

        public void Add(string tpSheetPath, string texturePath)
        {
            if (!Entries.Any(e => string.Equals(e.TpSheet, tpSheetPath, StringComparison.Ordinal)))
                Entries.Add(new Entry(tpSheetPath, texturePath, ""));
        }

        private static bool Matches(Entry e, string tpSheetPath, string texturePath) =>
            string.Equals(e.TpSheet, tpSheetPath, StringComparison.Ordinal) ||
            string.Equals(e.Texture, texturePath, StringComparison.Ordinal);

        /// <summary>Mesmo layout que o TexturePacker Importer grava (e ele lê).</summary>
        public string Serialize()
        {
            var sb = new StringBuilder();
            void Line(string s) => sb.Append(s).Append(_newLine);

            Line("version: " + Version);
            Line("tpsheetFileNames:");
            foreach (var e in Entries) Line("- " + e.TpSheet);
            Line("textureFileNames:");
            foreach (var e in Entries) Line("- " + e.Texture);
            Line("normalmapFileNames:");
            foreach (var e in Entries) Line("- " + e.NormalMap);
            Line("enableDebugOutput: " + DebugOutput);
            return sb.ToString();
        }

        public void Save()
        {
            Core.AtomicFile.WriteAllText(ProjectPaths.ToFull(AssetPath), Serialize());
            AssetDatabase.ImportAsset(AssetPath, ImportAssetOptions.ForceUpdate);
        }
    }
}
