using System;
using System.IO;
using System.Linq;
using System.Text;
using NUnit.Framework;

namespace Atlasmith.Editor.Tests
{
    public sealed class SpriteReferenceScannerTests
    {
        private const string Guid = "0123456789abcdef0123456789abcdef";
        private const string OtherGuid = "fedcba9876543210fedcba9876543210";
        private string _root = "";

        private string IndexPath => Path.Combine(_root, "Library", "Atlasmith", "SpriteReferences.index");

        [SetUp]
        public void SetUp()
        {
            _root = Path.Combine(Path.GetTempPath(), "AtlasmithScan-" + System.Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path.Combine(_root, "Assets", "UI"));
            Directory.CreateDirectory(Path.Combine(_root, "Packages", "com.example.embedded"));
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(_root)) Directory.Delete(_root, true);
        }

        private string Full(string relative) => Path.Combine(_root, relative);

        private void Write(string relative, string content) => File.WriteAllText(Full(relative), content);

        private static string Ref(long fileId, string guid = Guid) => $"  m_Sprite: {{fileID: {fileId}, guid: {guid}, type: 3}}\n";

        [Test]
        public void FindsYamlAndUxmlReferences()
        {
            Write("Assets/UI/a.prefab", $"  m_Sprite: {{fileID: 12345, guid: {Guid}, type: 3}}\n  m_Other: {{fileID: 999, guid: {OtherGuid}, type: 3}}\n");
            Write("Assets/UI/b.uxml", $"<ui:VisualElement style=\"background-image: url(&apos;project://database/Assets/x.png?fileID=-777&amp;guid={Guid}&amp;type=3#x&apos;);\" />");
            Write("Assets/UI/c.asset", $"  icon: {{fileID: 999, guid: {OtherGuid}, type: 3}}\n");
            Write("Assets/UI/d.txt", $"fileID: 12345, guid: {Guid}");
            Write("Packages/com.example.embedded/e.prefab", $"  m_Sprite: {{fileID: 12345, guid: {Guid}, type: 3}}\n");

            var found = SpriteReferenceScanner.Scan(_root, Guid, new long[] { 12345, -777, 999 });

            CollectionAssert.AreEquivalent(new[] { 12345L, -777L }, found.Keys);
            CollectionAssert.AreEqual(new[] { "Assets/UI/a.prefab", "Packages/com.example.embedded/e.prefab" }, found[12345]);
            CollectionAssert.AreEqual(new[] { "Assets/UI/b.uxml" }, found[-777]);
        }

        [Test]
        public void NothingToLookForReturnsEmpty()
        {
            Write("Assets/UI/a.prefab", Ref(12345));
            Assert.IsEmpty(SpriteReferenceScanner.Scan(_root, Guid, Array.Empty<long>()));
            Assert.IsEmpty(SpriteReferenceScanner.Scan(_root, "", new long[] { 12345 }));
        }

        [Test]
        public void IndexAnswersForUnchangedFilesAndRereadsChangedOnes()
        {
            Write("Assets/UI/a.prefab", Ref(12345));
            Write("Assets/UI/b.prefab", Ref(999, OtherGuid));
            var ids = new long[] { 12345, 54321, 777 };

            var first = SpriteReferenceScanner.Scan(_root, Guid, ids, default, IndexPath);
            Assert.IsTrue(File.Exists(IndexPath));
            CollectionAssert.AreEqual(new[] { "Assets/UI/a.prefab" }, first[12345]);

            // Mesmo tamanho e mesma data: o arquivo não é relido, quem responde é o índice.
            var stamp = File.GetLastWriteTimeUtc(Full("Assets/UI/a.prefab"));
            Write("Assets/UI/a.prefab", Ref(54321));
            File.SetLastWriteTimeUtc(Full("Assets/UI/a.prefab"), stamp);
            var cached = SpriteReferenceScanner.Scan(_root, Guid, ids, default, IndexPath);
            CollectionAssert.AreEquivalent(new[] { 12345L }, cached.Keys);

            // Outra data: relido.
            File.SetLastWriteTimeUtc(Full("Assets/UI/a.prefab"), stamp.AddSeconds(2));
            var touched = SpriteReferenceScanner.Scan(_root, Guid, ids, default, IndexPath);
            CollectionAssert.AreEquivalent(new[] { 54321L }, touched.Keys);

            // Outro tamanho: relido; um arquivo que não citava a textura passa a citar.
            Write("Assets/UI/b.prefab", Ref(777) + Ref(999, OtherGuid));
            var grown = SpriteReferenceScanner.Scan(_root, Guid, ids, default, IndexPath);
            CollectionAssert.AreEqual(new[] { "Assets/UI/b.prefab" }, grown[777]);

            // Apagado: some da resposta.
            File.Delete(Full("Assets/UI/b.prefab"));
            var deleted = SpriteReferenceScanner.Scan(_root, Guid, ids, default, IndexPath);
            CollectionAssert.AreEquivalent(new[] { 54321L }, deleted.Keys);
        }

