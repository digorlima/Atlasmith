using System.Collections.Generic;
using System.Linq;
using Atlasmith.Core;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Atlasmith.Editor.Tests
{
    public sealed class SpriteSheetSyncPlanTests
    {
        private static AtlasSheet Sheet(int width, int height, params AtlasSheetSprite[] sprites)
        {
            var sheet = new AtlasSheet { Texture = "test-atlas.png", Width = width, Height = height };
            sheet.Sprites.AddRange(sprites);
            return sheet;
        }

        private static AtlasSheetSprite Sprite(string name, int x, int y, int w, int h) => new AtlasSheetSprite(name, new RectI(x, y, w, h));

        private static SpriteRect Existing(string name, Rect rect) => new SpriteRect
        {
            name = name,
            rect = rect,
            spriteID = GUID.Generate(),
            alignment = SpriteAlignment.Center,
            pivot = new Vector2(0.5f, 0.5f)
        };

        private static List<SpriteNameFileIdPair> PairsOf(IEnumerable<SpriteRect> rects) =>
            rects.Select(r => new SpriteNameFileIdPair(r.name, r.spriteID)).ToList();

        [Test]
        public void ExistingSpriteKeepsIdPivotAndBorderAndTakesTheNewRect()
        {
            var a = Existing("menu-a", new Rect(0, 0, 10, 10));
            a.alignment = SpriteAlignment.Custom;
            a.pivot = new Vector2(0.1f, 0.2f);
            a.border = new Vector4(1, 2, 3, 4);
            GUID id = a.spriteID;

            var sheetSprite = Sprite("menu-a", 5, 0, 10, 10);
            sheetSprite.PivotX = 0.9; // a planilha diz outra coisa, mas o que vale é o do Unity
            var plan = SpriteSheetSync.Plan(new[] { a }, PairsOf(new[] { a }), Sheet(64, 32, sheetSprite));

            Assert.AreEqual(1, plan.Rects.Count);
            var kept = plan.Rects[0];
            Assert.AreSame(a, kept);
            Assert.AreEqual(id, kept.spriteID);
            Assert.AreEqual(new Vector2(0.1f, 0.2f), kept.pivot);
            Assert.AreEqual(SpriteAlignment.Custom, kept.alignment);
            Assert.AreEqual(new Vector4(1, 2, 3, 4), kept.border);
            Assert.AreEqual(new Rect(5, 22, 10, 10), kept.rect);
            CollectionAssert.AreEqual(new[] { "menu-a" }, plan.Report.Moved);
            Assert.IsEmpty(plan.Report.Added);
            Assert.IsEmpty(plan.Report.Removed);
        }

        [Test]
        public void NewSpriteTakesPivotAndBorderFromTheSheet()
        {
            var sprite = Sprite("menu-new", 0, 0, 8, 8);
            sprite.PivotX = 0.5;
            sprite.PivotY = 1.0; // embaixo, no sistema y-para-baixo da planilha
            sprite.BorderLeft = 1;
            sprite.BorderTop = 2;
            sprite.BorderRight = 3;
            sprite.BorderBottom = 4;

            var plan = SpriteSheetSync.Plan(new SpriteRect[0], new SpriteNameFileIdPair[0], Sheet(16, 16, sprite));

            var created = plan.Rects.Single();
            Assert.IsFalse(created.spriteID.Empty());
            Assert.AreEqual(SpriteAlignment.Custom, created.alignment);
            Assert.AreEqual(0.5f, created.pivot.x, 1e-6f);
            Assert.AreEqual(0f, created.pivot.y, 1e-6f);
            Assert.AreEqual(new Vector4(1, 4, 3, 2), created.border);
            Assert.AreEqual(new Rect(0, 8, 8, 8), created.rect);
            CollectionAssert.AreEqual(new[] { "menu-new" }, plan.Report.Added);
            Assert.AreEqual(created.spriteID, plan.Pairs.Single(p => p.name == "menu-new").GetFileGUID());
        }

        [Test]
        public void RemovedSpriteStaysInTheTableAndComesBackWithTheSameId()
        {
            var a = Existing("menu-a", new Rect(0, 0, 4, 4));
            var b = Existing("menu-b", new Rect(4, 0, 4, 4));
            GUID bId = b.spriteID;

            var first = SpriteSheetSync.Plan(new[] { a, b }, PairsOf(new[] { a, b }), Sheet(8, 4, Sprite("menu-a", 0, 0, 4, 4)));
            CollectionAssert.AreEqual(new[] { "menu-b" }, first.Report.Removed);
            Assert.AreEqual(1, first.Rects.Count);
            Assert.AreEqual(bId, first.Pairs.Single(p => p.name == "menu-b").GetFileGUID(), "o par órfão continua na tabela");

            var second = SpriteSheetSync.Plan(first.Rects, first.Pairs,
                Sheet(8, 4, Sprite("menu-a", 0, 0, 4, 4), Sprite("menu-b", 4, 0, 4, 4)));
            var back = second.Rects.Single(r => r.name == "menu-b");
            Assert.AreEqual(bId, back.spriteID);
            CollectionAssert.AreEqual(new[] { "menu-b" }, second.Report.Added);
        }

        [Test]
        public void NewSpriteNeverReusesAnIdThatIsStillInUse()
        {
            var a = Existing("menu-a", new Rect(0, 0, 4, 4));
            // Tabela corrompida: "menu-x" aponta para o mesmo ID de "menu-a".
            var pairs = new List<SpriteNameFileIdPair> { new SpriteNameFileIdPair("menu-a", a.spriteID), new SpriteNameFileIdPair("menu-x", a.spriteID) };

            var plan = SpriteSheetSync.Plan(new[] { a }, pairs, Sheet(8, 4, Sprite("menu-a", 0, 0, 4, 4), Sprite("menu-x", 4, 0, 4, 4)));
            var x = plan.Rects.Single(r => r.name == "menu-x");
            Assert.AreNotEqual(a.spriteID, x.spriteID);
        }

        [Test]
        public void PairOrderIsKeptAndNewNamesGoLast()
        {
            var b = Existing("menu-b", new Rect(0, 0, 4, 4));
            var a = Existing("menu-a", new Rect(4, 0, 4, 4));
            var pairs = PairsOf(new[] { b, a });

            var plan = SpriteSheetSync.Plan(new[] { b, a }, pairs,
                Sheet(12, 4, Sprite("menu-a", 4, 0, 4, 4), Sprite("menu-c", 8, 0, 4, 4), Sprite("menu-b", 0, 0, 4, 4)));
            CollectionAssert.AreEqual(new[] { "menu-b", "menu-a", "menu-c" }, plan.Pairs.Select(p => p.name).ToArray());
        }

        [Test]
        public void DuplicateAndOutOfBoundsNamesAreReported()
        {
            var plan = SpriteSheetSync.Plan(new SpriteRect[0], new SpriteNameFileIdPair[0],
                Sheet(8, 8, Sprite("menu-a", 0, 0, 4, 4), Sprite("menu-a", 4, 4, 4, 4), Sprite("menu-big", 4, 4, 8, 8)));
            CollectionAssert.AreEqual(new[] { "menu-a" }, plan.Report.Duplicates);
            CollectionAssert.AreEqual(new[] { "menu-big" }, plan.Report.OutOfBounds);
            Assert.AreEqual(2, plan.Rects.Count);
        }

        [Test]
        public void MaxTextureSizeIsTheNextPowerOfTwo()
        {
            Assert.AreEqual(32, SpriteSheetSync.MaxTextureSizeFor(1));
            Assert.AreEqual(2048, SpriteSheetSync.MaxTextureSizeFor(2048));
            Assert.AreEqual(4096, SpriteSheetSync.MaxTextureSizeFor(2049));
            Assert.AreEqual(16384, SpriteSheetSync.MaxTextureSizeFor(20000));
        }
    }
}
