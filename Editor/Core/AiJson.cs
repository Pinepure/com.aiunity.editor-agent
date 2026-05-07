#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEngine;

namespace AiUnity.EditorAgent
{
    public static class AiJson
    {
        public static string Escape(string value)
        {
            if (value == null) return string.Empty;

            StringBuilder sb = new StringBuilder(value.Length + 16);
            for (int i = 0; i < value.Length; i++)
            {
                char c = value[i];
                switch (c)
                {
                    case '\\': sb.Append("\\\\"); break;
                    case '"': sb.Append("\\\""); break;
                    case '\b': sb.Append("\\b"); break;
                    case '\f': sb.Append("\\f"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < 32)
                        {
                            sb.Append("\\u");
                            sb.Append(((int)c).ToString("x4"));
                        }
                        else
                        {
                            sb.Append(c);
                        }
                        break;
                }
            }
            return sb.ToString();
        }

        public static string Quote(string value)
        {
            return "\"" + Escape(value) + "\"";
        }

        public static string Bool(bool value)
        {
            return value ? "true" : "false";
        }

        public static string Number(float value)
        {
            if (float.IsNaN(value) || float.IsInfinity(value)) return "0";
            return value.ToString(CultureInfo.InvariantCulture);
        }

        public static string Number(double value)
        {
            if (double.IsNaN(value) || double.IsInfinity(value)) return "0";
            return value.ToString(CultureInfo.InvariantCulture);
        }

        public static string Number(int value)
        {
            return value.ToString(CultureInfo.InvariantCulture);
        }

        public static string Number(long value)
        {
            return value.ToString(CultureInfo.InvariantCulture);
        }

        public static string StringArray(IEnumerable<string> values)
        {
            StringBuilder sb = new StringBuilder();
            sb.Append('[');
            bool first = true;
            if (values != null)
            {
                foreach (string value in values)
                {
                    if (!first) sb.Append(',');
                    sb.Append(Quote(value));
                    first = false;
                }
            }
            sb.Append(']');
            return sb.ToString();
        }

        public static string Vector3(Vector3 v)
        {
            return "[" + Number(v.x) + "," + Number(v.y) + "," + Number(v.z) + "]";
        }

        public static string Error(string message)
        {
            return "{\"ok\":false,\"error\":" + Quote(message) + "}";
        }

        public static string AsJsonValue(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return "{}";

            string trimmed = value.Trim();
            if (LooksLikeJsonValue(trimmed)) return trimmed;
            return Quote(trimmed);
        }

        public static bool LooksLikeJsonValue(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return false;
            string s = value.Trim();
            if (s == "true" || s == "false" || s == "null") return true;
            if (s.StartsWith("{") && s.EndsWith("}")) return true;
            if (s.StartsWith("[") && s.EndsWith("]")) return true;
            double number;
            return double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out number);
        }

        public static T FromJsonOrThrow<T>(string json)
        {
            if (string.IsNullOrWhiteSpace(json)) json = "{}";
            try
            {
                return JsonUtility.FromJson<T>(json);
            }
            catch (Exception e)
            {
                throw new Exception("Invalid JSON for " + typeof(T).Name + ": " + e.Message);
            }
        }
    }
}
#endif