        [Test]
        public void BrokenIndexIsRebuilt()
        {
            Write("Assets/UI/a.prefab", Ref(12345));
            Directory.CreateDirectory(Path.GetDirectoryName(IndexPath));
            // Cabeçalho certo ("AMRI", versão 1) e o resto cortado.
            File.WriteAllBytes(IndexPath, new byte[] { 0x41, 0x4D, 0x52, 0x49, 1, 0, 0, 0, 0xFF, 0xFF });

            var found = SpriteReferenceScanner.Scan(_root, Guid, new long[] { 12345 }, default, IndexPath);

            CollectionAssert.AreEqual(new[] { "Assets/UI/a.prefab" }, found[12345]);
            Assert.Greater(new FileInfo(IndexPath).Length, 10);
            CollectionAssert.AreEqual(found[12345], SpriteReferenceScanner.Scan(_root, Guid, new long[] { 12345 }, default, IndexPath)[12345]);
        }

        [Test]
        public void SkipsBinaryAssetsAndWhatUnityIgnores()
        {
            // Um asset binário guarda os GUIDs em bytes; um texto parecido dentro dele não é referência.
            File.WriteAllBytes(Full("Assets/UI/LightingData.asset"), new byte[] { 0, 0, 0, 1 }.Concat(Encoding.UTF8.GetBytes(Ref(12345))).ToArray());
            Directory.CreateDirectory(Full("Assets/Samples~"));
            Write("Assets/Samples~/sample.prefab", Ref(12345));
            Directory.CreateDirectory(Full("Assets/.backup"));
            Write("Assets/.backup/old.prefab", Ref(12345));
            Write("Assets/UI/kept.prefab", Ref(12345));

            var found = SpriteReferenceScanner.Scan(_root, Guid, new long[] { 12345 });

            CollectionAssert.AreEqual(new[] { "Assets/UI/kept.prefab" }, found[12345]);
        }

        [Test]
        public void ReadsUtf16Text()
        {
            File.WriteAllText(Full("Assets/UI/c.uxml"),
                $"<ui:VisualElement style=\"background-image: url(&apos;project://database/Assets/x.png?fileID=-777&amp;guid={Guid}&amp;type=3#x&apos;);\" />",
                Encoding.Unicode);

            var found = SpriteReferenceScanner.Scan(_root, Guid, new long[] { -777 }, default, IndexPath);

            CollectionAssert.AreEqual(new[] { "Assets/UI/c.uxml" }, found[-777]);
        }

        [Test]
        public void ReportsProgressUpToEveryFile()
        {
            Write("Assets/UI/a.prefab", Ref(12345));
            Write("Assets/UI/b.anim", Ref(1));
            Write("Packages/com.example.embedded/c.asset", Ref(2));
            Write("Assets/UI/d.png", "não é candidato");
            int highest = 0, total = -1;
            var sync = new object();

            SpriteReferenceScanner.Scan(_root, Guid, new long[] { 12345 }, default, null, (done, all) =>
            {
                lock (sync)
                {
                    highest = Math.Max(highest, done);
                    total = all;
                }
            });

            Assert.AreEqual(3, total);
            Assert.AreEqual(3, highest);
        }

        [Test]
        public void GuidCollectorFindsGuidsSplitAcrossReads()
        {
            string text = $"a: {{fileID: 1, guid: {Guid}, type: 3}}\n" +
                          $"b: url(?fileID=2&amp;guid={OtherGuid}&amp;type=3)\n" +
                          $"ggguid:\t\r\n {Guid.ToUpperInvariant()}\n" +
                          "guid: 0123 incompleto\nguid=\nguid";
            byte[] bytes = Encoding.UTF8.GetBytes(text);

            var whole = new GuidCollector();
            whole.Feed(bytes);
            var split = new GuidCollector();
            foreach (byte b in bytes) split.Feed(new[] { b });

            foreach (var collector in new[] { whole, split })
            {
                Assert.AreEqual(2, collector.Count);
                Assert.IsTrue(collector.Contains(Guid));
                Assert.IsTrue(collector.Contains(OtherGuid));
            }
        }
    }
}
