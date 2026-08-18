using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Kingmaker;
using Kingmaker.EntitySystem.Entities;
using UnityEngine;
using UnityModManagerNet;

namespace KitsunePortrait
{
    public static class ModUI
    {
        private const int PreviewSize = 40;
        private const int PreviewCacheCap = 200;
        private const int BrowserThumbWidth = 90;
        private const int BrowserThumbHeight = 120;
        private const float BrowserHeight = 320f;

        private static readonly Dictionary<string, string> InputBuffers = new Dictionary<string, string>();

        // Кэш превью-спрайтов по ID портрета. GetSmallPortraitSprite для кастомных
        // портретов может читать с диска — IMGUI вызывает OnGUI каждый кадр, поэтому
        // без кэша это означало бы диск-I/O десятки раз в секунду, пока открыта панель.
        private static readonly Dictionary<string, Sprite> PreviewCache = new Dictionary<string, Sprite>();

        // Ключ поля ввода (foxKey/humanKey), для которого сейчас открыта галерея, либо null.
        private static string _browserOpenForKey;
        private static Vector2 _browserScrollPos;

        // Список имён папок кастомных портретов из Portraits/. Читается с диска один раз
        // при первом открытии галереи за сессию, обновляется по кнопке.
        private static List<string> _customPortraitFolders;

        public static void OnGUI(UnityModManager.ModEntry modEntry)
        {
            if (Game.Instance?.Player == null)
            {
                GUILayout.Label(Localization.Get("Kitsune.ModUI.LoadSaveMessage"));
                return;
            }

            GUILayout.Label(Localization.Get("Kitsune.ModUI.Title"));
            GUILayout.Label(Localization.Get("Kitsune.ModUI.FallbackLine", PortraitManager.FallbackHumanPortraitId));
            GUILayout.Space(10);

            var party = Game.Instance.Player.Party;
            if (party == null || party.Count == 0) return;

            var activeKeys = new HashSet<string>();

            foreach (UnitEntityData unit in party)
            {
                if (!PortraitManager.IsKitsune(unit)) continue;

                string unitId = unit.UniqueId;
                bool inHuman = PortraitManager.IsInHumanForm(unit);

                if (!Main.Settings.CharacterPortraits.TryGetValue(unitId, out PortraitPair pair))
                {
                    pair = new PortraitPair();
                    Main.Settings.CharacterPortraits[unitId] = pair;
                }

                string foxKey = unitId + "_fox";
                string humanKey = unitId + "_human";
                activeKeys.Add(foxKey);
                activeKeys.Add(humanKey);

                if (!InputBuffers.ContainsKey(foxKey)) InputBuffers[foxKey] = pair.FoxPortrait ?? "";
                if (!InputBuffers.ContainsKey(humanKey)) InputBuffers[humanKey] = pair.HumanPortrait ?? "";

                GUILayout.BeginVertical(GUI.skin.box);

                string formLabel = inHuman
                    ? $"<color=yellow>{Localization.Get("Kitsune.Form.Human")}</color>"
                    : $"<color=orange>{Localization.Get("Kitsune.Form.Fox")}</color>";
                GUILayout.Label(Localization.Get("Kitsune.ModUI.CharacterFormLine", unit.CharacterName, formLabel));
                GUILayout.Space(5);

                DrawPortraitRow(Localization.Get("Kitsune.ModUI.FoxPortraitLabel"), foxKey);
                DrawPortraitRow(Localization.Get("Kitsune.ModUI.HumanPortraitLabel"), humanKey);

                GUILayout.Space(5);

                if (GUILayout.Button(Localization.Get("Kitsune.ModUI.ApplyButton"), GUILayout.Width(180), GUILayout.Height(25)))
                {
                    pair.FoxPortrait = InputBuffers[foxKey].Trim();
                    pair.HumanPortrait = InputBuffers[humanKey].Trim();

                    Main.Settings.Save(modEntry);

                    PortraitManager.UpdatePortrait(unit);
                    Main.Logger?.Log($"[ModUI] Обновлены портреты для {unit.CharacterName}. Лиса: '{pair.FoxPortrait}', Человек: '{pair.HumanPortrait}'");
                }

                GUILayout.EndVertical();
                GUILayout.Space(8);
            }

            PruneStaleBuffers(activeKeys);
        }

