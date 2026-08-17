using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Kingmaker.Localization;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace KitsunePortrait
{
    [Serializable]
    public class LocalizedEntry
    {
        public string Key;
        public bool ProcessTemplates;

        [JsonExtensionData]
        public Dictionary<string, JToken> Translations = new Dictionary<string, JToken>();
    }

    [Serializable]
    public class LocalizationFile
    {
        public List<LocalizedEntry> LocalizedStrings = new List<LocalizedEntry>();
    }

    public static class Localization
    {
        private const string FileName = "localization.json";
        public const string FallbackLocale = "enGB";
        public const string SecondaryFallbackLocale = "ruRU";

        private static readonly Dictionary<string, LocalizedEntry> Entries = new Dictionary<string, LocalizedEntry>();
        private static List<string> _availableLocalesCache;

        /// <summary>
        /// Возвращает текущий язык игры. Вычисляется «на лету»:
        /// 1) Берется текущий язык игры (Kingmaker.Localization.LocalizationManager.CurrentLocale).
        /// 2) Если для него есть перевод в файле локализации — используется он.
        /// 3) Если такого языка в моде нет — переключается на английский ("enGB").
        /// </summary>
        public static string CurrentLocale
        {
            get
            {
                string gameLocale = TryGetGameLocale();
                if (!string.IsNullOrEmpty(gameLocale) && GetAvailableLocales().Contains(gameLocale))
                {
                    return gameLocale;
                }

                return FallbackLocale;
            }
        }

        public static void Load()
        {
            Entries.Clear();
            _availableLocalesCache = null;

            try
            {
                string path = Path.Combine(Main.ModEntry.Path, "Locales", FileName);
                if (!File.Exists(path))
                {
                    path = Path.Combine(Main.ModEntry.Path, FileName);
                }

                if (!File.Exists(path))
                {
                    Main.Logger?.Error($"[Localization] Файл локализации не найден в Locales/ или корне: {path}");
                    return;
                }

                string json = File.ReadAllText(path);
                var data = JsonConvert.DeserializeObject<LocalizationFile>(json);

                if (data?.LocalizedStrings != null)
                {
                    foreach (var entry in data.LocalizedStrings)
                    {
                        if (string.IsNullOrEmpty(entry.Key)) continue;
                        Entries[entry.Key] = entry;
                    }
                }

                Main.Logger?.Log($"[Localization] Загружено строк: {Entries.Count}. Доступные языки в моде: {string.Join(", ", GetAvailableLocales())}");
            }
            catch (Exception ex)
            {
                Main.Logger?.Error($"[Localization] Ошибка загрузки локализации: {ex}");
            }
        }

        private static string TryGetGameLocale()
        {
            try
            {
                return LocalizationManager.CurrentLocale.ToString();
            }
            catch
            {
                return null;
            }
        }

        public static List<string> GetAvailableLocales()
        {
            if (_availableLocalesCache != null) return _availableLocalesCache;

            var set = new HashSet<string>();
            foreach (var entry in Entries.Values)
            {
                if (entry.Translations == null) continue;
                foreach (var key in entry.Translations.Keys) set.Add(key);
            }

            var list = set.ToList();
            list.Sort(StringComparer.OrdinalIgnoreCase);
            _availableLocalesCache = list;
            return list;
        }

        public static string Get(string key, params object[] args)
        {
            if (string.IsNullOrEmpty(key)) return string.Empty;

            if (!Entries.TryGetValue(key, out var entry))
            {
                return key;
            }

            // Цепочка поиска: Текущий язык игры -> enGB -> ruRU -> Ключ
            string text = ResolveText(entry, CurrentLocale)
                          ?? ResolveText(entry, FallbackLocale)
                          ?? ResolveText(entry, SecondaryFallbackLocale)
                          ?? key;

            if (args == null || args.Length == 0) return text;

            try
            {
                return string.Format(text, args);
            }
            catch (FormatException)
            {
                return text;
            }
        }

        private static string ResolveText(LocalizedEntry entry, string locale)
        {
            if (string.IsNullOrEmpty(locale)) return null;
            if (entry.Translations == null) return null;
            if (!entry.Translations.TryGetValue(locale, out JToken token)) return null;

            string value = token?.Type == JTokenType.String ? token.Value<string>() : token?.ToString();
            return string.IsNullOrEmpty(value) ? null : value;
        }
    }
}