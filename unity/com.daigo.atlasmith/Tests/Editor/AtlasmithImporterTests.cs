using System.IO;
using System.Linq;
using Atlasmith.Core;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Atlasmith.Editor.Tests
{
    /// <summary>Importação de verdade: PNG + .atlasmith.json numa pasta temporária de Assets/.</summary>
    public sealed class AtlasmithImporterTests
    {
        private const string Folder = "Assets/AtlasmithImporterTests";
        private const string Png = Folder + "/test-atlas.png";
        private const string Json = Folder + "/test-atlas.atlasmith.json";

        [SetUp]
        public void SetUp()
        {
            if (AssetDatabase.IsValidFolder(Folder)) AssetDatabase.DeleteAsset(Folder);
            AssetDatabase.CreateFolder("Assets", "AtlasmithImporterTests");
        }

        [TearDown]
        public void TearDown()
        {
            AssetDatabase.DeleteAsset(Folder);
        }

        private static void WritePng(int width, int height)
        {
            var pixels = new byte[width * height * 4];
            for (int i = 0; i < pixels.Length; i += 4)
            {
                pixels[i] = 40;      // B
                pixels[i + 1] = 120; // G
                pixels[i + 2] = 200; // R
                pixels[i + 3] = 255; // A
            }
            File.WriteAllBytes(ProjectPaths.ToFull(Png), PngWriter.Encode(pixels, width, height));
        }

        private static void WriteSheet(int width, int height, params AtlasSheetSprite[] sprites)
        {
            var sheet = new AtlasSheet { Texture = "test-atlas.png", Width = width, Height = height };
            sheet.Sprites.AddRange(sprites);
            sheet.Save(ProjectPaths.ToFull(Json));
        }

        private static AtlasSheetSprite Sprite(string name, int x, int y, int w, int h) => new AtlasSheetSprite(name, new RectI(x, y, w, h));

        private static void ImportSheet() =>
            AssetDatabase.ImportAsset(Json, ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport);

        [Test]
        public void FirstImportMakesAMultipleSpriteTexture()
        {
            var pivoted = Sprite("menu-b", 16, 0, 16, 8);
            pivoted.PivotY = 1; // embaixo
            pivoted.BorderLeft = 2;
            pivoted.BorderTop = 1;
            WriteSheet(64, 32, Sprite("menu-a", 0, 0, 16, 16), pivoted);
            WritePng(64, 32);
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);

            var importer = (TextureImporter)AssetImporter.GetAtPath(Png);
            Assert.AreEqual(TextureImporterType.Sprite, importer.textureType);
            Assert.AreEqual(SpriteImportMode.Multiple, importer.spriteImportMode);
            Assert.IsFalse(importer.mipmapEnabled);
            Assert.IsTrue(importer.alphaIsTransparency);

            var state = SpriteImportState.Capture(Png);
            Assert.AreEqual(2, state.Sprites.Count);
            Assert.AreEqual(new Rect(0, 16, 16, 16), state.Find("menu-a").Rect);

            var b = state.Find("menu-b");
            Assert.AreEqual(new Rect(16, 24, 16, 8), b.Rect);
            Assert.AreEqual((int)SpriteAlignment.Custom, b.Alignment);
            Assert.AreEqual(0f, b.Pivot.y, 1e-6f);
            Assert.AreEqual(new Vector4(2, 0, 0, 1), b.Border);

            var sprites = AssetDatabase.LoadAllAssetRepresentationsAtPath(Png).OfType<Sprite>().Select(s => s.name).OrderBy(n => n).ToArray();
            CollectionAssert.AreEqual(new[] { "menu-a", "menu-b" }, sprites);
        }

        [Test]
        public void EditingTheSheetKeepsIdsAndUserEdits()
        {
            WriteSheet(64, 32, Sprite("menu-a", 0, 0, 16, 16), Sprite("menu-b", 16, 0, 16, 16));
            WritePng(64, 32);
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            var before = SpriteImportState.Capture(Png);
            var a0 = before.Find("menu-a");
            var b0 = before.Find("menu-b");

            // Pivô e borda mexidos à mão no Sprite Editor ficam.
            var importer = (TextureImporter)AssetImporter.GetAtPath(Png);
            var provider = SpriteSheetSync.CreateProvider(importer);
            var rects = provider.GetSpriteRects();
            var a = rects.First(r => r.name == "menu-a");
            a.alignment = SpriteAlignment.Custom;
            a.pivot = new Vector2(0.25f, 0.75f);
            a.border = new Vector4(1, 2, 3, 4);
            provider.SetSpriteRects(rects);
            provider.Apply();
            importer.SaveAndReimport();

            // Move "menu-a", tira "menu-b", põe "menu-c".
            WriteSheet(64, 32, Sprite("menu-a", 32, 0, 16, 16), Sprite("menu-c", 0, 16, 16, 16));
            ImportSheet();

            var after = SpriteImportState.Capture(Png);
            var a1 = after.Find("menu-a");
            Assert.AreEqual(new Rect(32, 16, 16, 16), a1.Rect);
            Assert.AreEqual(a0.SpriteId, a1.SpriteId);
            Assert.AreEqual(a0.InternalId, a1.InternalId);
            Assert.AreEqual(new Vector2(0.25f, 0.75f), a1.Pivot);
            Assert.AreEqual(new Vector4(1, 2, 3, 4), a1.Border);
            Assert.IsNull(after.Find("menu-b"));
            Assert.IsNotNull(after.Find("menu-c"));
            Assert.AreNotEqual(b0.InternalId, after.Find("menu-c").InternalId);

            // "menu-b" volta com o mesmo fileID (internalID), que é o que as referências guardam.
            // O spriteID (GUID) de um sprite removido não fica no .meta — só o par nome/fileID —,
            // então esse é novo; o fileID, não.
            WriteSheet(64, 32, Sprite("menu-a", 32, 0, 16, 16), Sprite("menu-c", 0, 16, 16, 16), Sprite("menu-b", 16, 0, 16, 16));
            ImportSheet();
            var again = SpriteImportState.Capture(Png);
            Assert.AreEqual(b0.InternalId, again.Find("menu-b").InternalId);
            Assert.AreEqual(a0.SpriteId, again.Find("menu-a").SpriteId);
            Assert.AreEqual(b0.InternalId, again.ImportedSprites.First(p => p.Value == "menu-b").Key);
        }

        [Test]
        public void SheetForAnotherTextureIsIgnored()
        {
            var sheet = new AtlasSheet { Texture = "other.png", Width = 8, Height = 8 };
            sheet.Sprites.Add(Sprite("menu-a", 0, 0, 8, 8));
            sheet.Save(ProjectPaths.ToFull(Json));
            WritePng(8, 8);
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);

            var importer = (TextureImporter)AssetImporter.GetAtPath(Png);
            Assert.AreNotEqual(SpriteImportMode.Multiple, importer.spriteImportMode);
        }
    }
}