        private static void DrawPortraitRow(string label, string bufferKey)
        {
            GUILayout.BeginHorizontal();
            DrawPortraitPreview(InputBuffers[bufferKey]);
            GUILayout.BeginVertical();
            GUILayout.Label(label);
            GUILayout.BeginHorizontal();
            InputBuffers[bufferKey] = GUILayout.TextField(InputBuffers[bufferKey], GUILayout.Width(220));
            if (GUILayout.Button(Localization.Get("Kitsune.ModUI.BrowseButton"), GUILayout.Width(80)))
            {
                if (_browserOpenForKey == bufferKey)
                {
                    _browserOpenForKey = null;
                }
                else
                {
                    _browserOpenForKey = bufferKey;
                    EnsureCustomPortraitFoldersLoaded(forceRefresh: false);
                }
            }
            GUILayout.EndHorizontal();
            GUILayout.EndVertical();
            GUILayout.EndHorizontal();

            if (_browserOpenForKey == bufferKey)
            {
                DrawCustomPortraitBrowser(bufferKey);
            }
        }

        /// <summary>
        /// Раскрывающаяся галерея кастомных (папочных) портретов из Portraits/.
        /// Только кастомные — для встроенных ванильных портретов игры нужен подтверждённый
        /// декомпиляцией способ перечисления BlueprintPortrait, которого пока нет.
        /// </summary>
        private static void DrawCustomPortraitBrowser(string bufferKey)
        {
            GUILayout.BeginVertical(GUI.skin.box);

            GUILayout.BeginHorizontal();
            GUILayout.Label(Localization.Get("Kitsune.ModUI.BrowserTitle"));
            GUILayout.FlexibleSpace();
            if (GUILayout.Button(Localization.Get("Kitsune.ModUI.BrowserRefresh"), GUILayout.Width(90)))
            {
                EnsureCustomPortraitFoldersLoaded(forceRefresh: true);
            }
            GUILayout.EndHorizontal();

            if (_customPortraitFolders == null || _customPortraitFolders.Count == 0)
            {
                GUILayout.Label(Localization.Get("Kitsune.ModUI.BrowserEmpty"));
            }
            else
            {
                _browserScrollPos = GUILayout.BeginScrollView(_browserScrollPos, GUILayout.Height(BrowserHeight));

                int maxCols = 4;
                int col = 0;

                GUILayout.BeginHorizontal();
                foreach (string folderName in _customPortraitFolders)
                {
                    if (col >= maxCols)
                    {
                        GUILayout.EndHorizontal();
                        GUILayout.BeginHorizontal();
                        col = 0;
                    }

                    GUILayout.BeginVertical(GUI.skin.box, GUILayout.Width(BrowserThumbWidth + 12));
                    
                    DrawThumbnail(folderName);

                    var labelStyle = new GUIStyle(GUI.skin.label)
                    {
                        alignment = TextAnchor.MiddleCenter,
                        fontSize = 10,
                        wordWrap = true
                    };
                    GUILayout.Label(folderName, labelStyle, GUILayout.Width(BrowserThumbWidth), GUILayout.Height(32));

                    if (GUILayout.Button(Localization.Get("Kitsune.ModUI.BrowserSelectButton"), GUILayout.Width(BrowserThumbWidth)))
                    {
                        InputBuffers[bufferKey] = folderName;
                        _browserOpenForKey = null;
                    }

                    GUILayout.EndVertical();
                    col++;
                }
                GUILayout.EndHorizontal();

                GUILayout.EndScrollView();
            }

            GUILayout.EndVertical();
        }

