using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace SimpleMultiGestureTool.Editor
{
    internal enum SimpleMultiGestureLanguage
    {
        Japanese,
        Korean,
        English
    }

    internal static class SimpleMultiGestureLocalization
    {
        private const string BasePath = "Packages/me.kirisame.smg/Editor/Localization/";
        private const string EditorPrefsKey = "me.kirisame.smg.language";
        private const SimpleMultiGestureLanguage FallbackLanguage =
            SimpleMultiGestureLanguage.Japanese;

        private static readonly Dictionary<SimpleMultiGestureLanguage, Dictionary<string, string>>
            Tables = new Dictionary<SimpleMultiGestureLanguage, Dictionary<string, string>>();

        internal static SimpleMultiGestureLanguage CurrentLanguage
        {
            get
            {
                var stored = EditorPrefs.GetInt(EditorPrefsKey, (int)FallbackLanguage);
                return Enum.IsDefined(typeof(SimpleMultiGestureLanguage), stored)
                    ? (SimpleMultiGestureLanguage)stored
                    : FallbackLanguage;
            }
            set => EditorPrefs.SetInt(EditorPrefsKey, (int)value);
        }

        internal static GUIContent Label(string key)
        {
            return new GUIContent(Text(key));
        }

        internal static string Text(string key)
        {
            return Text(CurrentLanguage, key);
        }

        internal static string Text(SimpleMultiGestureLanguage language, string key)
        {
            var table = GetTable(language);
            if (table.TryGetValue(key, out var value))
            {
                return value;
            }

            var fallback = GetTable(FallbackLanguage);
            return fallback.TryGetValue(key, out value) ? value : key;
        }

        internal static string Format(string key, params object[] arguments)
        {
            try
            {
                return string.Format(Text(key), arguments);
            }
            catch (FormatException)
            {
                return Text(key);
            }
        }

        internal static void ClearCache()
        {
            Tables.Clear();
        }

        private static Dictionary<string, string> GetTable(SimpleMultiGestureLanguage language)
        {
            if (Tables.TryGetValue(language, out var table))
            {
                return table;
            }

            table = LoadTable(language);
            Tables[language] = table;
            return table;
        }

        private static Dictionary<string, string> LoadTable(SimpleMultiGestureLanguage language)
        {
            var asset = AssetDatabase.LoadAssetAtPath<TextAsset>(
                BasePath + LocaleCode(language) + ".json");
            if (asset == null)
            {
                return new Dictionary<string, string>();
            }

            var parsed = JsonUtility.FromJson<StringTable>(asset.text);
            var result = new Dictionary<string, string>();
            if (parsed?.entries == null)
            {
                return result;
            }

            foreach (var entry in parsed.entries)
            {
                if (!string.IsNullOrEmpty(entry.key))
                {
                    result[entry.key] = entry.value ?? string.Empty;
                }
            }

            return result;
        }

        private static string LocaleCode(SimpleMultiGestureLanguage language)
        {
            switch (language)
            {
                case SimpleMultiGestureLanguage.Korean:
                    return "ko-KR";
                case SimpleMultiGestureLanguage.English:
                    return "en-US";
                default:
                    return "ja-JP";
            }
        }

        [Serializable]
        private sealed class StringTable
        {
            public StringEntry[] entries;
        }

        [Serializable]
        private sealed class StringEntry
        {
            public string key;
            public string value;
        }
    }
}
