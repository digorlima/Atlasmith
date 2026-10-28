#nullable enable

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace Atlasmith.Editor
{
    /// <summary>
    /// Procura, nos arquivos de texto do projeto, quem aponta para certos sprites de uma
    /// textura. Uma referência a sprite é o par (fileID, guid): "{fileID: 348250710, guid:
    /// 68b8…, type: 3}" no YAML de prefabs, cenas e assets, e "?fileID=…&amp;guid=…" no
    /// UXML/USS. Não usa o AssetDatabase, então roda fora da thread principal.
    ///
    /// Num projeto grande são gigabytes de texto, então a busca guarda um índice com os GUIDs
    /// que cada arquivo cita, junto com o tamanho e a data do arquivo. Da segunda vez em
    /// diante só são lidos os arquivos que mudaram e os que citam a textura procurada.
    ///
    /// Limites: só enxerga assets serializados como texto (o padrão do Unity, Force Text) e
    /// só olha Assets/ e os pacotes embutidos em Packages/. Arquivos binários, como o
    /// LightingData, guardam os GUIDs em bytes e ficam de fora, assim como as pastas que o
    /// Unity ignora (".git", "Samples~").
    /// </summary>
    internal static class SpriteReferenceScanner
    {
        public static readonly string[] Extensions =
        {
            ".prefab", ".unity", ".anim", ".asset", ".controller", ".overrideController", ".playable",
            ".mat", ".spriteatlas", ".spriteatlasv2", ".preset", ".mask", ".signal", ".uxml", ".uss", ".tss"
        };

        private static readonly HashSet<string> ExtensionSet = new HashSet<string>(Extensions, StringComparer.OrdinalIgnoreCase);

        // Lido de cada vez. Um byte zero logo no começo marca o arquivo como binário, como no git.
        private const int ChunkSize = 1 << 20;
        private const int BinaryProbe = 8000;
        private const int MaxFolderDepth = 100;

        /// <summary>Onde o índice de um projeto fica guardado (Library não vai para o controle de versão).</summary>
        public static string IndexPathFor(string projectRoot) => Path.Combine(projectRoot, "Library", "Atlasmith", "SpriteReferences.index");

        /// <summary>
        /// fileID de cada sprite → arquivos (caminhos "Assets/…") que o referenciam.
        /// <paramref name="indexPath"/> guarda o índice entre uma busca e outra (null: lê tudo,
        /// sem índice); <paramref name="progress"/> recebe (arquivos vistos, total).
        /// </summary>
        public static Dictionary<long, List<string>> Scan(string projectRoot, string textureGuid, ICollection<long> fileIds,
            CancellationToken ct = default, string? indexPath = null, Action<int, int>? progress = null)
        {
            var found = new Dictionary<long, List<string>>();
            // O Unity escreve todo GUID com 32 dígitos hexadecimais; outra coisa nunca é citada.
            if (fileIds.Count == 0 || !GuidCollector.TryParse(textureGuid, out var target)) return found;

            progress?.Invoke(0, 0);
            var files = CandidateFiles(projectRoot);
            progress?.Invoke(0, files.Count);

            var known = indexPath == null ? new Dictionary<string, FileEntry>() : ReferenceIndex.Load(indexPath);
            var entries = new FileEntry?[files.Count];
            var wanted = new HashSet<long>(fileIds);
            var patterns = new ReferencePatterns(textureGuid);
            var hits = new ConcurrentBag<(long Id, string Path)>();
            int done = 0, reused = 0;
            bool changed = false;
            var options = new ParallelOptions { CancellationToken = ct, MaxDegreeOfParallelism = Math.Max(1, Math.Min(8, Environment.ProcessorCount)) };

            try
            {
                Parallel.For(0, files.Count, options, () => new byte[ChunkSize], (i, _, buffer) =>
                {
                    var file = files[i];
                    if (known.TryGetValue(file.ProjectPath, out var entry) && entry.Matches(file))
                    {
                        Interlocked.Increment(ref reused);
                    }
                    else
                    {
                        entry = Index(file, buffer, ct);
                        if (entry != null) changed = true;
                    }
                    entries[i] = entry;

                    if (entry != null && entry.Cites(target))
                    {
                        long[]? ids = entry.IdsFor(textureGuid);
                        if (ids == null)
                        {
                            ids = Resolve(file.FullPath, patterns);
                            if (ids != null)
                            {
                                entry.Remember(textureGuid, ids);
                                changed = true;
                            }
                        }
                        if (ids != null)
                            foreach (long id in ids)
                                if (wanted.Contains(id)) hits.Add((id, file.ProjectPath));
                    }

                    progress?.Invoke(Interlocked.Increment(ref done), files.Count);
                    return buffer;
                }, _ => { });
            }
            finally
            {
                // Cancelada no meio, a busca ainda guarda o que já leu.
                if (indexPath != null && (changed || reused != known.Count)) ReferenceIndex.Save(indexPath, files, entries, known);
            }

            foreach (var group in hits.GroupBy(h => h.Id))
                found[group.Key] = group.Select(h => h.Path).Distinct(StringComparer.Ordinal).OrderBy(p => p, StringComparer.Ordinal).ToList();
            return found;
        }

        /// <summary>Lê o arquivo em pedaços e anota os GUIDs que ele cita. Null se não deu para ler (fica para a próxima busca).</summary>
        private static FileEntry? Index(CandidateFile file, byte[] buffer, CancellationToken ct)
        {
            try
            {
                using var stream = new FileStream(file.FullPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 4096, FileOptions.SequentialScan);
                var guids = new GuidCollector();
                int count = Fill(stream, buffer);
                if (StartsWithUtf16Or32Bom(buffer, count))
                {
                    // Raro, mas é texto: convertido para UTF-8, segue o mesmo caminho.
                    guids.Feed(Encoding.UTF8.GetBytes(ReadText(file.FullPath)));
                }
                else if (new ReadOnlySpan<byte>(buffer, 0, Math.Min(count, BinaryProbe)).IndexOf((byte)0) < 0)
                {
                    while (count > 0)
                    {
                        ct.ThrowIfCancellationRequested();
                        guids.Feed(new ReadOnlySpan<byte>(buffer, 0, count));
                        count = Fill(stream, buffer);
                    }
                }
                return new FileEntry(file.ProjectPath, file.Length, file.Ticks, guids.ToArray());
            }
            catch (IOException)
            {
                return null;
            }
            catch (UnauthorizedAccessException)
            {
                return null;
            }
        }

        /// <summary>Os fileIDs que o arquivo cita da textura, pelas regras exatas. O índice só diz quem vale a pena reler.</summary>
        private static long[]? Resolve(string fullPath, ReferencePatterns patterns)
        {
            string content;
            try
            {
                content = ReadText(fullPath);
            }
            catch (IOException)
            {
                return null;
            }
            catch (UnauthorizedAccessException)
            {
                return null;
            }

            var ids = new SortedSet<long>();
            patterns.Collect(content, ids);
            return ids.ToArray();
        }

        private static int Fill(Stream stream, byte[] buffer)
        {
            int total = 0, read;
            while (total < buffer.Length && (read = stream.Read(buffer, total, buffer.Length - total)) > 0) total += read;
            return total;
        }

        private static bool StartsWithUtf16Or32Bom(byte[] data, int count) =>
            (count >= 2 && ((data[0] == 0xFF && data[1] == 0xFE) || (data[0] == 0xFE && data[1] == 0xFF))) ||
            (count >= 4 && data[0] == 0 && data[1] == 0 && data[2] == 0xFE && data[3] == 0xFF);

        // Como o File.ReadAllText (UTF-8, ou o que o BOM disser), mas sem brigar com quem estiver gravando o arquivo.
        private static string ReadText(string fullPath)
        {
            using var stream = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(stream, Encoding.UTF8, true);
            return reader.ReadToEnd();
        }

        private static List<CandidateFile> CandidateFiles(string projectRoot)
        {
            var files = new List<CandidateFile>();
            Collect(projectRoot, new DirectoryInfo(Path.Combine(projectRoot, "Assets")), files, 0);
            foreach (var package in Children(new DirectoryInfo(Path.Combine(projectRoot, "Packages"))).OfType<DirectoryInfo>())
                Collect(projectRoot, package, files, 0);
            return files;
        }

        private static void Collect(string projectRoot, DirectoryInfo folder, List<CandidateFile> files, int depth)
        {
            if (depth > MaxFolderDepth) return;
            foreach (var item in Children(folder))
            {
                if (item is DirectoryInfo sub)
                {
                    Collect(projectRoot, sub, files, depth + 1);
                }
                else if (item is FileInfo file && ExtensionSet.Contains(file.Extension))
                {
                    try
                    {
                        files.Add(new CandidateFile(file.FullName, ToProjectPath(projectRoot, file.FullName), file.Length, file.LastWriteTimeUtc.Ticks));
                    }
                    catch (IOException)
                    {
                        // Sumiu entre a listagem e agora.
                    }
                }
            }
        }

        // O que o Unity enxerga: nomes começando com "." ou terminando com "~" ficam de fora.
        private static IEnumerable<FileSystemInfo> Children(DirectoryInfo folder)
        {
            FileSystemInfo[] items;
            try
            {
                items = folder.Exists ? folder.GetFileSystemInfos() : Array.Empty<FileSystemInfo>();
            }
            catch (IOException)
            {
                return Array.Empty<FileSystemInfo>();
            }
            catch (UnauthorizedAccessException)
            {
                return Array.Empty<FileSystemInfo>();
            }
            return items.Where(i => !i.Name.StartsWith(".", StringComparison.Ordinal) && !i.Name.EndsWith("~", StringComparison.Ordinal));
        }

        private static string ToProjectPath(string projectRoot, string file)
        {
            // Os arquivos vêm da enumeração a partir da própria raiz, então o prefixo bate exato.
            string root = projectRoot.TrimEnd('\\', '/') + Path.DirectorySeparatorChar;
            string relative = file.StartsWith(root, StringComparison.Ordinal) ? file.Substring(root.Length) : file;
            return relative.Replace('\\', '/');
        }

        private sealed class CandidateFile
        {
            public CandidateFile(string fullPath, string projectPath, long length, long ticks)
            {
                FullPath = fullPath;
                ProjectPath = projectPath;
                Length = length;
                Ticks = ticks;
            }

            public string FullPath { get; }
            public string ProjectPath { get; }
            public long Length { get; }
            public long Ticks { get; }
        }

        /// <summary>O que o índice sabe de um arquivo: os GUIDs que ele cita e, das texturas já procuradas, os fileIDs.</summary>
        private sealed class FileEntry
        {
            private Dictionary<string, long[]>? _idsByTexture;

            public FileEntry(string projectPath, long length, long ticks, ulong[] guids)
            {
                ProjectPath = projectPath;
                Length = length;
                Ticks = ticks;
                Guids = guids;
            }

            public string ProjectPath { get; }
            public long Length { get; }

            /// <summary>Data da última gravação, em ticks UTC.</summary>
            public long Ticks { get; }

            /// <summary>Cada GUID ocupa duas posições: a metade alta e a baixa.</summary>
            public ulong[] Guids { get; }

            public IEnumerable<KeyValuePair<string, long[]>> IdsByTexture => _idsByTexture ?? Enumerable.Empty<KeyValuePair<string, long[]>>();

            public bool Matches(CandidateFile file) => file.Length == Length && file.Ticks == Ticks;

            public bool Cites((ulong High, ulong Low) guid)
            {
                for (int i = 0; i + 1 < Guids.Length; i += 2)
                    if (Guids[i] == guid.High && Guids[i + 1] == guid.Low) return true;
                return false;
            }

            public long[]? IdsFor(string textureGuid) => _idsByTexture != null && _idsByTexture.TryGetValue(textureGuid, out var ids) ? ids : null;

            public void Remember(string textureGuid, long[] ids) =>
                (_idsByTexture ??= new Dictionary<string, long[]>(StringComparer.Ordinal))[textureGuid] = ids;
        }

        /// <summary>As expressões de sempre para as referências a uma textura, no YAML e nas URLs do UXML/USS.</summary>
        private sealed class ReferencePatterns
        {
            private readonly Regex _yaml;
            private readonly Regex _url;
            private readonly Regex _urlGuidFirst;

            public ReferencePatterns(string textureGuid)
            {
                string guid = Regex.Escape(textureGuid);
                _yaml = new Regex(@"fileID:\s*(-?\d+),\s*guid:\s*" + guid + @"\b", RegexOptions.CultureInvariant);
                _url = new Regex(@"fileID=(-?\d+)&(?:amp;)?guid=" + guid + @"\b", RegexOptions.CultureInvariant);
                _urlGuidFirst = new Regex(@"guid=" + guid + @"&(?:amp;)?fileID=(-?\d+)", RegexOptions.CultureInvariant);
            }

            public void Collect(string content, ISet<long> ids)
            {
                foreach (Match m in _yaml.Matches(content)) Add(m, ids);
                foreach (Match m in _url.Matches(content)) Add(m, ids);
                foreach (Match m in _urlGuidFirst.Matches(content)) Add(m, ids);
            }

            private static void Add(Match match, ISet<long> ids)
            {
                if (long.TryParse(match.Groups[1].Value, System.Globalization.NumberStyles.AllowLeadingSign,
                        System.Globalization.CultureInfo.InvariantCulture, out long id))
                    ids.Add(id);
            }
        }

        /// <summary>O índice em disco. Se faltar ou estiver estragado, a busca começa do zero.</summary>
        private static class ReferenceIndex
        {
            private const int Magic = 0x49524D41; // "AMRI"
            private const int Version = 1;
            private const int MaxCount = 1_000_000;

            public static Dictionary<string, FileEntry> Load(string path)
            {
                var entries = new Dictionary<string, FileEntry>(StringComparer.Ordinal);
                try
                {
                    if (!File.Exists(path)) return entries;
                    using var reader = new BinaryReader(new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete, 1 << 16), Encoding.UTF8);
                    if (reader.ReadInt32() != Magic || reader.ReadInt32() != Version) return entries;

                    int count = ReadCount(reader);
                    for (int i = 0; i < count; i++)
                    {
                        string projectPath = reader.ReadString();
                        long length = reader.ReadInt64();
                        long ticks = reader.ReadInt64();
                        var guids = new ulong[ReadCount(reader) * 2];
                        for (int g = 0; g < guids.Length; g++) guids[g] = reader.ReadUInt64();

                        var entry = new FileEntry(projectPath, length, ticks, guids);
                        int textures = ReadCount(reader);
                        for (int t = 0; t < textures; t++)
                        {
                            string textureGuid = reader.ReadString();
                            var ids = new long[ReadCount(reader)];
                            for (int k = 0; k < ids.Length; k++) ids[k] = reader.ReadInt64();
                            entry.Remember(textureGuid, ids);
                        }
                        entries[projectPath] = entry;
                    }
                }
                catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is InvalidDataException || ex is FormatException)
                {
                    entries.Clear();
                }
                return entries;
            }

            /// <summary>Grava o que vale agora: o que esta busca leu e, dos arquivos que ela não chegou a ver, o que ainda vale.</summary>
            public static void Save(string path, List<CandidateFile> files, FileEntry?[] entries, Dictionary<string, FileEntry> known)
            {
                var keep = new List<FileEntry>(files.Count);
                for (int i = 0; i < files.Count; i++)
                {
                    var entry = entries[i];
                    if (entry == null && known.TryGetValue(files[i].ProjectPath, out var old) && old.Matches(files[i])) entry = old;
                    if (entry != null) keep.Add(entry);
                }

                string temp = path + "." + Guid.NewGuid().ToString("N").Substring(0, 8) + ".tmp";
                try
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                    using (var writer = new BinaryWriter(new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 16), Encoding.UTF8))
                    {
                        writer.Write(Magic);
                        writer.Write(Version);
                        writer.Write(keep.Count);
                        foreach (var entry in keep)
                        {
                            writer.Write(entry.ProjectPath);
                            writer.Write(entry.Length);
                            writer.Write(entry.Ticks);
                            writer.Write(entry.Guids.Length / 2);
                            foreach (ulong half in entry.Guids) writer.Write(half);

                            var textures = entry.IdsByTexture.ToList();
                            writer.Write(textures.Count);
                            foreach (var pair in textures)
                            {
                                writer.Write(pair.Key);
                                writer.Write(pair.Value.Length);
                                foreach (long id in pair.Value) writer.Write(id);
                            }
                        }
                    }
                    if (File.Exists(path)) File.Replace(temp, path, null);
                    else File.Move(temp, path);
                }
                catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
                {
                    // Fica sem índice desta vez; a busca em si já terminou.
                    try
                    {
                        File.Delete(temp);
                    }
                    catch (Exception cleanup) when (cleanup is IOException || cleanup is UnauthorizedAccessException)
                    {
                    }
                }
            }

            private static int ReadCount(BinaryReader reader)
            {
                int count = reader.ReadInt32();
                if (count < 0 || count > MaxCount) throw new InvalidDataException();
                return count;
            }
        }
    }

    /// <summary>
    /// Junta, de um texto lido em pedaços, os GUIDs escritos depois de "guid:" (com ou sem
    /// espaços) ou de "guid=", que é onde as referências do YAML e das URLs do UXML/USS os
    /// põem. O estado passa de um pedaço para o outro, então um GUID cortado entre duas
    /// leituras também conta. Pega mais do que precisa (qualquer "guid:" serve), nunca menos.
    /// </summary>
    internal sealed class GuidCollector
    {
        private const int Searching = 0, AfterG = 1, AfterGu = 2, AfterGui = 3, AfterGuid = 4, SkippingSpace = 5, ReadingDigits = 6;

        private readonly HashSet<(ulong High, ulong Low)> _guids = new HashSet<(ulong High, ulong Low)>();
        private int _state;
        private int _digits;
        private ulong _high, _low;

        public int Count => _guids.Count;

        public bool Contains(string guid) => TryParse(guid, out var key) && _guids.Contains(key);

        /// <summary>"0123…cdef" (32 dígitos hexadecimais) → as duas metades.</summary>
        public static bool TryParse(string guid, out (ulong High, ulong Low) key)
        {
            key = default;
            if (guid.Length != 32) return false;
            ulong high = 0, low = 0;
            for (int i = 0; i < 32; i++)
            {
                int value = HexValue(guid[i]);
                if (value < 0) return false;
                if (i < 16) high = (high << 4) | (uint)value;
                else low = (low << 4) | (uint)value;
            }
            key = (high, low);
            return true;
        }

        public ulong[] ToArray()
        {
            var halves = new ulong[_guids.Count * 2];
            int i = 0;
            foreach (var guid in _guids)
            {
                halves[i++] = guid.High;
                halves[i++] = guid.Low;
            }
            return halves;
        }

        public void Feed(ReadOnlySpan<byte> data)
        {
            int i = 0;
            while (i < data.Length)
            {
                if (_state == Searching)
                {
                    int next = data.Slice(i).IndexOf((byte)'g');
                    if (next < 0) return;
                    i += next + 1;
                    _state = AfterG;
                    continue;
                }

                byte b = data[i++];
                switch (_state)
                {
                    case AfterG:
                        _state = b == 'u' ? AfterGu : Restart(b);
                        break;
                    case AfterGu:
                        _state = b == 'i' ? AfterGui : Restart(b);
                        break;
                    case AfterGui:
                        _state = b == 'd' ? AfterGuid : Restart(b);
                        break;
                    case AfterGuid:
                        if (b == ':') _state = SkippingSpace;
                        else if (b == '=') StartDigits();
                        else _state = Restart(b);
                        break;
                    case SkippingSpace:
                        if (IsSpace(b)) break;
                        StartDigits();
                        ReadDigit(b);
                        break;
                    default:
                        ReadDigit(b);
                        break;
                }
            }
        }

        // "guid" não repete letras, então quem quebra a sequência só pode recomeçá-la se for um "g".
        private static int Restart(byte b) => b == 'g' ? AfterG : Searching;

        // Os espaços do \s das expressões; bytes acima de 127 entram porque podem ser espaços Unicode em UTF-8.
        private static bool IsSpace(byte b) => b == ' ' || (b >= 9 && b <= 13) || b >= 0x80;

        private void StartDigits()
        {
            _state = ReadingDigits;
            _digits = 0;
            _high = _low = 0;
        }

        private void ReadDigit(byte b)
        {
            int value = HexValue((char)b);
            if (value < 0)
            {
                _state = Restart(b);
                return;
            }
            if (_digits < 16) _high = (_high << 4) | (uint)value;
            else _low = (_low << 4) | (uint)value;
            if (++_digits == 32)
            {
                _guids.Add((_high, _low));
                _state = Searching;
            }
        }

        private static int HexValue(char c)
        {
            if (c >= '0' && c <= '9') return c - '0';
            if (c >= 'a' && c <= 'f') return c - 'a' + 10;
            if (c >= 'A' && c <= 'F') return c - 'A' + 10;
            return -1;
        }
    }
}
