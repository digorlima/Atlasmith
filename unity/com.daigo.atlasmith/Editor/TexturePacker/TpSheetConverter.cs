#nullable enable

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Atlasmith.Core;
using UnityEditor;
using UnityEngine;

namespace Atlasmith.Editor
{
    public enum TpConversionStatus
    {
        Converted,

        /// <summary>A verificação achou diferença e tudo voltou ao estado anterior.</summary>
        RolledBack,

        /// <summary>Não converteu (nada foi alterado, ou a volta automática também falhou — veja as mensagens).</summary>
        Failed,

        Skipped
    }

    public sealed class TpConversionResult
    {
        public string TpSheetPath = "";
        public string TexturePath = "";
        public string SheetPath = "";
        public TpConversionStatus Status = TpConversionStatus.Failed;
        public int SpriteCount;
        public string BackupFolder = "";

        /// <summary>O .meta da textura ficou byte a byte igual ao de antes.</summary>
        public bool MetaUnchanged;

        public List<string> Messages = new List<string>();

        /// <summary>Diferenças achadas pela verificação (vazio quando deu certo).</summary>
        public List<string> Differences = new List<string>();

        /// <summary>A volta automática restaurou exatamente o estado anterior.</summary>
        public bool RollbackVerified;
    }

    /// <summary>Um .tpsheet do projeto e se ele pode ser convertido.</summary>
    public sealed class TpSheetCandidate
    {
        public string TpSheetPath = "";
        public string TexturePath = "";
        public int SpriteCount;
        public bool CanConvert;
        public string Problem = "";
    }

    /// <summary>
    /// Converte atlas do TexturePacker (.tpsheet) para o .atlasmith.json sem quebrar
    /// nenhuma referência: a planilha nova sai do que o Unity tem de fato no importer
    /// (não do .tpsheet), os sprites mantêm spriteID e internalID, e o resultado é
    /// conferido sprite por sprite. Qualquer diferença desfaz tudo a partir do backup.
    /// </summary>
    public static class TpSheetConverter
    {
        public const string BackupFolderName = "AtlasmithBackups";

        public static List<TpSheetCandidate> FindCandidates()
        {
            var list = new List<TpSheetCandidate>();
            foreach (string path in AssetDatabase.GetAllAssetPaths())
            {
                if (!path.StartsWith("Assets/", StringComparison.Ordinal) || !TpSheet.IsSheetFile(path)) continue;
                list.Add(Inspect(path));
            }
            return list.OrderBy(c => c.TpSheetPath, StringComparer.OrdinalIgnoreCase).ToList();
        }

        public static TpSheetCandidate Inspect(string tpSheetPath)
        {
            var text = Texts.Current;
            var candidate = new TpSheetCandidate { TpSheetPath = tpSheetPath };

            var tp = TpSheet.TryRead(ProjectPaths.ToFull(tpSheetPath));
            string folder = ProjectPaths.FolderOf(tpSheetPath);
            string textureFile = tp != null && tp.Texture.Length > 0 ? tp.Texture : Path.GetFileNameWithoutExtension(tpSheetPath) + ".png";
            candidate.TexturePath = ProjectPaths.Combine(folder, textureFile);

            if (!(AssetImporter.GetAtPath(candidate.TexturePath) is TextureImporter importer))
            {
                candidate.Problem = text.ConvertProblemNoTexture(candidate.TexturePath);
                return candidate;
            }
            if (importer.textureType != TextureImporterType.Sprite || importer.spriteImportMode != SpriteImportMode.Multiple)
            {
                candidate.Problem = text.ConvertProblemNotSprites;
                return candidate;
            }

            string sheetPath = AtlasmithImporter.SheetPathFor(candidate.TexturePath);
            if (File.Exists(ProjectPaths.ToFull(sheetPath)))
            {
                candidate.Problem = text.ConvertProblemAlreadyHasSheet(Path.GetFileName(sheetPath));
                return candidate;
            }

            var so = new SerializedObject(importer);
            candidate.SpriteCount = so.FindProperty("m_SpriteSheet.m_Sprites")?.arraySize ?? 0;
            if (candidate.SpriteCount == 0)
            {
                candidate.Problem = text.ConvertProblemNotSprites;
                return candidate;
            }

            candidate.CanConvert = true;
            return candidate;
        }

