#nullable enable

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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
    /// Limites: só enxerga assets serializados como texto (o padrão do Unity, Force Text) e
    /// só olha Assets/ e os pacotes embutidos em Packages/.
    /// </summary>
    internal static class SpriteReferenceScanner
    {
        public static readonly string[] Extensions =
        {
            ".prefab", ".unity", ".anim", ".asset", ".controller", ".overrideController", ".playable",
            ".mat", ".spriteatlas", ".spriteatlasv2", ".preset", ".mask", ".signal", ".uxml", ".uss", ".tss"
        };

        private static readonly HashSet<string> ExtensionSet = new HashSet<string>(Extensions, StringComparer.OrdinalIgnoreCase);

        /// <summary>fileID de cada sprite → arquivos (caminhos "Assets/…") que o referenciam.</summary>
        public static Dictionary<long, List<string>> Scan(string projectRoot, string textureGuid, ICollection<long> fileIds, CancellationToken ct = default)
        {
            var found = new Dictionary<long, List<string>>();
            if (fileIds.Count == 0 || textureGuid.Length == 0) return found;

            var wanted = new HashSet<long>(fileIds);
            string guid = Regex.Escape(textureGuid);
            var yaml = new Regex(@"fileID:\s*(-?\d+),\s*guid:\s*" + guid + @"\b", RegexOptions.CultureInvariant);
            var url = new Regex(@"fileID=(-?\d+)&(?:amp;)?guid=" + guid + @"\b", RegexOptions.CultureInvariant);
            var urlGuidFirst = new Regex(@"guid=" + guid + @"&(?:amp;)?fileID=(-?\d+)", RegexOptions.CultureInvariant);

            var hits = new ConcurrentBag<(long Id, string Path)>();
            var options = new ParallelOptions { CancellationToken = ct, MaxDegreeOfParallelism = Math.Max(1, Math.Min(8, Environment.ProcessorCount)) };

            Parallel.ForEach(CandidateFiles(projectRoot), options, file =>
            {
                string content;
                try
                {
                    content = File.ReadAllText(file);
                }
                catch (IOException)
                {
                    return;
                }
                catch (UnauthorizedAccessException)
                {
                    return;
                }

                if (content.IndexOf(textureGuid, StringComparison.OrdinalIgnoreCase) < 0) return;

                var ids = new HashSet<long>();
                foreach (Match m in yaml.Matches(content)) AddId(m, wanted, ids);
                foreach (Match m in url.Matches(content)) AddId(m, wanted, ids);
                foreach (Match m in urlGuidFirst.Matches(content)) AddId(m, wanted, ids);
                if (ids.Count == 0) return;

                string relative = ToProjectPath(projectRoot, file);
                foreach (long id in ids) hits.Add((id, relative));
            });

            foreach (var group in hits.GroupBy(h => h.Id))
                found[group.Key] = group.Select(h => h.Path).Distinct(StringComparer.Ordinal).OrderBy(p => p, StringComparer.Ordinal).ToList();
            return found;
        }

        private static void AddId(Match match, HashSet<long> wanted, HashSet<long> ids)
        {
            if (long.TryParse(match.Groups[1].Value, System.Globalization.NumberStyles.AllowLeadingSign,
                    System.Globalization.CultureInfo.InvariantCulture, out long id) && wanted.Contains(id))
                ids.Add(id);
        }

        private static IEnumerable<string> CandidateFiles(string projectRoot)
        {
            var roots = new List<string> { Path.Combine(projectRoot, "Assets") };
            string packages = Path.Combine(projectRoot, "Packages");
            if (Directory.Exists(packages)) roots.AddRange(Directory.EnumerateDirectories(packages));

            foreach (string root in roots)
            {
                if (!Directory.Exists(root)) continue;
                IEnumerable<string> files;
                try
                {
                    files = Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories);
                }
                catch (IOException)
                {
                    continue;
                }
                catch (UnauthorizedAccessException)
                {
                    continue;
                }

                foreach (string file in files)
                    if (ExtensionSet.Contains(Path.GetExtension(file))) yield return file;
            }
        }

        private static string ToProjectPath(string projectRoot, string file)
        {
            // Os arquivos vêm da enumeração a partir da própria raiz, então o prefixo bate exato.
            string root = projectRoot.TrimEnd('\\', '/') + Path.DirectorySeparatorChar;
            string relative = file.StartsWith(root, StringComparison.Ordinal) ? file.Substring(root.Length) : file;
            return relative.Replace('\\', '/');
        }
    }
}