        private static void DrawThumbnail(string portraitId)
        {
            Sprite sprite = GetCachedPreview(portraitId);
            Rect rect = GUILayoutUtility.GetRect(BrowserThumbWidth, BrowserThumbHeight, GUILayout.Width(BrowserThumbWidth), GUILayout.Height(BrowserThumbHeight));
            GUI.Box(rect, GUIContent.none);

            if (sprite == null || sprite.texture == null) return;

            Rect uv = new Rect(
                sprite.rect.x / sprite.texture.width,
                sprite.rect.y / sprite.texture.height,
                sprite.rect.width / sprite.texture.width,
                sprite.rect.height / sprite.texture.height);

            GUI.DrawTextureWithTexCoords(rect, sprite.texture, uv);
        }

        private static void EnsureCustomPortraitFoldersLoaded(bool forceRefresh)
        {
            if (_customPortraitFolders != null && !forceRefresh) return;

            _customPortraitFolders = new List<string>();

            try
            {
                string portraitsPath = Path.Combine(Application.persistentDataPath, "Portraits");
                if (Directory.Exists(portraitsPath))
                {
                    foreach (string dir in Directory.GetDirectories(portraitsPath))
                    {
                        _customPortraitFolders.Add(Path.GetFileName(dir));
                    }
                    _customPortraitFolders.Sort(StringComparer.OrdinalIgnoreCase);
                }
                else
                {
                    Main.Logger?.Log($"[ModUI] Папка кастомных портретов не найдена: {portraitsPath}");
                }
            }
            catch (Exception ex)
            {
                Main.Logger?.Error($"[ModUI] Ошибка чтения папки Portraits: {ex}");
            }

            if (forceRefresh)
            {
                PreviewCache.Clear();
            }
        }

        /// <summary>
        /// Убирает буферы ввода для персонажей, которых больше нет в текущей партии
        /// (ушли/были распущены), чтобы словарь не рос бессрочно за долгую сессию.
        /// </summary>
        private static void PruneStaleBuffers(HashSet<string> activeKeys)
        {
            if (InputBuffers.Count <= activeKeys.Count) return;

            var stale = InputBuffers.Keys.Where(key => !activeKeys.Contains(key)).ToList();
            foreach (string key in stale)
            {
                InputBuffers.Remove(key);
            }
        }

        private static void DrawPortraitPreview(string portraitId)
        {
            Sprite sprite = GetCachedPreview(portraitId);

            Rect drawRect = GUILayoutUtility.GetRect(PreviewSize, PreviewSize, GUILayout.Width(PreviewSize), GUILayout.Height(PreviewSize));
            GUI.Box(drawRect, GUIContent.none);

            if (sprite == null || sprite.texture == null) return;

            // Спрайт может быть частью атласа — вырезаем именно его прямоугольник
            // через нормализованные UV, а не рисуем всю текстуру целиком.
            Rect uv = new Rect(
                sprite.rect.x / sprite.texture.width,
                sprite.rect.y / sprite.texture.height,
                sprite.rect.width / sprite.texture.width,
                sprite.rect.height / sprite.texture.height);

            GUI.DrawTextureWithTexCoords(drawRect, sprite.texture, uv);
        }

        private static Sprite GetCachedPreview(string portraitId)
        {
            if (string.IsNullOrEmpty(portraitId)) return null;

            if (PreviewCache.TryGetValue(portraitId, out Sprite cached))
            {
                return cached;
            }

            if (PreviewCache.Count > PreviewCacheCap)
            {
                // Простая защита от неограниченного роста, если игрок перебрал много ID.
                PreviewCache.Clear();
            }

            Sprite sprite = PortraitManager.GetSmallPortraitSprite(portraitId);
            PreviewCache[portraitId] = sprite; // кэшируем и null — чтобы не повторять неудачный лукап каждый кадр
            return sprite;
        }
    }
}