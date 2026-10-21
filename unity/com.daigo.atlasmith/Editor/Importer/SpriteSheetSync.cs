#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using Atlasmith.Core;
using UnityEditor;
using UnityEditor.U2D.Sprites;
using UnityEngine;

namespace Atlasmith.Editor
{
    /// <summary>O que uma sincronização mudou nos sprites da textura.</summary>
    internal sealed class SyncReport
    {
        public List<string> Added { get; } = new List<string>();
        public List<string> Removed { get; } = new List<string>();
        public List<string> Moved { get; } = new List<string>();
        public List<string> Duplicates { get; } = new List<string>();
        public List<string> OutOfBounds { get; } = new List<string>();
        public int SpriteCount { get; set; }
    }

    /// <summary>
    /// Aplica uma planilha .atlasmith.json aos sprites de um TextureImporter.
    ///
    /// A identidade de um sprite é o nome. Sprite que já existe na textura mantém o
    /// spriteID e o internalID (o fileID das referências em cenas e prefabs) e também o
    /// pivô, o alinhamento e as bordas — quem manda nesses é o Sprite Editor do Unity.
    /// Da planilha vêm só os retângulos e a lista de sprites. Sprite novo nasce com o
    /// pivô e as bordas da planilha. O par nome/fileID de um sprite removido continua na
    /// tabela da textura (como o Sprite Editor faz), então se ele voltar ganha o mesmo
    /// fileID e as referências antigas voltam a funcionar.
    /// </summary>
    internal static class SpriteSheetSync
    {
        public static SyncReport Apply(TextureImporter importer, AtlasSheet sheet, bool firstImport)
        {
            if (firstImport) ApplyFirstImportDefaults(importer, sheet);

            // Tipo e modo antes de criar o provider: ele guarda os dois na construção.
            if (importer.textureType != TextureImporterType.Sprite) importer.textureType = TextureImporterType.Sprite;
            if (importer.spriteImportMode != SpriteImportMode.Multiple) importer.spriteImportMode = SpriteImportMode.Multiple;

            var provider = CreateProvider(importer);
            var ids = provider.GetDataProvider<ISpriteNameFileIdDataProvider>();

            SpriteRect[] existing = provider.GetSpriteRects() ?? Array.Empty<SpriteRect>();
            List<SpriteNameFileIdPair> oldPairs = ids?.GetNameFileIdPairs()?.ToList() ?? new List<SpriteNameFileIdPair>();

            var plan = Plan(existing, oldPairs, sheet);

            provider.SetSpriteRects(plan.Rects.ToArray());
            ids?.SetNameFileIdPairs(plan.Pairs);
            provider.Apply();

            plan.Report.SpriteCount = plan.Rects.Count;
            return plan.Report;
        }

        internal sealed class SyncPlan
        {
            public List<SpriteRect> Rects { get; } = new List<SpriteRect>();
            public List<SpriteNameFileIdPair> Pairs { get; } = new List<SpriteNameFileIdPair>();
            public SyncReport Report { get; } = new SyncReport();
        }

