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
        private const int PreviewWidth = 46;
        private const int PreviewHeight = 64;
        private const int PreviewCacheCap = 200;
        private const int BrowserThumbWidth = 100;
        private const int BrowserThumbHeight = 140;
        private const int BrowserCellPadding = 12;
        // Резерв под вертикальный скроллбар галереи — вычитается из измеренной ширины
        // контейнера перед расчётом числа колонок, чтобы сетка не переполнялась по горизонтали.
        private const float BrowserScrollbarAllowance = 24f;
        private const float BrowserHeight = 320f;
        private const int MinPageSize = 4;
        private const int MaxPageSize = 60;

        // Измеренная на предыдущем кадре (Repaint) ширина контейнера галереи — используется
        // для адаптивного числа колонок. Обновляется каждый кадр через width-probe, так что
        // подстраивается под реальную ширину окна UMM в реальном времени (лаг в 1 кадр,
        // незаметный глазу и являющийся стандартным приёмом для IMGUI).
        private static float _measuredBrowserWidth = 400f;

        // Сколько ещё не закэшированных превью можно декодировать за один вызов OnGUI.
        // Ограничивает пиковую нагрузку одного кадра — при большом количестве кастомных
        // портретов на странице они "доливаются" на протяжении нескольких кадров вместо
        // одной фриз-паузы при открытии галереи.
        private const int MaxPreviewDecodesPerFrame = 4;
        private static int _previewDecodesThisFrame;

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

        // Текущая страница галереи (0-based). Размер страницы хранится в Main.Settings и
        // регулируется слайдером прямо в окне мода.
        private static int _browserPage;

        public static void OnGUI(UnityModManager.ModEntry modEntry)
        {
            _previewDecodesThisFrame = 0;

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

                    // Намеренно НЕ пишем на диск здесь (см. Settings.SyncCurrentSaveAndFlush) —
                    // применяется сразу, но в постоянную запись сейва попадёт только когда игра
                    // реально сохранится. Иначе правка "утекала" бы в запись сейва, загруженного
                    // последним, даже если игрок с тех пор ни разу не сохранялся.
                    PortraitManager.UpdatePortrait(unit);
                    Main.Logger?.Log($"[ModUI] Обновлены портреты (в памяти, до следующего сохранения игры) для {unit.CharacterName}. Лиса: '{pair.FoxPortrait}', Человек: '{pair.HumanPortrait}'");
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
                    _browserPage = 0;
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
                DrawPageSizeSlider();

                int pageSize = Mathf.Clamp(Main.Settings.PortraitBrowserPageSize, MinPageSize, MaxPageSize);
                int totalItems = _customPortraitFolders.Count;
                int totalPages = Mathf.Max(1, Mathf.CeilToInt(totalItems / (float)pageSize));
                _browserPage = Mathf.Clamp(_browserPage, 0, totalPages - 1);

                DrawPageNav(totalPages);

                // Width-probe: невидимый элемент на всю доступную ширину контейнера. На событии
                // Repaint (когда Unity уже посчитала финальный layout) его итоговая ширина и есть
                // текущая ширина окна UMM в месте отрисовки галереи — на Layout-событии это
                // значение ещё не финализировано, поэтому читаем только на Repaint.
                Rect widthProbeRect = GUILayoutUtility.GetRect(1f, 1f, GUILayout.ExpandWidth(true), GUILayout.Height(1f));
                if (Event.current.type == EventType.Repaint)
                {
                    _measuredBrowserWidth = widthProbeRect.width;
                }

                int cellWidth = BrowserThumbWidth + BrowserCellPadding;
                float contentWidth = Mathf.Max(cellWidth, _measuredBrowserWidth - BrowserScrollbarAllowance);
                int maxCols = Mathf.Max(1, Mathf.FloorToInt(contentWidth / cellWidth));

                _browserScrollPos = GUILayout.BeginScrollView(_browserScrollPos, GUILayout.Height(BrowserHeight));

                int col = 0;

                int startIndex = _browserPage * pageSize;
                int endIndex = Mathf.Min(startIndex + pageSize, totalItems);

                GUILayout.BeginHorizontal();
                for (int i = startIndex; i < endIndex; i++)
                {
                    string folderName = _customPortraitFolders[i];

                    if (col >= maxCols)
                    {
                        GUILayout.EndHorizontal();
                        GUILayout.BeginHorizontal();
                        col = 0;
                    }

                    GUILayout.BeginVertical(GUI.skin.box, GUILayout.Width(BrowserThumbWidth + BrowserCellPadding));

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

                DrawPageNav(totalPages);
            }

            GUILayout.EndVertical();
        }

        /// <summary>
        /// Слайдер количества портретов на странице (Main.Settings.PortraitBrowserPageSize).
        /// Меняется в реальном времени; сохраняется на диск только когда значение реально
        /// изменилось, чтобы не писать файл настроек на каждый кадр перетаскивания слайдера.
        /// </summary>
        private static void DrawPageSizeSlider()
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(Localization.Get("Kitsune.ModUI.BrowserPageSizeLabel", Main.Settings.PortraitBrowserPageSize), GUILayout.Width(140));
            float sliderValue = GUILayout.HorizontalSlider(Main.Settings.PortraitBrowserPageSize, MinPageSize, MaxPageSize, GUILayout.Width(150));
            int newPageSize = Mathf.RoundToInt(sliderValue);

            if (newPageSize != Main.Settings.PortraitBrowserPageSize)
            {
                Main.Settings.PortraitBrowserPageSize = newPageSize;
                Main.Settings.Save(Main.ModEntry);
                _browserPage = 0;
            }
            GUILayout.EndHorizontal();
        }

        private static void DrawPageNav(int totalPages)
        {
            GUILayout.BeginHorizontal();

            GUI.enabled = _browserPage > 0;
            if (GUILayout.Button(Localization.Get("Kitsune.ModUI.BrowserPrevPage"), GUILayout.Width(30)))
            {
                _browserPage--;
            }
            GUI.enabled = true;

            GUILayout.FlexibleSpace();
            GUILayout.Label(Localization.Get("Kitsune.ModUI.BrowserPageInfo", _browserPage + 1, totalPages));
            GUILayout.FlexibleSpace();

            GUI.enabled = _browserPage < totalPages - 1;
            if (GUILayout.Button(Localization.Get("Kitsune.ModUI.BrowserNextPage"), GUILayout.Width(30)))
            {
                _browserPage++;
            }
            GUI.enabled = true;

            GUILayout.EndHorizontal();
        }

        private static void DrawThumbnail(string portraitId)
        {
            Sprite sprite = GetCachedPreview(portraitId);
            Rect rect = GUILayoutUtility.GetRect(BrowserThumbWidth, BrowserThumbHeight, GUILayout.Width(BrowserThumbWidth), GUILayout.Height(BrowserThumbHeight));
            DrawSpriteFitted(rect, sprite);
        }

        /// <summary>
        /// Рисует спрайт вписанным в прямоугольник с сохранением исходного соотношения сторон
        /// (contain-fit: letterbox/pillarbox по необходимости), а не растянутым по UV на весь
        /// бокс — раньше это "плющило" неквадратные портреты.
        /// </summary>
        private static void DrawSpriteFitted(Rect box, Sprite sprite)
        {
            GUI.Box(box, GUIContent.none);

            if (sprite == null || sprite.texture == null || sprite.rect.height <= 0f || sprite.rect.width <= 0f) return;

            float spriteAspect = sprite.rect.width / sprite.rect.height;
            float boxAspect = box.width / box.height;

            Rect drawRect;
            if (spriteAspect > boxAspect)
            {
                float height = box.width / spriteAspect;
                drawRect = new Rect(box.x, box.y + (box.height - height) * 0.5f, box.width, height);
            }
            else
            {
                float width = box.height * spriteAspect;
                drawRect = new Rect(box.x + (box.width - width) * 0.5f, box.y, width, box.height);
            }

            Rect uv = new Rect(
                sprite.rect.x / sprite.texture.width,
                sprite.rect.y / sprite.texture.height,
                sprite.rect.width / sprite.texture.width,
                sprite.rect.height / sprite.texture.height);

            GUI.DrawTextureWithTexCoords(drawRect, sprite.texture, uv);
        }

        private static void EnsureCustomPortraitFoldersLoaded(bool forceRefresh)
        {
            if (_customPortraitFolders != null && !forceRefresh) return;

            if (forceRefresh)
            {
                _browserPage = 0;
            }

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
            Rect drawRect = GUILayoutUtility.GetRect(PreviewWidth, PreviewHeight, GUILayout.Width(PreviewWidth), GUILayout.Height(PreviewHeight));
            DrawSpriteFitted(drawRect, sprite);
        }

        private static Sprite GetCachedPreview(string portraitId)
        {
            if (string.IsNullOrEmpty(portraitId)) return null;

            if (PreviewCache.TryGetValue(portraitId, out Sprite cached))
            {
                return cached;
            }

            if (_previewDecodesThisFrame >= MaxPreviewDecodesPerFrame)
            {
                // Бюджет декодирования на этот кадр исчерпан — вернём null (плейсхолдер),
                // портрет "доедет" на одном из следующих кадров. Это то, что не даёт окну
                // подвиснуть при первом открытии галереи/страницы с непрогретым кэшем.
                return null;
            }

            if (PreviewCache.Count > PreviewCacheCap)
            {
                // Простая защита от неограниченного роста, если игрок перебрал много ID.
                PreviewCache.Clear();
            }

            Sprite sprite = PortraitManager.GetSmallPortraitSprite(portraitId);
            PreviewCache[portraitId] = sprite; // кэшируем и null — чтобы не повторять неудачный лукап каждый кадр
            _previewDecodesThisFrame++;
            return sprite;
        }
    }
}