#nullable enable

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace Atlasmith.Core
{
    /// <summary>Elemento de XML reduzido ao que o maindoc.xml do Krita usa: nome, atributos e filhos.</summary>
    internal sealed class MiniXmlElement
    {
        public MiniXmlElement(string name) => Name = name;

        public string Name { get; }
        public Dictionary<string, string> Attributes { get; } = new(StringComparer.Ordinal);
        public List<MiniXmlElement> Children { get; } = new List<MiniXmlElement>();

        public string? Attr(string name) => Attributes.TryGetValue(name, out string? value) ? value : null;

        public MiniXmlElement? Child(string name) => Children.FirstOrDefault(c => c.Name == name);

        public IEnumerable<MiniXmlElement> ChildrenNamed(string name) => Children.Where(c => c.Name == name);
    }

    /// <summary>
    /// Leitor mínimo de XML: elementos e atributos, sem texto, namespaces nem DTD.
    ///
    /// Mantém o núcleo sem dependência de System.Xml: o executável publicado é
    /// enxugado à mão (alvo PruneUnusedRuntimeFiles) e o mesmo código roda no Unity.
    /// </summary>
    internal static class MiniXml
    {
        public static MiniXmlElement Parse(string text)
        {
            var document = new MiniXmlElement("#document");
            var open = new Stack<MiniXmlElement>();
            open.Push(document);

            int pos = 0;
            while (pos < text.Length)
            {
                int lt = text.IndexOf('<', pos);
                if (lt < 0) break;
                pos = lt;

                if (StartsWithAt(text, pos, "<!--"))
                    pos = SkipPast(text, pos + 4, "-->");
                else if (StartsWithAt(text, pos, "<![CDATA["))
                    pos = SkipPast(text, pos + 9, "]]>");
                else if (StartsWithAt(text, pos, "<?"))
                    pos = SkipPast(text, pos + 2, "?>");
                else if (StartsWithAt(text, pos, "<!"))
                    pos = SkipDeclaration(text, pos + 2);
                else if (StartsWithAt(text, pos, "</"))
                    pos = ParseEndTag(text, pos + 2, open);
                else
                    pos = ParseStartTag(text, pos + 1, open);
            }

            if (open.Count != 1)
                throw new InvalidDataException(CoreText.Current.XmlUnclosed(open.Peek().Name));

            return document;
        }

        private static int ParseStartTag(string s, int pos, Stack<MiniXmlElement> open)
        {
            int nameStart = pos;
            while (pos < s.Length && !IsSpace(s[pos]) && s[pos] != '>' && s[pos] != '/') pos++;
            if (pos == nameStart) throw Malformed(nameStart);

            var element = new MiniXmlElement(s.Substring(nameStart, pos - nameStart));

            while (true)
            {
                while (pos < s.Length && IsSpace(s[pos])) pos++;
                if (pos >= s.Length) throw Malformed(pos);

                if (s[pos] == '>')
                {
                    open.Peek().Children.Add(element);
                    open.Push(element);
                    return pos + 1;
                }

                if (s[pos] == '/')
                {
                    if (pos + 1 >= s.Length || s[pos + 1] != '>') throw Malformed(pos);
                    open.Peek().Children.Add(element);
                    return pos + 2;
                }

                int attrStart = pos;
                while (pos < s.Length && s[pos] != '=' && !IsSpace(s[pos]) && s[pos] != '>' && s[pos] != '/') pos++;
                string attrName = s.Substring(attrStart, pos - attrStart);

                while (pos < s.Length && IsSpace(s[pos])) pos++;
                if (attrName.Length == 0 || pos >= s.Length || s[pos] != '=') throw Malformed(pos);
                pos++;

                while (pos < s.Length && IsSpace(s[pos])) pos++;
                if (pos >= s.Length || (s[pos] != '"' && s[pos] != '\'')) throw Malformed(pos);

                char quote = s[pos++];
                int valueEnd = s.IndexOf(quote, pos);
                if (valueEnd < 0) throw Malformed(pos);

                element.Attributes[attrName] = DecodeEntities(s, pos, valueEnd);
                pos = valueEnd + 1;
            }
        }

        private static int ParseEndTag(string s, int pos, Stack<MiniXmlElement> open)
        {
            int end = s.IndexOf('>', pos);
            if (end < 0) throw Malformed(pos);

            string name = s.Substring(pos, end - pos).Trim();
            if (open.Count <= 1 || open.Peek().Name != name)
                throw new InvalidDataException(CoreText.Current.XmlUnexpectedClose(name, pos));

            open.Pop();
            return end + 1;
        }

        /// <summary>Pula &lt;!DOCTYPE ...&gt;, inclusive um subconjunto interno entre colchetes.</summary>
        private static int SkipDeclaration(string s, int pos)
        {
            int depth = 0;
            char quote = '\0';

            for (; pos < s.Length; pos++)
            {
                char c = s[pos];
                if (quote != '\0')
                {
                    if (c == quote) quote = '\0';
                    continue;
                }

                if (c is '"' or '\'') quote = c;
                else if (c == '[') depth++;
                else if (c == ']') depth--;
                else if (c == '>' && depth <= 0) return pos + 1;
            }

            throw Malformed(pos);
        }

        private static int SkipPast(string s, int pos, string terminator)
        {
            int end = s.IndexOf(terminator, pos, StringComparison.Ordinal);
            if (end < 0) throw Malformed(pos);
            return end + terminator.Length;
        }

        private static string DecodeEntities(string s, int start, int end)
        {
            int amp = s.IndexOf('&', start, end - start);
            if (amp < 0) return s.Substring(start, end - start);

            var sb = new StringBuilder(end - start);
            sb.Append(s, start, amp - start);
            int pos = amp;

            while (pos < end)
            {
                char c = s[pos];
                int semi = c == '&' ? s.IndexOf(';', pos, end - pos) : -1;
                string? decoded = semi > pos ? DecodeEntity(s.Substring(pos + 1, semi - pos - 1)) : null;

                if (decoded is null)
                {
                    sb.Append(c);
                    pos++;
                }
                else
                {
                    sb.Append(decoded);
                    pos = semi + 1;
                }
            }

            return sb.ToString();
        }

        private static string? DecodeEntity(string entity)
        {
            switch (entity)
            {
                case "lt": return "<";
                case "gt": return ">";
                case "amp": return "&";
                case "quot": return "\"";
                case "apos": return "'";
            }

            if (entity.Length < 2 || entity[0] != '#') return null;

            bool hex = entity[1] is 'x' or 'X';
            string digits = hex ? entity.Substring(2) : entity.Substring(1);
            var style = hex ? NumberStyles.AllowHexSpecifier : NumberStyles.None;

            if (!int.TryParse(digits, style, CultureInfo.InvariantCulture, out int code)) return null;
            if (code is < 0 or > 0x10FFFF or (>= 0xD800 and <= 0xDFFF)) return null;

            return char.ConvertFromUtf32(code);
        }

        private static bool StartsWithAt(string s, int pos, string value) =>
            string.CompareOrdinal(s, pos, value, 0, value.Length) == 0;

        private static bool IsSpace(char c) => c is ' ' or '\t' or '\r' or '\n';

        private static InvalidDataException Malformed(int pos) =>
            new InvalidDataException(CoreText.Current.XmlMalformed(pos));
    }
}
