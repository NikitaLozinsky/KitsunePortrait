using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Kingmaker.Localization;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace KitsunePortrait
{
    /// <summary>
    /// Одна строка локализации. Помимо Key/ProcessTemplates объект может содержать
    /// произвольное число языковых колонок ("ruRU", "enGB", "zhCN", ...). Они не
    /// объявлены явными полями, чтобы добавление нового языка не требовало правок
    /// кода — новая колонка в JSON подхватывается автоматически через JsonExtensionData.
    /// </summary>
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

    /// <summary>
    /// Загружает Locales/localization.json и отдаёт локализованные строки по ключу.
    /// Цепочка фолбэков: текущий язык -> enGB -> ruRU -> сам ключ (чтобы отсутствующий
    /// перевод был явно виден в интерфейсе, а не приводил к пустой строке или краху).
    /// </summary>
    public static class Localization
    {
        private const string FileName = "localization.json";
        public const string DefaultLocale = "ruRU";
        private const string SecondaryFallbackLocale = "enGB";

        private static readonly Dictionary<string, LocalizedEntry> Entries = new Dictionary<string, LocalizedEntry>();
        private static List<string> _availableLocalesCache;

        /// <summary>
        /// Текущий язык интерфейса мода. Вычисляется "на лету" при каждом обращении:
        /// 1) если игрок явно выбрал язык в ModUI (Settings.Language) — используется он;
        /// 2) иначе берётся текущий язык самой игры (Kingmaker.Localization.LocalizationManager.CurrentLocale),
        ///    если для него есть перевод в файле;
        /// 3) иначе — DefaultLocale ("ruRU").
        /// Живое вычисление (а не разовое кэширование при загрузке мода) означает, что
        /// если игрок сменит язык игры в настройках прямо во время сессии — тексты мода
        /// подхватят это без перезапуска.
        /// </summary>
        public static string CurrentLocale
        {
            get
            {
                if (!string.IsNullOrEmpty(Main.Settings?.Language))
                {
                    return Main.Settings.Language;
                }

                string gameLocale = TryGetGameLocale();
                if (!string.IsNullOrEmpty(gameLocale) && GetAvailableLocales().Contains(gameLocale))
                {
                    return gameLocale;
                }

                return DefaultLocale;
            }
        }

        public static void Load()
        {
            Entries.Clear();
            _availableLocalesCache = null;

            try
            {
                // 1. Основной путь: Mods/KitsunePortrait/Locales/localization.json
                string path = Path.Combine(Main.ModEntry.Path, "Locales", "localization.json");

                // 2. Резервный путь (корень): Mods/KitsunePortrait/localization.json
                if (!File.Exists(path))
                {
                    path = Path.Combine(Main.ModEntry.Path, "localization.json");
                }

                // 3. Резервный путь с большой буквы: Mods/KitsunePortrait/Localization.json
                if (!File.Exists(path))
                {
                    path = Path.Combine(Main.ModEntry.Path, "Localization.json");
                }

                if (!File.Exists(path))
                {
                    Main.Logger?.Error($"[Localization] Файл локализации не найден по путям в Locales/ или корне!");
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

                Main.Logger?.Log($"[Localization] Успешно загружено строк: {Entries.Count}");
            }
            catch (Exception ex)
            {
                Main.Logger?.Error($"[Localization] Ошибка загрузки локализации: {ex}");
            }
        }

        /// <summary>
        /// Подтверждено декомпиляцией Assembly-CSharp: публичное статическое свойство
        /// Kingmaker.Localization.LocalizationManager.CurrentLocale (enum Locale, значения
        /// вида enGB/deDE/frFR/ruRU/zhCN/esES/ptBR/itIT — те же коды, что и колонки в JSON).
        /// Всё равно оборачиваем в try/catch: свойство читает SettingsRoot.Game.Main.Localization,
        /// который в теории может быть ещё не готов на самых ранних этапах загрузки игры.
        /// </summary>
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

        /// <summary>
        /// Явно переключить язык интерфейса мода (например, из ModUI) и сохранить выбор.
        /// Передайте null/пустую строку, чтобы вернуться к автоопределению по языку игры.
        /// </summary>
        public static void SetLanguage(string locale)
        {
            if (Main.Settings == null) return;

            Main.Settings.Language = string.IsNullOrEmpty(locale) ? null : locale;
            Main.Settings.Save(Main.ModEntry);
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

        /// <summary>
        /// Возвращает локализованную строку по ключу. Если args не пустой — строка
        /// прогоняется через string.Format (плейсхолдеры {0}, {1}, ...).
        /// Если ключ не найден ни в одном языке — возвращает сам ключ, чтобы недостающий
        /// перевод было легко заметить и найти в файле.
        /// </summary>
        public static string Get(string key, params object[] args)
        {
            if (string.IsNullOrEmpty(key)) return string.Empty;

            if (!Entries.TryGetValue(key, out var entry))
            {
                return key;
            }

            string text = ResolveText(entry, CurrentLocale)
                          ?? ResolveText(entry, SecondaryFallbackLocale)
                          ?? ResolveText(entry, DefaultLocale)
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