        /// <summary>Uma pasta de backup nova: AtlasmithBackups/aaaaMMdd-HHmmss na raiz do projeto.</summary>
        public static string NewBackupFolder()
        {
            string root = Path.Combine(ProjectPaths.ProjectRoot, BackupFolderName);
            string stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
            string folder = Path.Combine(root, stamp);
            for (int i = 2; Directory.Exists(folder); i++) folder = Path.Combine(root, stamp + "-" + i.ToString(CultureInfo.InvariantCulture));
            return folder;
        }

        /// <summary>Converte vários de uma vez, com um só backup. Um atlas que falha não impede os outros.</summary>
        public static List<TpConversionResult> ConvertAll(IEnumerable<string> tpSheetPaths, Action<int, int, string>? progress = null)
        {
            var paths = tpSheetPaths.ToList();
            string backup = NewBackupFolder();
            var results = new List<TpConversionResult>();
            for (int i = 0; i < paths.Count; i++)
            {
                progress?.Invoke(i, paths.Count, paths[i]);
                results.Add(Convert(paths[i], backup));
            }
            return results;
        }

        public static TpConversionResult Convert(string tpSheetPath, string? backupFolder = null)
        {
            AtlasmithLanguage.Apply();
            var text = Texts.Current;
            var result = new TpConversionResult { TpSheetPath = tpSheetPath };

            var candidate = Inspect(tpSheetPath);
            result.TexturePath = candidate.TexturePath;
            result.SheetPath = AtlasmithImporter.SheetPathFor(candidate.TexturePath);
            if (!candidate.CanConvert)
            {
                result.Status = TpConversionStatus.Skipped;
                result.Messages.Add(candidate.Problem);
                return result;
            }

            string texturePath = candidate.TexturePath;
            string sheetPath = result.SheetPath;

            // 1. O retrato do que o Unity tem agora: é ele que a conversão precisa preservar.
            var before = SpriteImportState.Capture(texturePath);
            result.SpriteCount = before.Sprites.Count;

            AtlasSheet sheet;
            try
            {
                sheet = BuildSheet(before);
            }
            catch (InvalidDataException ex)
            {
                result.Status = TpConversionStatus.Skipped;
                result.Messages.Add(ex.Message);
                return result;
            }

            // 2. Backup de tudo o que vai mudar, fora da pasta Assets.
            backupFolder ??= NewBackupFolder();
            result.BackupFolder = backupFolder;
            string settingsPath = TexturePackerSettingsFile.FindAssetPath() ?? "";
            var settings = settingsPath.Length > 0 ? TexturePackerSettingsFile.Load(settingsPath) : null;
            bool listed = settings != null && settings.Lists(tpSheetPath, texturePath);

            var backedUp = new List<string> { texturePath, texturePath + ".meta", tpSheetPath, tpSheetPath + ".meta" };
            if (listed) backedUp.AddRange(new[] { settingsPath, settingsPath + ".meta" });
            try
            {
                foreach (string path in backedUp)
                {
                    bool isSettings = settingsPath.Length > 0 && path.StartsWith(settingsPath, StringComparison.Ordinal);
                    Backup(path, backupFolder, overwrite: !isSettings);
                }
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                result.Status = TpConversionStatus.Failed;
                result.Messages.Add(text.ConvertBackupFailed(ex.Message));
                return result;
            }
            result.Messages.Add(text.ConvertBackedUp(backupFolder));

            byte[] metaBefore = File.ReadAllBytes(ProjectPaths.ToFull(texturePath + ".meta"));

            try
            {
                // 3. Primeiro sai o .tpsheet (o TexturePacker Importer tira a entrada dele
                //    nesse momento); só depois entra o .atlasmith.json e a textura reimporta.
                if (!AssetDatabase.DeleteAsset(tpSheetPath)) throw new IOException(text.ConvertDeleteFailed(tpSheetPath));
                RemoveSettingsEntries(tpSheetPath, texturePath, result);

                // Escrita direta, sem o temporário do AtomicFile: o sufixo ".<guid>.tmp" soma 37
                // caracteres e, em pastas fundas, passa do limite de 260 do Windows. Aqui não
                // faz falta — se a escrita falhar, o rollback devolve tudo do backup.
                File.WriteAllText(ProjectPaths.ToFull(sheetPath), sheet.ToJson(), new System.Text.UTF8Encoding(false));
                AssetDatabase.ImportAsset(sheetPath, ImportAssetOptions.ForceSynchronousImport);
                AssetDatabase.ImportAsset(texturePath, ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport);

                // 4. Confere tudo contra o retrato.
                var after = SpriteImportState.Capture(texturePath);
                result.Differences.AddRange(SpriteImportState.Compare(before, after));
                if (File.Exists(ProjectPaths.ToFull(tpSheetPath))) result.Differences.Add(text.ConvertTpSheetStillThere);
                if (StillListed(tpSheetPath, texturePath)) result.Differences.Add(text.ConvertStillListed);
            }
            catch (Exception ex)
            {
                result.Differences.Add(ex.GetType().Name + ": " + ex.Message);
            }

            if (result.Differences.Count == 0)
            {
                result.Status = TpConversionStatus.Converted;
                result.MetaUnchanged = metaBefore.SequenceEqual(File.ReadAllBytes(ProjectPaths.ToFull(texturePath + ".meta")));
                result.Messages.Add(text.ConvertDone(Path.GetFileName(sheetPath), result.SpriteCount));
                return result;
            }

            // 5. Algo não bateu: tudo volta como estava.
            result.Messages.Add(text.ConvertMismatch(result.Differences.Count));
            Rollback(result, before, backupFolder, listed);
            return result;
        }

