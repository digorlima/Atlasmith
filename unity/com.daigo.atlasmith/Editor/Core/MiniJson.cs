#nullable enable

using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace Atlasmith.Core
{
    /// <summary>Objeto JSON que guarda as chaves na ordem em que entraram, para o arquivo gravado ficar estável.</summary>
    public sealed class JsonObject : IEnumerable<KeyValuePair<string, object?>>
    {
        private readonly List<KeyValuePair<string, object?>> _items = new List<KeyValuePair<string, object?>>();

        public int Count => _items.Count;

        /// <summary>Substitui o valor se a chave já existir; senão, acrescenta no fim.</summary>
        public object? this[string key]
        {
            get => TryGet(key, out object? value) ? value : null;
            set
            {
                int index = IndexOf(key);
                if (index >= 0) _items[index] = new KeyValuePair<string, object?>(key, value);
                else _items.Add(new KeyValuePair<string, object?>(key, value));
            }
        }

        public void Add(string key, object? value) => this[key] = value;

        public bool ContainsKey(string key) => IndexOf(key) >= 0;

        public bool TryGet(string key, out object? value)
        {
            int index = IndexOf(key);
            value = index >= 0 ? _items[index].Value : null;
            return index >= 0;
        }

        public string? GetString(string key) => this[key] as string;

        public JsonObject? GetObject(string key) => this[key] as JsonObject;

        public List<object?>? GetArray(string key) => this[key] as List<object?>;

        public double? GetNumber(string key) => this[key] is double d ? d : (double?)null;

        /// <summary>Só aceita número inteiro; 1.5 ou texto contam como ausente.</summary>
        public int? GetInt(string key)
        {
            double? d = GetNumber(key);
            if (d is null || d.Value != Math.Floor(d.Value) || d.Value < int.MinValue || d.Value > int.MaxValue) return null;
            return (int)d.Value;
        }

        public bool? GetBool(string key) => this[key] is bool b ? b : (bool?)null;

        public IEnumerator<KeyValuePair<string, object?>> GetEnumerator() => _items.GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        private int IndexOf(string key)
        {
            for (int i = 0; i < _items.Count; i++)
                if (string.Equals(_items[i].Key, key, StringComparison.Ordinal)) return i;
            return -1;
        }
    }

    /// <summary>
    /// JSON mínimo, sem dependências: o Unity não traz System.Text.Json e o JsonUtility
    /// não existe fora da engine. Objetos viram <see cref="JsonObject"/>, listas viram
    /// List&lt;object?&gt;, números viram double.
    /// </summary>
    public static class MiniJson
    {
        public static object? Parse(string json)
        {
            var parser = new Parser(json);
            parser.SkipWhitespace();
            object? value = parser.ReadValue(0);
            parser.SkipWhitespace();
            if (!parser.AtEnd) throw parser.Malformed();
            return value;
        }

        /// <summary>
        /// Indentado com dois espaços. Listas e objetos pequenos de nível mais fundo
        /// ficam numa linha só — um sprite por linha deixa a planilha fácil de comparar.
        /// </summary>
        public static string Write(object? value)
        {
            var sb = new StringBuilder(4096);
            WriteValue(sb, value, 0);
            sb.Append('\n');
            return sb.ToString();
        }

        private const int InlineLimit = 160;

        private static void WriteValue(StringBuilder sb, object? value, int depth)
        {
            switch (value)
            {
                case JsonObject obj:
                    WriteObject(sb, obj, depth);
                    break;
                case IList list:
                    WriteArray(sb, list, depth);
                    break;
                default:
                    WritePrimitive(sb, value);
                    break;
            }
        }

        private static void WriteObject(StringBuilder sb, JsonObject obj, int depth)
        {
            if (obj.Count == 0)
            {
                sb.Append("{}");
                return;
            }

            if (TryInline(obj, depth, out string? inline))
            {
                sb.Append(inline);
                return;
            }

            sb.Append("{\n");
            int i = 0;
            foreach (var pair in obj)
            {
                Indent(sb, depth + 1);
                WriteString(sb, pair.Key);
                sb.Append(": ");
                WriteValue(sb, pair.Value, depth + 1);
                if (++i < obj.Count) sb.Append(',');
                sb.Append('\n');
            }
            Indent(sb, depth);
            sb.Append('}');
        }

        private static void WriteArray(StringBuilder sb, IList list, int depth)
        {
            if (list.Count == 0)
            {
                sb.Append("[]");
                return;
            }

            if (TryInline(list, depth, out string? inline))
            {
                sb.Append(inline);
                return;
            }

            sb.Append("[\n");
            for (int i = 0; i < list.Count; i++)
            {
                Indent(sb, depth + 1);
                WriteValue(sb, list[i], depth + 1);
                if (i < list.Count - 1) sb.Append(',');
                sb.Append('\n');
            }
            Indent(sb, depth);
            sb.Append(']');
        }

        /// <summary>
        /// Numa linha só quando cabe e não tem lista dentro: a partir do segundo nível
        /// (os sprites), ou no primeiro se for um par simples como "size".
        /// </summary>
        private static bool TryInline(object container, int depth, out string? inline)
        {
            inline = null;
            bool small = container is JsonObject o && o.Count <= 2 && OnlyPrimitives(o);
            if (depth < 2 && !(depth == 1 && small)) return false;
            if (ContainsList(container)) return false;

            var sb = new StringBuilder();
            WriteCompact(sb, container);
            if (sb.Length > InlineLimit) return false;

            inline = sb.ToString();
            return true;
        }

        private static bool OnlyPrimitives(JsonObject obj)
        {
            foreach (var pair in obj)
                if (pair.Value is JsonObject || pair.Value is IList) return false;
            return true;
        }

        private static bool ContainsList(object container)
        {
            if (container is JsonObject obj)
            {
                foreach (var pair in obj)
                    if (pair.Value is IList || (pair.Value is JsonObject child && ContainsList(child))) return true;
                return false;
            }

            if (container is IList list)
                foreach (object? item in list)
                    if (item is IList || (item is JsonObject child && ContainsList(child))) return true;

            return false;
        }

        private static void WriteCompact(StringBuilder sb, object? value)
        {
            switch (value)
            {
                case JsonObject obj:
                    sb.Append('{');
                    int i = 0;
                    foreach (var pair in obj)
                    {
                        if (i++ > 0) sb.Append(", ");
                        WriteString(sb, pair.Key);
                        sb.Append(": ");
                        WriteCompact(sb, pair.Value);
                    }
                    sb.Append('}');
                    break;

                case IList list:
                    sb.Append('[');
                    for (int j = 0; j < list.Count; j++)
                    {
                        if (j > 0) sb.Append(", ");
                        WriteCompact(sb, list[j]);
                    }
                    sb.Append(']');
                    break;

                default:
                    WritePrimitive(sb, value);
                    break;
            }
        }

        private static void WritePrimitive(StringBuilder sb, object? value)
        {
            switch (value)
            {
                case null:
                    sb.Append("null");
                    break;
                case string s:
                    WriteString(sb, s);
                    break;
                case bool b:
                    sb.Append(b ? "true" : "false");
                    break;
                case int n:
                    sb.Append(n.ToString(CultureInfo.InvariantCulture));
                    break;
                case long n:
                    sb.Append(n.ToString(CultureInfo.InvariantCulture));
                    break;
                case float f:
                    WriteNumber(sb, f);
                    break;
                case double d:
                    WriteNumber(sb, d);
                    break;
                default:
                    throw new ArgumentException($"Tipo não suportado em JSON: {value.GetType().Name}");
            }
        }

        private static void WriteNumber(StringBuilder sb, double d)
        {
            if (double.IsNaN(d) || double.IsInfinity(d)) throw new ArgumentException("JSON não aceita NaN nem infinito.");
            sb.Append(d.ToString("R", CultureInfo.InvariantCulture));
        }

        private static void WriteString(StringBuilder sb, string s)
        {
            sb.Append('"');
            foreach (char c in s)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    case '\b': sb.Append("\\b"); break;
                    case '\f': sb.Append("\\f"); break;
                    default:
                        if (c < 0x20) sb.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                        else sb.Append(c);
                        break;
                }
            }
            sb.Append('"');
        }

        private static void Indent(StringBuilder sb, int depth) => sb.Append(' ', depth * 2);

        private sealed class Parser
        {
            private const int MaxDepth = 64;

            private readonly string _s;
            private int _pos;

            public Parser(string s) => _s = s;

            public bool AtEnd => _pos >= _s.Length;

            public InvalidDataException Malformed() => new InvalidDataException(CoreText.Current.JsonMalformed(_pos));

            public void SkipWhitespace()
            {
                while (_pos < _s.Length && (_s[_pos] == ' ' || _s[_pos] == '\t' || _s[_pos] == '\n' || _s[_pos] == '\r' || _s[_pos] == '﻿'))
                    _pos++;
            }

            public object? ReadValue(int depth)
            {
                if (AtEnd || depth > MaxDepth) throw Malformed();

                char c = _s[_pos];
                switch (c)
                {
                    case '{': return ReadObject(depth);
                    case '[': return ReadArray(depth);
                    case '"': return ReadString();
                    case 't': Expect("true"); return true;
                    case 'f': Expect("false"); return false;
                    case 'n': Expect("null"); return null;
                    default:
                        if (c == '-' || (c >= '0' && c <= '9')) return ReadNumber();
                        throw Malformed();
                }
            }

            private JsonObject ReadObject(int depth)
            {
                var obj = new JsonObject();
                _pos++; // {
                SkipWhitespace();
                if (Peek('}'))
                {
                    _pos++;
                    return obj;
                }

                while (true)
                {
                    SkipWhitespace();
                    if (!Peek('"')) throw Malformed();
                    string key = ReadString();
                    SkipWhitespace();
                    if (!Peek(':')) throw Malformed();
                    _pos++;
                    SkipWhitespace();
                    obj[key] = ReadValue(depth + 1);
                    SkipWhitespace();

                    if (Peek(','))
                    {
                        _pos++;
                        continue;
                    }
                    if (Peek('}'))
                    {
                        _pos++;
                        return obj;
                    }
                    throw Malformed();
                }
            }

            private List<object?> ReadArray(int depth)
            {
                var list = new List<object?>();
                _pos++; // [
                SkipWhitespace();
                if (Peek(']'))
                {
                    _pos++;
                    return list;
                }

                while (true)
                {
                    SkipWhitespace();
                    list.Add(ReadValue(depth + 1));
                    SkipWhitespace();

                    if (Peek(','))
                    {
                        _pos++;
                        continue;
                    }
                    if (Peek(']'))
                    {
                        _pos++;
                        return list;
                    }
                    throw Malformed();
                }
            }

            private string ReadString()
            {
                _pos++; // "
                var sb = new StringBuilder();

                while (true)
                {
                    if (AtEnd) throw Malformed();
                    char c = _s[_pos++];

                    if (c == '"') return sb.ToString();
                    if (c < 0x20) throw Malformed();
                    if (c != '\\')
                    {
                        sb.Append(c);
                        continue;
                    }

                    if (AtEnd) throw Malformed();
                    char e = _s[_pos++];
                    switch (e)
                    {
                        case '"': sb.Append('"'); break;
                        case '\\': sb.Append('\\'); break;
                        case '/': sb.Append('/'); break;
                        case 'b': sb.Append('\b'); break;
                        case 'f': sb.Append('\f'); break;
                        case 'n': sb.Append('\n'); break;
                        case 'r': sb.Append('\r'); break;
                        case 't': sb.Append('\t'); break;
                        case 'u':
                            if (_pos + 4 > _s.Length ||
                                !int.TryParse(_s.Substring(_pos, 4), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out int code))
                            {
                                throw Malformed();
                            }
                            sb.Append((char)code);
                            _pos += 4;
                            break;
                        default:
                            throw Malformed();
                    }
                }
            }

            private double ReadNumber()
            {
                int start = _pos;
                if (Peek('-')) _pos++;
                while (_pos < _s.Length && IsNumberChar(_s[_pos])) _pos++;

                if (!double.TryParse(_s.Substring(start, _pos - start), NumberStyles.Float, CultureInfo.InvariantCulture, out double value))
                {
                    _pos = start;
                    throw Malformed();
                }
                return value;
            }

            private static bool IsNumberChar(char c) =>
                (c >= '0' && c <= '9') || c == '.' || c == 'e' || c == 'E' || c == '+' || c == '-';

            private void Expect(string word)
            {
                if (string.CompareOrdinal(_s, _pos, word, 0, word.Length) != 0) throw Malformed();
                _pos += word.Length;
            }

            private bool Peek(char c) => _pos < _s.Length && _s[_pos] == c;
        }
    }
}
