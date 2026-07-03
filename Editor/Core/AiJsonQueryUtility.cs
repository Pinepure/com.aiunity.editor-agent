#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Text;

namespace AiUnity.EditorAgent
{
    internal static class AiJsonQueryUtility
    {
        public static bool TryGetRawValue(string json, string path, out string rawValue)
        {
            rawValue = string.Empty;
            if (string.IsNullOrWhiteSpace(json)) return false;
            if (string.IsNullOrWhiteSpace(path))
            {
                rawValue = json.Trim();
                return true;
            }

            string current = json.Trim();
            string[] segments = path.Split('.');
            for (int i = 0; i < segments.Length; i++)
            {
                string segment = segments[i] == null ? string.Empty : segments[i].Trim();
                if (string.IsNullOrEmpty(segment)) return false;
                string next;
                if (!TryGetFieldValue(current, segment, out next)) return false;
                current = next;
            }

            rawValue = current.Trim();
            return true;
        }

        public static bool TryGetString(string json, string path, out string value)
        {
            value = string.Empty;
            string raw;
            if (!TryGetRawValue(json, path, out raw)) return false;
            raw = raw.Trim();
            if (raw.Length >= 2 && raw[0] == '"' && raw[raw.Length - 1] == '"')
            {
                value = Unescape(raw.Substring(1, raw.Length - 2));
                return true;
            }

            value = raw;
            return true;
        }

        public static string ReplacePlaceholders(string template, Dictionary<string, string> stepJsonById)
        {
            if (string.IsNullOrEmpty(template)) return "{}";
            if (stepJsonById == null || stepJsonById.Count == 0) return template;

            StringBuilder sb = new StringBuilder(template.Length + 32);
            int index = 0;
            while (index < template.Length)
            {
                int open = template.IndexOf("{{", index, StringComparison.Ordinal);
                if (open < 0)
                {
                    sb.Append(template, index, template.Length - index);
                    break;
                }

                sb.Append(template, index, open - index);
                int close = template.IndexOf("}}", open + 2, StringComparison.Ordinal);
                if (close < 0)
                {
                    sb.Append(template, open, template.Length - open);
                    break;
                }

                string expression = template.Substring(open + 2, close - open - 2).Trim();
                string resolved = ResolvePlaceholder(expression, stepJsonById);
                sb.Append(resolved);
                index = close + 2;
            }

            return sb.ToString();
        }

        private static string ResolvePlaceholder(string expression, Dictionary<string, string> stepJsonById)
        {
            if (string.IsNullOrEmpty(expression)) return "null";
            int dot = expression.IndexOf('.');
            string stepId = dot < 0 ? expression : expression.Substring(0, dot);
            string path = dot < 0 ? string.Empty : expression.Substring(dot + 1);

            string stepJson;
            if (!stepJsonById.TryGetValue(stepId, out stepJson)) return "null";
            if (string.IsNullOrEmpty(path)) return stepJson;

            string raw;
            return TryGetRawValue(stepJson, path, out raw) ? raw : "null";
        }

        private static bool TryGetFieldValue(string jsonObject, string fieldName, out string rawValue)
        {
            rawValue = string.Empty;
            if (string.IsNullOrWhiteSpace(jsonObject)) return false;
            string json = jsonObject.Trim();
            if (json.Length < 2 || json[0] != '{' || json[json.Length - 1] != '}') return false;

            int index = 1;
            while (index < json.Length - 1)
            {
                SkipWhitespace(json, ref index);
                if (index >= json.Length - 1) break;
                if (json[index] == ',')
                {
                    index++;
                    continue;
                }
                if (json[index] != '"') return false;

                int keyEnd = FindStringEnd(json, index + 1);
                if (keyEnd < 0) return false;
                string key = Unescape(json.Substring(index + 1, keyEnd - index - 1));
                index = keyEnd + 1;
                SkipWhitespace(json, ref index);
                if (index >= json.Length || json[index] != ':') return false;
                index++;
                SkipWhitespace(json, ref index);

                int valueEnd = FindValueEnd(json, index);
                if (valueEnd < 0) return false;
                if (key == fieldName)
                {
                    rawValue = json.Substring(index, valueEnd - index);
                    return true;
                }

                index = valueEnd;
            }

            return false;
        }

        private static void SkipWhitespace(string text, ref int index)
        {
            while (index < text.Length)
            {
                char c = text[index];
                if (c != ' ' && c != '\t' && c != '\r' && c != '\n') break;
                index++;
            }
        }

        private static int FindStringEnd(string text, int index)
        {
            bool escaped = false;
            for (int i = index; i < text.Length; i++)
            {
                char c = text[i];
                if (escaped)
                {
                    escaped = false;
                    continue;
                }
                if (c == '\\')
                {
                    escaped = true;
                    continue;
                }
                if (c == '"') return i;
            }
            return -1;
        }

        private static int FindValueEnd(string text, int index)
        {
            if (index >= text.Length) return -1;
            char c = text[index];
            if (c == '"')
            {
                int end = FindStringEnd(text, index + 1);
                return end < 0 ? -1 : end + 1;
            }
            if (c == '{' || c == '[')
            {
                return FindContainerEnd(text, index);
            }

            int i = index;
            while (i < text.Length)
            {
                char valueChar = text[i];
                if (valueChar == ',' || valueChar == '}' || valueChar == ']') break;
                i++;
            }
            return i;
        }

        private static int FindContainerEnd(string text, int start)
        {
            char open = text[start];
            char close = open == '{' ? '}' : ']';
            int depth = 0;
            bool insideString = false;
            bool escaped = false;

            for (int i = start; i < text.Length; i++)
            {
                char c = text[i];
                if (insideString)
                {
                    if (escaped)
                    {
                        escaped = false;
                    }
                    else if (c == '\\')
                    {
                        escaped = true;
                    }
                    else if (c == '"')
                    {
                        insideString = false;
                    }
                    continue;
                }

                if (c == '"')
                {
                    insideString = true;
                    continue;
                }

                if (c == open) depth++;
                if (c == close)
                {
                    depth--;
                    if (depth == 0) return i + 1;
                }
            }

            return -1;
        }

        private static string Unescape(string value)
        {
            if (string.IsNullOrEmpty(value)) return string.Empty;

            StringBuilder sb = new StringBuilder(value.Length);
            for (int i = 0; i < value.Length; i++)
            {
                char c = value[i];
                if (c != '\\' || i + 1 >= value.Length)
                {
                    sb.Append(c);
                    continue;
                }

                i++;
                char next = value[i];
                switch (next)
                {
                    case '\\': sb.Append('\\'); break;
                    case '"': sb.Append('"'); break;
                    case 'n': sb.Append('\n'); break;
                    case 'r': sb.Append('\r'); break;
                    case 't': sb.Append('\t'); break;
                    case 'b': sb.Append('\b'); break;
                    case 'f': sb.Append('\f'); break;
                    case 'u':
                        if (i + 4 < value.Length)
                        {
                            string hex = value.Substring(i + 1, 4);
                            int code;
                            if (int.TryParse(hex, System.Globalization.NumberStyles.HexNumber, System.Globalization.CultureInfo.InvariantCulture, out code))
                            {
                                sb.Append((char)code);
                                i += 4;
                            }
                        }
                        break;
                    default:
                        sb.Append(next);
                        break;
                }
            }

            return sb.ToString();
        }
    }
}
#endif
