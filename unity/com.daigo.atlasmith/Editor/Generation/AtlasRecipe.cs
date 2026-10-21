#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Atlasmith.Core;
using UnityEditor;

namespace Atlasmith.Editor
{
    public enum RecipeOrigin
    {
        /// <summary>Nenhuma planilha conhecida: valores padrão.</summary>
        Defaults,

        /// <summary>Uma planilha gerada deste arquivo do Krita (o campo "source" aponta para ele).</summary>
        SheetForThisFile,

        /// <summary>A planilha que já existe no destino, sem origem registrada (por exemplo, convertida do TexturePacker).</summary>
        ExistingSheet
    }

    /// <summary>
    /// Tudo o que define um atlas gerado de um arquivo do Krita: onde ele fica, como se
    /// chama e com que opções é empacotado. A receita mora no próprio .atlasmith.json
    /// (campos "source" e "settings"), então as opções ficam guardadas por arquivo do
    /// Krita e vão junto com o projeto para o controle de versão.
    /// </summary>
    public sealed class AtlasRecipe
    {
        /// <summary>O .kra/.krz, como caminho do AssetDatabase.</summary>
        public string KritaPath { get; set; } = "";

        /// <summary>A pasta do atlas, como caminho do AssetDatabase.</summary>
        public string OutputFolder { get; set; } = "";

        public string AtlasName { get; set; } = "";
        public PackSettings Settings { get; set; } = new PackSettings();
        public bool IgnoreHiddenLayers { get; set; } = true;

        public RecipeOrigin Origin { get; set; }

        /// <summary>A planilha de onde as opções vieram (null quando são as padrão).</summary>
        public string? RecipeSheetPath { get; set; }

        public string PngPath => ProjectPaths.Combine(OutputFolder, AtlasName + ".png");
        public string SheetPath => ProjectPaths.Combine(OutputFolder, AtlasName + AtlasSheet.Extension);
        public string TpSheetPath => ProjectPaths.Combine(OutputFolder, AtlasName + TpSheet.Extension);

        /// <summary>O atlas de destino ainda é do TexturePacker: precisa ser convertido antes.</summary>
        public bool HasTexturePackerSheet => File.Exists(ProjectPaths.ToFull(TpSheetPath));

        public AtlasRecipe Clone()
        {
            var copy = (AtlasRecipe)MemberwiseClone();
            copy.Settings = Settings.Clone();
            return copy;
        }

        /// <summary>
        /// A receita guardada para este arquivo do Krita: primeiro uma planilha na mesma
        /// pasta cuja origem é ele, depois em qualquer lugar do projeto; sem nenhuma, o
        /// atlas vai para a pasta do arquivo com o nome sugerido e as opções padrão.
        /// </summary>
        public static AtlasRecipe For(string kritaPath)
        {
            string kritaFull = ProjectPaths.ToFull(kritaPath);
            string folder = ProjectPaths.FolderOf(kritaPath);

            var local = SheetsIn(folder);
            var found = FindSheetFor(kritaFull, local) ??
                        FindSheetFor(kritaFull, AllSheets().Where(p => !local.Contains(p, StringComparer.Ordinal)));
            if (found != null) return FromSheet(kritaPath, found.Value.Path, found.Value.Sheet, RecipeOrigin.SheetForThisFile);

            string folderFull = ProjectPaths.ToFull(folder);
            string name = AtlasNaming.Suggest(kritaFull, folderFull);

            // A planilha sozinha na pasta pode ser de outro arquivo do Krita: não sobrescreve.
            var existing = AtlasSheet.TryRead(AtlasSheet.PathFor(folderFull, name));
            if (existing?.Source != null && !ProjectPaths.SameFile(existing.Source.Resolve(folderFull), kritaFull))
            {
                // Sem pasta de destino, o Suggest cai no nome derivado do arquivo ("x-project" → "x-atlas").
                name = AtlasNaming.Suggest(kritaFull, "");
                existing = AtlasSheet.TryRead(AtlasSheet.PathFor(folderFull, name));
            }

            var recipe = new AtlasRecipe { KritaPath = kritaPath, OutputFolder = folder, AtlasName = name };
            if (existing != null)
            {
                recipe.Origin = RecipeOrigin.ExistingSheet;
                recipe.RecipeSheetPath = ProjectPaths.Combine(folder, name + AtlasSheet.Extension);
                if (existing.Settings != null) recipe.Settings = existing.Settings.Clone();
                if (existing.Source != null) recipe.IgnoreHiddenLayers = existing.Source.IgnoreHiddenLayers;
            }
            return recipe;
        }

        private static AtlasRecipe FromSheet(string kritaPath, string sheetPath, AtlasSheet sheet, RecipeOrigin origin) =>
            new AtlasRecipe
            {
                KritaPath = kritaPath,
                OutputFolder = ProjectPaths.FolderOf(sheetPath),
                AtlasName = AtlasSheet.AtlasNameOf(sheetPath),
                Settings = sheet.Settings?.Clone() ?? new PackSettings(),
                IgnoreHiddenLayers = sheet.Source?.IgnoreHiddenLayers ?? true,
                Origin = origin,
                RecipeSheetPath = sheetPath
            };

        private static (string Path, AtlasSheet Sheet)? FindSheetFor(string kritaFull, IEnumerable<string> sheetPaths)
        {
            foreach (string path in sheetPaths)
            {
                var sheet = AtlasSheet.TryRead(ProjectPaths.ToFull(path));
                if (sheet?.Source == null || sheet.Source.Kind != AtlasSourceKind.Krita || sheet.Source.Path.Length == 0) continue;

                string sheetFolder = ProjectPaths.ToFull(ProjectPaths.FolderOf(path));
                if (ProjectPaths.SameFile(sheet.Source.Resolve(sheetFolder), kritaFull)) return (path, sheet);
            }
            return null;
        }

        private static List<string> SheetsIn(string folder)
        {
            var list = new List<string>();
            string full = ProjectPaths.ToFull(folder);
            if (!Directory.Exists(full)) return list;
            foreach (string file in Directory.EnumerateFiles(full, "*" + AtlasSheet.Extension))
                list.Add(ProjectPaths.Combine(folder, Path.GetFileName(file)));
            list.Sort(StringComparer.Ordinal);
            return list;
        }

        private static IEnumerable<string> AllSheets() =>
            AssetDatabase.GetAllAssetPaths()
                .Where(p => p.StartsWith("Assets/", StringComparison.Ordinal) && AtlasSheet.IsSheetFile(p))
                .OrderBy(p => p, StringComparer.Ordinal);
    }
}