        /// <summary>A planilha a partir do que o Unity tem (com y convertido), no tamanho real da textura.</summary>
        internal static AtlasSheet BuildSheet(SpriteImportState state)
        {
            var text = Texts.Current;
            if (state.Width <= 0 || state.Height <= 0) throw new InvalidDataException(text.ConvertProblemNoSize(state.TexturePath));

            var sheet = new AtlasSheet
            {
                Generator = AtlasmithInfo.Generator,
                Texture = Path.GetFileName(state.TexturePath),
                Width = state.Width,
                Height = state.Height
            };

            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var s in state.Sprites)
            {
                if (!names.Add(s.Name)) throw new InvalidDataException(text.ConvertProblemDuplicate(s.Name));

                var rect = SheetCoordinates.FromUnityRect(s.Rect, state.Height);
                var border = SheetCoordinates.FromUnityBorder(s.Border);
                if (rect == null || border == null) throw new InvalidDataException(text.ConvertProblemFractional(s.Name));

                // O pivô efetivo: com um alinhamento pronto (TopLeft, Bottom…), o Unity ignora o campo pivot.
                var pivot = EffectivePivot((SpriteAlignment)s.Alignment, s.Pivot);
                var (px, py) = SheetCoordinates.FromUnityPivot(pivot);

                sheet.Sprites.Add(new AtlasSheetSprite(s.Name, rect.Value)
                {
                    PivotX = px,
                    PivotY = py,
                    BorderLeft = border.Value.Left,
                    BorderTop = border.Value.Top,
                    BorderRight = border.Value.Right,
                    BorderBottom = border.Value.Bottom
                });
            }
            return sheet;
        }

        internal static Vector2 EffectivePivot(SpriteAlignment alignment, Vector2 custom)
        {
            switch (alignment)
            {
                case SpriteAlignment.Center: return new Vector2(0.5f, 0.5f);
                case SpriteAlignment.TopLeft: return new Vector2(0f, 1f);
                case SpriteAlignment.TopCenter: return new Vector2(0.5f, 1f);
                case SpriteAlignment.TopRight: return new Vector2(1f, 1f);
                case SpriteAlignment.LeftCenter: return new Vector2(0f, 0.5f);
                case SpriteAlignment.RightCenter: return new Vector2(1f, 0.5f);
                case SpriteAlignment.BottomLeft: return new Vector2(0f, 0f);
                case SpriteAlignment.BottomCenter: return new Vector2(0.5f, 0f);
                case SpriteAlignment.BottomRight: return new Vector2(1f, 0f);
                default: return custom;
            }
        }