        /// <summary>Decide os sprites e a tabela nome/fileID finais. Separado do importer para ser testável.</summary>
        internal static SyncPlan Plan(IReadOnlyList<SpriteRect> existing, IReadOnlyList<SpriteNameFileIdPair> oldPairs, AtlasSheet sheet)
        {
            var plan = new SyncPlan();
            var report = plan.Report;

            var existingByName = new Dictionary<string, SpriteRect>(StringComparer.Ordinal);
            foreach (var rect in existing)
                if (rect != null && !existingByName.ContainsKey(rect.name)) existingByName[rect.name] = rect;

            var pairByName = new Dictionary<string, SpriteNameFileIdPair>(StringComparer.Ordinal);
            foreach (var pair in oldPairs)
                if (pair != null && !pairByName.ContainsKey(pair.name)) pairByName[pair.name] = pair;

            // Primeiro os nomes válidos da planilha, sem repetição.
            var wanted = new List<AtlasSheetSprite>();
            var wantedNames = new HashSet<string>(StringComparer.Ordinal);
            foreach (var sprite in sheet.Sprites)
            {
                if (!wantedNames.Add(sprite.Name))
                {
                    report.Duplicates.Add(sprite.Name);
                    continue;
                }
                wanted.Add(sprite);

                var r = sprite.Rect;
                if (r.X < 0 || r.Y < 0 || r.Right > sheet.Width || r.Bottom > sheet.Height) report.OutOfBounds.Add(sprite.Name);
            }

            // IDs que continuam em uso, para um sprite novo nunca herdar o de outro.
            var usedIds = new HashSet<GUID>();
            foreach (var sprite in wanted)
                if (existingByName.TryGetValue(sprite.Name, out var kept)) usedIds.Add(kept.spriteID);

            var finalByName = new Dictionary<string, SpriteRect>(StringComparer.Ordinal);
            foreach (var sprite in wanted)
            {
                Rect unityRect = SheetCoordinates.ToUnityRect(sprite.Rect, sheet.Height);

                if (existingByName.TryGetValue(sprite.Name, out var current))
                {
                    if (current.rect != unityRect)
                    {
                        current.rect = unityRect;
                        report.Moved.Add(sprite.Name);
                    }
                    plan.Rects.Add(current);
                    finalByName[sprite.Name] = current;
                    continue;
                }

                // Um nome que já esteve na textura recupera o fileID antigo: o .meta guarda o par
                // nome/internalID dos removidos, e o provider casa o sprite novo com ele pelo nome
                // (e o par pelo GUID que o próprio provider deu). O spriteID antigo não fica
                // guardado em lugar nenhum, então esse é novo.
                GUID id = pairByName.TryGetValue(sprite.Name, out var oldPair) ? oldPair.GetFileGUID() : default;
                if (id.Empty() || usedIds.Contains(id)) id = GUID.Generate();
                usedIds.Add(id);

                var created = new SpriteRect
                {
                    name = sprite.Name,
                    rect = unityRect,
                    alignment = SheetCoordinates.AlignmentFor(sprite),
                    pivot = SheetCoordinates.ToUnityPivot(sprite.PivotX, sprite.PivotY),
                    border = SheetCoordinates.ToUnityBorder(sprite),
                    spriteID = id
                };
                plan.Rects.Add(created);
                finalByName[sprite.Name] = created;
                report.Added.Add(sprite.Name);
            }

            // Saem os que não estão mais na planilha (e cópias de nome repetido na textura).
            foreach (var rect in existing)
            {
                if (rect == null) continue;
                if (finalByName.TryGetValue(rect.name, out var survivor) && ReferenceEquals(survivor, rect)) continue;
                report.Removed.Add(rect.name);
            }

            // Tabela nome/fileID: mantém a ordem antiga (diffs menores no .meta) e os órfãos.
            var written = new HashSet<string>(StringComparer.Ordinal);
            foreach (var pair in oldPairs)
            {
                if (pair == null || !written.Add(pair.name)) continue;
                plan.Pairs.Add(finalByName.TryGetValue(pair.name, out var rect)
                    ? new SpriteNameFileIdPair(pair.name, rect.spriteID)
                    : pair);
            }
            foreach (var rect in plan.Rects)
                if (written.Add(rect.name)) plan.Pairs.Add(new SpriteNameFileIdPair(rect.name, rect.spriteID));

            return plan;
        }

        public static ISpriteEditorDataProvider CreateProvider(TextureImporter importer)
        {
            var factories = new SpriteDataProviderFactories();
            factories.Init();
            var provider = factories.GetSpriteEditorDataProviderFromObject(importer);
            provider.InitSpriteEditorDataProvider();
            return provider;
        }

        /// <summary>
        /// Padrões só para uma textura que acabou de aparecer (sem .meta). Em uma textura
        /// existente, compressão, filtro, tamanho máximo e o resto ficam como o usuário deixou.
        /// </summary>
        private static void ApplyFirstImportDefaults(TextureImporter importer, AtlasSheet sheet)
        {
            var settings = new TextureImporterSettings();
            importer.ReadTextureSettings(settings);
            settings.ApplyTextureType(TextureImporterType.Sprite);
            settings.spriteMode = (int)SpriteImportMode.Multiple;
            settings.alphaIsTransparency = true;
            settings.mipmapEnabled = false;
            // Como o TexturePacker Importer: malha retangular, previsível para UI.
            settings.spriteMeshType = SpriteMeshType.FullRect;
            importer.SetTextureSettings(settings);

            int needed = MaxTextureSizeFor(Math.Max(sheet.Width, sheet.Height));
            if (importer.maxTextureSize < needed) importer.maxTextureSize = needed;
        }

        /// <summary>A menor potência de 2 (entre 32 e 16384) que comporta o lado.</summary>
        public static int MaxTextureSizeFor(int side)
        {
            int size = 32;
            while (size < side && size < 16384) size *= 2;
            return size;
        }

        /// <summary>
        /// A textura já tem exatamente os sprites e retângulos da planilha? Leitura barata
        /// pelo SerializedObject, para não reimportar o que já está em dia.
        /// </summary>
        public static bool IsInSync(TextureImporter importer, AtlasSheet sheet)
        {
            if (importer.textureType != TextureImporterType.Sprite || importer.spriteImportMode != SpriteImportMode.Multiple)
                return false;

            var so = new SerializedObject(importer);
            var sprites = so.FindProperty("m_SpriteSheet.m_Sprites");
            if (sprites == null) return false;

            var current = new Dictionary<string, Rect>(StringComparer.Ordinal);
            for (int i = 0; i < sprites.arraySize; i++)
            {
                var element = sprites.GetArrayElementAtIndex(i);
                string name = element.FindPropertyRelative("m_Name").stringValue;
                if (current.ContainsKey(name)) return false;
                current[name] = element.FindPropertyRelative("m_Rect").rectValue;
            }

            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var sprite in sheet.Sprites)
            {
                if (!names.Add(sprite.Name)) continue;
                if (!current.TryGetValue(sprite.Name, out var rect)) return false;
                if (rect != SheetCoordinates.ToUnityRect(sprite.Rect, sheet.Height)) return false;
            }
            return names.Count == current.Count;
        }
    }
}
