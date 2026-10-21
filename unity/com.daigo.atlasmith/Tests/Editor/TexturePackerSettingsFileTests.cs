using System.Linq;
using NUnit.Framework;

namespace Atlasmith.Editor.Tests
{
    public sealed class TexturePackerSettingsFileTests
    {
        // O layout que o TexturePacker Importer 7.x grava: listas paralelas, CRLF, "- " vazio para "sem normal map".
        private const string Sample =
            "version: 2\r\n" +
            "tpsheetFileNames:\r\n" +
            "- Assets/A/a-atlas.tpsheet\r\n" +
            "- Assets/B/b-atlas.tpsheet\r\n" +
            "textureFileNames:\r\n" +
            "- Assets/A/a-atlas.png\r\n" +
            "- Assets/B/b-atlas.png\r\n" +
            "normalmapFileNames:\r\n" +
            "- Assets/A/a-atlas_n.png\r\n" +
            "- \r\n" +
            "enableDebugOutput: 0\r\n";

        [Test]
        public void ParsesTheParallelLists()
        {
            var file = TexturePackerSettingsFile.Parse(Sample);
            Assert.AreEqual("2", file.Version);
            Assert.AreEqual(2, file.Entries.Count);
            Assert.AreEqual("Assets/B/b-atlas.tpsheet", file.Entries[1].TpSheet);
            Assert.AreEqual("Assets/B/b-atlas.png", file.Entries[1].Texture);
            Assert.AreEqual("Assets/A/a-atlas_n.png", file.Entries[0].NormalMap);
            Assert.AreEqual("", file.Entries[1].NormalMap);
        }

        [Test]
        public void SerializesByteForByte()
        {
            Assert.AreEqual(Sample, TexturePackerSettingsFile.Parse(Sample).Serialize());
        }

        [Test]
        public void KeepsLfFiles()
        {
            string lf = Sample.Replace("\r\n", "\n");
            Assert.AreEqual(lf, TexturePackerSettingsFile.Parse(lf).Serialize());
        }

        [Test]
        public void RemoveKeepsTheListsAligned()
        {
            var file = TexturePackerSettingsFile.Parse(Sample);
            Assert.IsTrue(file.Lists("Assets/A/a-atlas.tpsheet", "Assets/A/a-atlas.png"));
            Assert.AreEqual(1, file.Remove("Assets/A/a-atlas.tpsheet", "Assets/A/a-atlas.png"));
            Assert.IsFalse(file.Lists("Assets/A/a-atlas.tpsheet", "Assets/A/a-atlas.png"));

            var again = TexturePackerSettingsFile.Parse(file.Serialize());
            Assert.AreEqual(1, again.Entries.Count);
            Assert.AreEqual("Assets/B/b-atlas.tpsheet", again.Entries[0].TpSheet);
            Assert.AreEqual("Assets/B/b-atlas.png", again.Entries[0].Texture);
            Assert.AreEqual("", again.Entries[0].NormalMap);
        }

        [Test]
        public void AddDoesNotDuplicate()
        {
            var file = TexturePackerSettingsFile.Parse(Sample);
            file.Add("Assets/B/b-atlas.tpsheet", "Assets/B/b-atlas.png");
            file.Add("Assets/C/c-atlas.tpsheet", "Assets/C/c-atlas.png");
            Assert.AreEqual(3, file.Entries.Count);
            Assert.AreEqual(1, file.Entries.Count(e => e.TpSheet == "Assets/B/b-atlas.tpsheet"));
        }
    }
}