        private static void Backup(string assetPath, string backupFolder, bool overwrite)
        {
            string source = ProjectPaths.ToFull(assetPath);
            if (!File.Exists(source)) return;

            string target = Path.Combine(backupFolder, assetPath.Replace('/', Path.DirectorySeparatorChar));
            // O arquivo de configurações do TexturePacker entra uma vez por lote: a cópia é o estado antes do lote.
            if (!overwrite && File.Exists(target)) return;

            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(source, target, overwrite: true);
        }

        private static void Restore(string assetPath, string backupFolder)
        {
            string source = Path.Combine(backupFolder, assetPath.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(source)) return;
            string target = ProjectPaths.ToFull(assetPath);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(source, target, overwrite: true);
        }

        /// <summary>Tira do arquivo do TexturePacker Importer o que ele próprio não tirou.</summary>
        private static void RemoveSettingsEntries(string tpSheetPath, string texturePath, TpConversionResult result)
        {
            string? settingsPath = TexturePackerSettingsFile.FindAssetPath();
            if (settingsPath == null) return;

            var settings = TexturePackerSettingsFile.Load(settingsPath);
            if (settings == null || !settings.Lists(tpSheetPath, texturePath)) return;

            settings.Remove(tpSheetPath, texturePath);
            settings.Save();
            result.Messages.Add(Texts.Current.ConvertSettingsCleaned(Path.GetFileName(settingsPath)));
        }

        private static bool StillListed(string tpSheetPath, string texturePath)
        {
            string? settingsPath = TexturePackerSettingsFile.FindAssetPath();
            var settings = settingsPath != null ? TexturePackerSettingsFile.Load(settingsPath) : null;
            return settings != null && settings.Lists(tpSheetPath, texturePath);
        }

        private static void Rollback(TpConversionResult result, SpriteImportState before, string backupFolder, bool wasListed)
        {
            var text = Texts.Current;
            result.Status = TpConversionStatus.RolledBack;
            try
            {
                if (File.Exists(ProjectPaths.ToFull(result.SheetPath))) AssetDatabase.DeleteAsset(result.SheetPath);

                foreach (string path in new[] { result.TexturePath, result.TexturePath + ".meta", result.TpSheetPath, result.TpSheetPath + ".meta" })
                    Restore(path, backupFolder);

                AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
                // Reimportar o .tpsheet faz o TexturePacker Importer registrá-lo de novo.
                AssetDatabase.ImportAsset(result.TpSheetPath, ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport);

                if (wasListed && !StillListed(result.TpSheetPath, result.TexturePath))
                {
                    string? settingsPath = TexturePackerSettingsFile.FindAssetPath();
                    var settings = settingsPath != null ? TexturePackerSettingsFile.Load(settingsPath) : null;
                    if (settings != null)
                    {
                        settings.Add(result.TpSheetPath, result.TexturePath);
                        settings.Save();
                    }
                }

                AssetDatabase.ImportAsset(result.TexturePath, ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport);

                var restored = SpriteImportState.Capture(result.TexturePath);
                var left = SpriteImportState.Compare(before, restored);
                result.RollbackVerified = left.Count == 0 && File.Exists(ProjectPaths.ToFull(result.TpSheetPath)) &&
                                          !File.Exists(ProjectPaths.ToFull(result.SheetPath));
                result.Messages.Add(result.RollbackVerified ? text.ConvertRolledBack : text.ConvertRollbackIncomplete(backupFolder));
                foreach (string diff in left) result.Messages.Add("  " + diff);
            }
            catch (Exception ex)
            {
                result.Status = TpConversionStatus.Failed;
                result.Messages.Add(text.ConvertRollbackIncomplete(backupFolder));
                result.Messages.Add(ex.GetType().Name + ": " + ex.Message);
            }

            if (!result.RollbackVerified) result.Status = TpConversionStatus.Failed;
        }
    }
}
