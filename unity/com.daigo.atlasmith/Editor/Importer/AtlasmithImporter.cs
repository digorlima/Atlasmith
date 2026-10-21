#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using Atlasmith.Core;
using UnityEditor;

namespace Atlasmith.Editor
{
    /// <summary>
    /// Importer das planilhas do Atlasmith: a textura "X.png" com um "X.atlasmith.json"
    /// ao lado (e cujo campo "texture" é "X.png") vira Sprite Multiple com os sprites da
    /// planilha. Substitui o TexturePacker Importer para os atlas convertidos ou gerados
    /// pelo Atlasmith; enquanto houver um "X.tpsheet" ao lado, quem cuida da textura
    /// continua sendo o TexturePacker Importer.
    /// </summary>
    public sealed class AtlasmithImporter : AssetPostprocessor
    {
        /// <summary>
        /// Versão da lógica de importação. Mudar este número reimporta todas as texturas do
        /// projeto, então só deve mudar quando o resultado da importação mudar.
        /// </summary>
        public const uint ImporterVersion = 1;

        public override uint GetVersion() => ImporterVersion;

        // Antes do TexturePacker Importer (ordem 0): se ele falhar com uma textura que
        // ainda aparece nas configurações dele, os sprites do Atlasmith já foram aplicados.
        public override int GetPostprocessOrder() => -100;

        /// <summary>"Assets/UI/menu-atlas.png" → "Assets/UI/menu-atlas.atlasmith.json".</summary>
        public static string SheetPathFor(string texturePath)
        {
            string folder = ProjectPaths.FolderOf(texturePath);
            return ProjectPaths.Combine(folder, Path.GetFileNameWithoutExtension(texturePath) + AtlasSheet.Extension);
        }

        public static string TpSheetPathFor(string texturePath)
        {
            string folder = ProjectPaths.FolderOf(texturePath);
            return ProjectPaths.Combine(folder, Path.GetFileNameWithoutExtension(texturePath) + TpSheet.Extension);
        }

        private void OnPreprocessTexture()
        {
            if (!(assetImporter is TextureImporter importer)) return;

            string sheetPath = SheetPathFor(assetPath);
            string sheetFull = ProjectPaths.ToFull(sheetPath);
            if (!File.Exists(sheetFull)) return;

            // Mudou a planilha, reimporta a textura.
            context.DependsOnSourceAsset(sheetPath);
            AtlasmithLanguage.Apply();
            var text = Texts.Current;

            if (File.Exists(ProjectPaths.ToFull(TpSheetPathFor(assetPath))))
            {
                context.LogImportWarning(text.ImporterTpSheetConflict(assetPath, TpSheetPathFor(assetPath)));
                return;
            }

            AtlasSheet sheet;
            try
            {
                sheet = AtlasSheet.Read(sheetFull);
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is InvalidDataException)
            {
                context.LogImportError(text.ImporterInvalidSheet(sheetPath, ex.Message));
                return;
            }

            string textureFile = Path.GetFileName(assetPath);
            if (!string.Equals(sheet.Texture, textureFile, StringComparison.OrdinalIgnoreCase))
            {
                context.LogImportWarning(text.ImporterTextureMismatch(sheetPath, sheet.Texture, textureFile));
                return;
            }

            if (ImageInfo.TryGetSourceSize(assetPath, importer, out int width, out int height) &&
                (width != sheet.Width || height != sheet.Height))
            {
                context.LogImportWarning(text.ImporterSizeMismatch(assetPath, width, height, sheet.Width, sheet.Height));
            }

            var report = SpriteSheetSync.Apply(importer, sheet, importer.importSettingsMissing);

            if (report.Removed.Count > 0) context.LogImportWarning(text.ImporterSpritesRemoved(assetPath, report.Removed));
            if (report.Duplicates.Count > 0) context.LogImportWarning(text.ImporterDuplicateNames(sheetPath, report.Duplicates));
            if (report.OutOfBounds.Count > 0) context.LogImportWarning(text.ImporterOutOfBounds(sheetPath, report.OutOfBounds));
        }

        /// <summary>
        /// Planilha criada, alterada ou movida depois da textura: a dependência só existe
        /// a partir da primeira importação com a planilha, então reimporta aqui o que
        /// estiver fora de sincronia.
        /// </summary>
        private static void OnPostprocessAllAssets(string[] importedAssets, string[] deletedAssets, string[] movedAssets, string[] movedFromAssetPaths)
        {
            List<string>? reimport = null;
            var imported = new HashSet<string>(importedAssets, StringComparer.Ordinal);

            void Check(string path)
            {
                if (!AtlasSheet.IsSheetFile(path)) return;

                var sheet = AtlasSheet.TryRead(ProjectPaths.ToFull(path));
                if (sheet == null) return;

                string texturePath = ProjectPaths.Combine(ProjectPaths.FolderOf(path), sheet.Texture);
                if (!string.Equals(Path.GetFileNameWithoutExtension(sheet.Texture), AtlasSheet.AtlasNameOf(path), StringComparison.OrdinalIgnoreCase))
                    return;
                if (imported.Contains(texturePath)) return;
                if (File.Exists(ProjectPaths.ToFull(TpSheetPathFor(texturePath)))) return;
                if (!(AssetImporter.GetAtPath(texturePath) is TextureImporter importer)) return;
                if (SpriteSheetSync.IsInSync(importer, sheet)) return;

                (reimport ??= new List<string>()).Add(texturePath);
            }

            foreach (string path in importedAssets) Check(path);
            foreach (string path in movedAssets) Check(path);

            if (reimport == null) return;
            foreach (string texturePath in reimport) AssetDatabase.ImportAsset(texturePath, ImportAssetOptions.ForceUpdate);
        }
    }
}
