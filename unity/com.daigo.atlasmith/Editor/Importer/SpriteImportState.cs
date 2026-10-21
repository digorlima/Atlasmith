#nullable enable

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Atlasmith.Editor
{
    /// <summary>Um sprite como está gravado no .meta da textura.</summary>
    public sealed class SpriteState
    {
        public string Name = "";
        public Rect Rect;
        public int Alignment;
        public Vector2 Pivot;
        public Vector4 Border;
        public string SpriteId = "";
        public long InternalId;
    }

    /// <summary>
    /// Retrato dos sprites de uma textura: o que está no importer (nome, retângulo,
    /// pivô, alinhamento, bordas, spriteID, internalID e a tabela nome/fileID), os
    /// sprites realmente importados (fileID local → nome, que é o que as referências
    /// em cenas e prefabs usam) e as opções de importação que não podem mudar.
    /// </summary>
    public sealed class SpriteImportState
    {
        public string TexturePath = "";
        public int Width;
        public int Height;
        public List<SpriteState> Sprites = new List<SpriteState>();
        public Dictionary<string, long> NameFileIds = new Dictionary<string, long>(StringComparer.Ordinal);
        public Dictionary<long, string> ImportedSprites = new Dictionary<long, string>();
        public SortedDictionary<string, string> Settings = new SortedDictionary<string, string>(StringComparer.Ordinal);

        public SpriteState? Find(string name) => Sprites.FirstOrDefault(s => s.Name == name);

        public static SpriteImportState Capture(string texturePath)
        {
            var state = new SpriteImportState { TexturePath = texturePath };
            if (!(AssetImporter.GetAtPath(texturePath) is TextureImporter importer)) return state;

            ImageInfo.TryGetSourceSize(texturePath, importer, out state.Width, out state.Height);

            var so = new SerializedObject(importer);
            var sprites = so.FindProperty("m_SpriteSheet.m_Sprites");
            if (sprites != null)
            {
                for (int i = 0; i < sprites.arraySize; i++)
                {
                    var e = sprites.GetArrayElementAtIndex(i);
                    state.Sprites.Add(new SpriteState
                    {
                        Name = e.FindPropertyRelative("m_Name").stringValue,
                        Rect = e.FindPropertyRelative("m_Rect").rectValue,
                        Alignment = e.FindPropertyRelative("m_Alignment").intValue,
                        Pivot = e.FindPropertyRelative("m_Pivot").vector2Value,
                        Border = e.FindPropertyRelative("m_Border").vector4Value,
                        SpriteId = e.FindPropertyRelative("m_SpriteID").stringValue,
                        InternalId = e.FindPropertyRelative("m_InternalID").longValue
                    });
                }
            }

            var table = so.FindProperty("m_SpriteSheet.m_NameFileIdTable");
            if (table != null)
            {
                for (int i = 0; i < table.arraySize; i++)
                {
                    var e = table.GetArrayElementAtIndex(i);
                    string name = e.FindPropertyRelative("first").stringValue;
                    state.NameFileIds[name] = e.FindPropertyRelative("second").longValue;
                }
            }

            foreach (var asset in AssetDatabase.LoadAllAssetRepresentationsAtPath(texturePath))
            {
                if (!(asset is Sprite sprite)) continue;
                if (AssetDatabase.TryGetGUIDAndLocalFileIdentifier(sprite, out string _, out long localId))
                    state.ImportedSprites[localId] = sprite.name;
            }

            var c = CultureInfo.InvariantCulture;
            state.Settings["textureType"] = importer.textureType.ToString();
            state.Settings["spriteImportMode"] = importer.spriteImportMode.ToString();
            state.Settings["maxTextureSize"] = importer.maxTextureSize.ToString(c);
            state.Settings["textureCompression"] = importer.textureCompression.ToString();
            state.Settings["compressionQuality"] = importer.compressionQuality.ToString(c);
            state.Settings["crunchedCompression"] = importer.crunchedCompression.ToString();
            state.Settings["filterMode"] = importer.filterMode.ToString();
            state.Settings["wrapMode"] = importer.wrapMode.ToString();
            state.Settings["anisoLevel"] = importer.anisoLevel.ToString(c);
            state.Settings["mipmapEnabled"] = importer.mipmapEnabled.ToString();
            state.Settings["sRGBTexture"] = importer.sRGBTexture.ToString();
            state.Settings["alphaIsTransparency"] = importer.alphaIsTransparency.ToString();
            state.Settings["alphaSource"] = importer.alphaSource.ToString();
            state.Settings["isReadable"] = importer.isReadable.ToString();
            state.Settings["npotScale"] = importer.npotScale.ToString();
            state.Settings["spritePixelsPerUnit"] = importer.spritePixelsPerUnit.ToString("R", c);

            var textureSettings = new TextureImporterSettings();
            importer.ReadTextureSettings(textureSettings);
            state.Settings["spriteMeshType"] = textureSettings.spriteMeshType.ToString();
            state.Settings["spriteExtrude"] = textureSettings.spriteExtrude.ToString(c);
            state.Settings["spriteGenerateFallbackPhysicsShape"] = textureSettings.spriteGenerateFallbackPhysicsShape.ToString();

            foreach (var platform in new[] { "Standalone", "Android", "iPhone", "WebGL" })
            {
                var p = importer.GetPlatformTextureSettings(platform);
                if (!p.overridden) continue;
                state.Settings["platform." + platform] = $"{p.maxTextureSize}/{p.format}/{p.textureCompression}/{p.compressionQuality}";
            }

            return state;
        }

        /// <summary>As diferenças entre dois retratos, uma por linha. Vazio = idênticos.</summary>
        public static List<string> Compare(SpriteImportState before, SpriteImportState after)
        {
            var diffs = new List<string>();
            var c = CultureInfo.InvariantCulture;

            var a = before.Sprites.GroupBy(s => s.Name).ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
            var b = after.Sprites.GroupBy(s => s.Name).ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

            if (before.Sprites.Count != after.Sprites.Count)
                diffs.Add($"sprite count {before.Sprites.Count} -> {after.Sprites.Count}");

            foreach (var pair in a)
            {
                if (!b.TryGetValue(pair.Key, out var other))
                {
                    diffs.Add($"{pair.Key}: missing after");
                    continue;
                }
                var s = pair.Value;
                if (s.Rect != other.Rect) diffs.Add($"{s.Name}: rect {Fmt(s.Rect)} -> {Fmt(other.Rect)}");
                if (s.Alignment != other.Alignment) diffs.Add($"{s.Name}: alignment {s.Alignment} -> {other.Alignment}");
                if (s.Pivot != other.Pivot)
                    diffs.Add(string.Format(c, "{0}: pivot ({1}, {2}) -> ({3}, {4})", s.Name,
                        s.Pivot.x.ToString("R", c), s.Pivot.y.ToString("R", c), other.Pivot.x.ToString("R", c), other.Pivot.y.ToString("R", c)));
                if (s.Border != other.Border) diffs.Add($"{s.Name}: border {s.Border} -> {other.Border}");
                if (s.SpriteId != other.SpriteId) diffs.Add($"{s.Name}: spriteID {s.SpriteId} -> {other.SpriteId}");
                if (s.InternalId != other.InternalId) diffs.Add($"{s.Name}: internalID {s.InternalId} -> {other.InternalId}");
            }
            foreach (var name in b.Keys)
                if (!a.ContainsKey(name)) diffs.Add($"{name}: new after");

            foreach (var pair in before.NameFileIds)
            {
                if (!after.NameFileIds.TryGetValue(pair.Key, out long id)) diffs.Add($"nameFileIdTable: {pair.Key} missing after");
                else if (id != pair.Value) diffs.Add($"nameFileIdTable: {pair.Key} {pair.Value} -> {id}");
            }
            foreach (var name in after.NameFileIds.Keys)
                if (!before.NameFileIds.ContainsKey(name)) diffs.Add($"nameFileIdTable: {name} new after");

            foreach (var pair in before.ImportedSprites)
            {
                if (!after.ImportedSprites.TryGetValue(pair.Key, out string? name)) diffs.Add($"imported sprite fileID {pair.Key} ({pair.Value}) missing after");
                else if (name != pair.Value) diffs.Add($"imported sprite fileID {pair.Key}: {pair.Value} -> {name}");
            }
            foreach (var id in after.ImportedSprites.Keys)
                if (!before.ImportedSprites.ContainsKey(id)) diffs.Add($"imported sprite fileID {id} ({after.ImportedSprites[id]}) new after");

            foreach (var pair in before.Settings)
            {
                after.Settings.TryGetValue(pair.Key, out string? value);
                if (value != pair.Value) diffs.Add($"setting {pair.Key}: {pair.Value} -> {value ?? "(none)"}");
            }
            foreach (var key in after.Settings.Keys)
                if (!before.Settings.ContainsKey(key)) diffs.Add($"setting {key}: (none) -> {after.Settings[key]}");

            return diffs;
        }

        private static string Fmt(Rect r) => string.Format(CultureInfo.InvariantCulture, "({0},{1} {2}x{3})", r.x, r.y, r.width, r.height);
    }
}
