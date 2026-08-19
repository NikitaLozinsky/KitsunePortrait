using System;
using System.Reflection;
using HarmonyLib;
using Kingmaker.UI.MVVM._PCView.CharGen.Phases.Portrait;
using Kingmaker.UI.MVVM._PCView.CharGen.Phases.Total;
using Kingmaker.UI.MVVM._PCView.CharGen.Portrait;
using Owlcat.Runtime.UI.Controls.Button;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace KitsunePortrait
{
    /// <summary>
    /// Двойная рамка портретов (Лиса/Человек) для CharGen. Показывается на двух разных
    /// экранах — вкладке выбора портрета (CharGenPortraitPhaseDetailedPCView, поле
    /// m_PortraitView) и итоговом экране сводки персонажа (CharGenTotalPhaseDetailedPCView,
    /// поле m_PortraitChanger) — оба поля одного и того же типа CharGenPortraitPCView, поэтому
    /// вместо жёсткого имени поля ищем его по типу. У каждого экрана — свой независимый
    /// экземпляр панели (общий GameObject нельзя переиспользовать между двумя одновременно
    /// существующими, хоть и не всегда видимыми, вьюхами).
    /// </summary>
    public static class KitsuneCharGenUIPatch
    {
        private static Sprite _dualFrameSprite;
        private static Sprite _frameOriginSprite;

        private static Panel _portraitPhasePanel;
        private static Panel _totalPhasePanel;

        private const string AssetDualFrame = "dual_rama.png";
        private const string AssetFrameOrigin = "rama_origin.png";

        [HarmonyPatch]
        public static class PortraitPhasePatch
        {
            [HarmonyTargetMethod]
            public static MethodBase TargetMethod() => FindBindViewImplementation(typeof(CharGenPortraitPhaseDetailedPCView));

            [HarmonyPostfix]
            public static void Postfix(Component __instance)
            {
                HandleBind(ref _portraitPhasePanel, __instance);
            }
        }

        [HarmonyPatch]
        public static class TotalPhasePatch
        {
            [HarmonyTargetMethod]
            public static MethodBase TargetMethod() => FindBindViewImplementation(typeof(CharGenTotalPhaseDetailedPCView));

            [HarmonyPostfix]
            public static void Postfix(Component __instance)
            {
                HandleBind(ref _totalPhasePanel, __instance);
            }
        }

        private static MethodBase FindBindViewImplementation(Type startType)
        {
            Type currentType = startType;
            while (currentType != null && currentType != typeof(object))
            {
                MethodInfo mi = currentType.GetMethod(
                    "BindViewImplementation",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);

                if (mi != null) return mi;
                currentType = currentType.BaseType;
            }
            return null;
        }

        private static void HandleBind(ref Panel panel, Component hostView)
        {
            try
            {
                if (hostView == null) return;

                EnsureSpritesLoaded();

                Component anchor = FindPortraitViewComponent(hostView);
                if (anchor == null) return;

                // panel — обычная (не-Unity) C#-обёртка, поэтому сама по себе она никогда не
                // становится null от разрушения объектов Unity. Когда CharGen-вьюха, под которой
                // она была ранее создана, закрывается (например, окно создания персонажа после
                // Commit), Unity уничтожает и её дочерний _nativePanel вместе с ней — а наша
                // обёртка Panel остаётся "живой" ссылкой на C#-объект с мёртвым GameObject внутри.
                // Без проверки panel.IsAlive это означало, что при повторном открытии CharGen
                // (например, при Респеке — второй, отдельный визит в этот же экран) панель
                // считалась "уже построенной" и просто молча ничего не делала (Reposition/
                // UpdateState видят Unity-"fake null" у _nativePanel и сразу выходят) — UI мода
                // пропадал целиком. Проверяем именно IsAlive (сверяет Unity-объект через
                // оператор ==), а не сам C#-референс.
                if (panel == null || !panel.IsAlive)
                {
                    panel = new Panel();
                    panel.Build(hostView, _dualFrameSprite, _frameOriginSprite);
                }

                panel.Reposition(anchor);
                panel.UpdateState();
            }
            catch (Exception ex)
            {
                Main.Logger?.Error($"[KitsuneUI] Ошибка создания UI двойной рамки: {ex}");
            }
        }

        private static void EnsureSpritesLoaded()
        {
            if (_dualFrameSprite == null) _dualFrameSprite = Assets.LoadCustomSprite(AssetDualFrame);
            if (_frameOriginSprite == null) _frameOriginSprite = Assets.LoadCustomSprite(AssetFrameOrigin);
        }

        /// <summary>
        /// Ищет на вьюхе (и её базовых классах) поле типа CharGenPortraitPCView — под ним
        /// позиционируется наша панель. И m_PortraitView (вкладка портрета), и m_PortraitChanger
        /// (итоговый экран) — поля именно этого типа, различаются только именем, поэтому ищем
        /// по типу, а не по жёстко заданному имени.
        /// </summary>
        private static Component FindPortraitViewComponent(Component hostView)
        {
            Type currentType = hostView.GetType();
            while (currentType != null && currentType != typeof(object))
            {
                foreach (FieldInfo field in currentType.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
                {
                    if (field.FieldType == typeof(CharGenPortraitPCView))
                    {
                        return field.GetValue(hostView) as Component;
                    }
                }
                currentType = currentType.BaseType;
            }
            return null;
        }

        // ---------- Отключение мода ----------

        /// <summary>
        /// Уничтожает обе панели и сбрасывает кэшированные ссылки на компоненты.
        /// Вызывается при выключении мода (Main.OnToggle(false)), чтобы при повторном
        /// включении без перезапуска игры не оставался "осиротевший" GameObject
        /// с патчами, которые уже сняты Harmony.
        /// </summary>
        public static void Cleanup()
        {
            _portraitPhasePanel?.Destroy();
            _portraitPhasePanel = null;

            _totalPhasePanel?.Destroy();
            _totalPhasePanel = null;
        }

        // ---------- Обновление состояния ----------

        public static void UpdateUIState()
        {
            _portraitPhasePanel?.UpdateState();
            _totalPhasePanel?.UpdateState();
        }

        /// <summary>
        /// Один экземпляр двойной рамки портретов, привязанный к конкретной вьюхе CharGen.
        /// </summary>
        private class Panel
        {
            private GameObject _nativePanel;

            // Unity переопределяет == для UnityEngine.Object — сравнение с null возвращает true
            // и для уничтоженного объекта (не только для настоящего null-референса). Именно
            // поэтому эта проверка (а не сравнение самой C#-обёртки Panel с null) корректно
            // определяет разрушенную панель.
            public bool IsAlive => _nativePanel != null;

            // Слой 1 (Снизу): Портреты
            private Image _foxThumbnail;
            private Image _humanThumbnail;

            // Слой 2 (Посредине): Двойная рамка
            private Image _dualFrameImage;

            // Слой 3 (Сверху): Рамка-указатель
            private Image _foxSelectionFrame;
            private Image _humanSelectionFrame;

            // Слой 4 (Самый верх): Тексты
            private TextMeshProUGUI _foxText;
            private TextMeshProUGUI _humanText;

            // Слой 5: Кнопки кликов
            private OwlcatButton _foxButton;
            private OwlcatButton _humanButton;

            public void Reposition(Component anchor)
            {
                if (_nativePanel == null || anchor == null) return;

                RectTransform anchorRect = anchor.GetComponent<RectTransform>();
                if (anchorRect == null) return;

                if (_nativePanel.transform.parent != anchorRect)
                {
                    _nativePanel.transform.SetParent(anchorRect, false);
                }

                var panelRect = _nativePanel.GetComponent<RectTransform>();
                panelRect.anchorMin = new Vector2(0.5f, 0f);
                panelRect.anchorMax = new Vector2(0.5f, 0f);
                panelRect.pivot = new Vector2(0.5f, 0f);
                panelRect.anchoredPosition = new Vector2(0f, 15f);
                panelRect.sizeDelta = new Vector2(340f, 312f);
            }

            public void Build(Component hostView, Sprite dualFrameSprite, Sprite frameOriginSprite)
            {
                // 1. Главный контейнер панели
                _nativePanel = new GameObject("KitsuneDualFramePanel", typeof(RectTransform));
                _nativePanel.transform.SetParent(hostView.transform, false);

                var layoutElement = _nativePanel.AddComponent<LayoutElement>();
                layoutElement.ignoreLayout = true;

                var panelRect = _nativePanel.GetComponent<RectTransform>();
                panelRect.anchorMin = new Vector2(0.5f, 0f);
                panelRect.anchorMax = new Vector2(0.5f, 0f);
                panelRect.pivot = new Vector2(0.5f, 0f);
                panelRect.sizeDelta = new Vector2(340f, 312f);

                // Получаем шрифт игры
                TMP_FontAsset nativeFont = null;
                Material nativeFontMaterial = null;
                var existingText = hostView.GetComponentInChildren<TextMeshProUGUI>(true);
                if (existingText != null)
                {
                    nativeFont = existingText.font;
                    nativeFontMaterial = existingText.fontSharedMaterial;
                }

                OwlcatButton buttonTemplate = hostView.GetComponentInChildren<OwlcatButton>(true);

                // ------------------------------------------------------------------
                // СЛОЙ 1 (САМЫЙ НИЖНИЙ): Портреты (Thumbnails)
                // ------------------------------------------------------------------
                var portraitsGroup = new GameObject("Layer1_Thumbnails", typeof(RectTransform));
                portraitsGroup.transform.SetParent(_nativePanel.transform, false);
                SetFullStretch(portraitsGroup.GetComponent<RectTransform>());

                _foxThumbnail = CreateThumbnail(portraitsGroup.transform, "FoxThumbnail", new Vector2(-78f, 38f));
                _humanThumbnail = CreateThumbnail(portraitsGroup.transform, "HumanThumbnail", new Vector2(78f, 38f));

                // ------------------------------------------------------------------
                // СЛОЙ 2 (СРЕДНИЙ): Двойная рамка dual_rama.png
                // ------------------------------------------------------------------
                var dualFrameGo = new GameObject("Layer2_DualFrame", typeof(RectTransform), typeof(Image));
                dualFrameGo.transform.SetParent(_nativePanel.transform, false);
                SetFullStretch(dualFrameGo.GetComponent<RectTransform>());

                _dualFrameImage = dualFrameGo.GetComponent<Image>();
                _dualFrameImage.sprite = dualFrameSprite;
                _dualFrameImage.raycastTarget = false;

                // ------------------------------------------------------------------
                // СЛОЙ 3 (ВЕРХНИЙ): Рамки-указатели rama_origin.png
                // ------------------------------------------------------------------
                var framesGroup = new GameObject("Layer3_SelectionFrames", typeof(RectTransform));
                framesGroup.transform.SetParent(_nativePanel.transform, false);
                SetFullStretch(framesGroup.GetComponent<RectTransform>());

                _foxSelectionFrame = CreateSelectionFrame(framesGroup.transform, "FoxSelectionFrame", new Vector2(-78f, 38f), frameOriginSprite);
                _humanSelectionFrame = CreateSelectionFrame(framesGroup.transform, "HumanSelectionFrame", new Vector2(78f, 38f), frameOriginSprite);

                // ------------------------------------------------------------------
                // СЛОЙ 4 (НАД РАМКОЙ): Тексты на плашках
                // ------------------------------------------------------------------
                var labelsGroup = new GameObject("Layer4_Labels", typeof(RectTransform));
                labelsGroup.transform.SetParent(_nativePanel.transform, false);
                SetFullStretch(labelsGroup.GetComponent<RectTransform>());

                _foxText = CreateLabelText(labelsGroup.transform, "FoxText", new Vector2(-78f, -92f), nativeFont, nativeFontMaterial);
                _humanText = CreateLabelText(labelsGroup.transform, "HumanText", new Vector2(78f, -92f), nativeFont, nativeFontMaterial);

                // ------------------------------------------------------------------
                // СЛОЙ 5 (САМЫЙ ВЕРХНИЙ): Кликабельные зоны (OwlcatButton)
                // ------------------------------------------------------------------
                var buttonsGroup = new GameObject("Layer5_Buttons", typeof(RectTransform));
                buttonsGroup.transform.SetParent(_nativePanel.transform, false);
                SetFullStretch(buttonsGroup.GetComponent<RectTransform>());

                _foxButton = CreateClickArea(buttonsGroup.transform, "FoxClickArea", new Vector2(-78f, 0f), buttonTemplate);
                _humanButton = CreateClickArea(buttonsGroup.transform, "HumanClickArea", new Vector2(78f, 0f), buttonTemplate);

                // Обработчики нажатий
                if (_foxButton != null)
                {
                    _foxButton.OnLeftClick.AddListener(() =>
                    {
                        CharGenState.CurrentForm = EditingPortraitForm.Fox;
                        UpdateUIState();
                        Main.Logger?.Log("[KitsuneUI] Выбрана форма: ЛИСА");
                    });
                }

                if (_humanButton != null)
                {
                    _humanButton.OnLeftClick.AddListener(() =>
                    {
                        CharGenState.CurrentForm = EditingPortraitForm.Human;
                        UpdateUIState();
                        Main.Logger?.Log("[KitsuneUI] Выбрана форма: ЧЕЛОВЕК");
                    });
                }
            }

            public void Destroy()
            {
                if (_nativePanel != null)
                {
                    UnityEngine.Object.Destroy(_nativePanel);
                }

                _nativePanel = null;
                _foxThumbnail = null;
                _humanThumbnail = null;
                _dualFrameImage = null;
                _foxSelectionFrame = null;
                _humanSelectionFrame = null;
                _foxText = null;
                _humanText = null;
                _foxButton = null;
                _humanButton = null;
            }

            public void UpdateState()
            {
                if (_nativePanel == null) return;

                bool isKitsune = Main.IsKitsuneSelectedInCharGen;
                _nativePanel.SetActive(isKitsune);

                if (!isKitsune) return;

                bool isHumanActive = CharGenState.CurrentForm == EditingPortraitForm.Human;

                string notSelectedLabel = Localization.Get("Kitsune.Portrait.NotSelected");
                string foxPortraitName = !string.IsNullOrEmpty(Main.SelectedFoxPortrait) ? Main.SelectedFoxPortrait : notSelectedLabel;
                string humanPortraitName = !string.IsNullOrEmpty(Main.TemporaryHumanPortrait) ? Main.TemporaryHumanPortrait : notSelectedLabel;

                // 1. Отображение активной рамки-указателя (Слой 3)
                if (_foxSelectionFrame != null) _foxSelectionFrame.enabled = !isHumanActive;
                if (_humanSelectionFrame != null) _humanSelectionFrame.enabled = isHumanActive;

                // 2. Цвета текста под металл (Слой 4)
                string activeTitleColor = "#FFF0B3";   // Светлое золото
                string activeSubColor = "#E6C280";     // Песочный
                string inactiveTitleColor = "#9E968D"; // Светло-серый
                string inactiveSubColor = "#736C65";   // Приглушенный серый

                string foxLabel = Localization.Get("Kitsune.Form.Fox");
                string humanLabel = Localization.Get("Kitsune.Form.Human");

                if (_foxText != null)
                {
                    _foxText.text = !isHumanActive
                        ? $"<b><color={activeTitleColor}>{foxLabel.ToUpperInvariant()}</color></b>\n<size=11><color={activeSubColor}>{foxPortraitName}</color></size>"
                        : $"<color={inactiveTitleColor}>{foxLabel}</color>\n<size=11><color={inactiveSubColor}>{foxPortraitName}</color></size>";
                }

                if (_humanText != null)
                {
                    _humanText.text = isHumanActive
                        ? $"<b><color={activeTitleColor}>{humanLabel.ToUpperInvariant()}</color></b>\n<size=11><color={activeSubColor}>{humanPortraitName}</color></size>"
                        : $"<color={inactiveTitleColor}>{humanLabel}</color>\n<size=11><color={inactiveSubColor}>{humanPortraitName}</color></size>";
                }

                // 3. Загрузка портретов (Слой 1)
                if (_foxThumbnail != null)
                {
                    Sprite sprite = PortraitManager.GetSmallPortraitSprite(Main.SelectedFoxPortrait);
                    _foxThumbnail.sprite = sprite;
                    _foxThumbnail.enabled = sprite != null;
                }

                if (_humanThumbnail != null)
                {
                    Sprite sprite = PortraitManager.GetSmallPortraitSprite(Main.TemporaryHumanPortrait);
                    _humanThumbnail.sprite = sprite;
                    _humanThumbnail.enabled = sprite != null;
                }
            }

            // ---------- Вспомогательные методы создания компонентов ----------

            private static void SetFullStretch(RectTransform rect)
            {
                rect.anchorMin = Vector2.zero;
                rect.anchorMax = Vector2.one;
                rect.sizeDelta = Vector2.zero;
                rect.anchoredPosition = Vector2.zero;
            }

            private static Image CreateThumbnail(Transform parent, string name, Vector2 pos)
            {
                var go = new GameObject(name, typeof(RectTransform), typeof(Image));
                go.transform.SetParent(parent, false);

                var rect = go.GetComponent<RectTransform>();
                rect.anchorMin = new Vector2(0.5f, 0.5f);
                rect.anchorMax = new Vector2(0.5f, 0.5f);
                rect.pivot = new Vector2(0.5f, 0.5f);
                rect.anchoredPosition = pos;
                // Увеличенный размер: заходит под края двойной рамки без зазоров
                rect.sizeDelta = new Vector2(142f, 182f);

                var img = go.GetComponent<Image>();
                img.preserveAspect = false;
                img.raycastTarget = false;
                img.enabled = false;
                return img;
            }

            private static Image CreateSelectionFrame(Transform parent, string name, Vector2 pos, Sprite frameSprite)
            {
                var go = new GameObject(name, typeof(RectTransform), typeof(Image));
                go.transform.SetParent(parent, false);

                var rect = go.GetComponent<RectTransform>();
                rect.anchorMin = new Vector2(0.5f, 0.5f);
                rect.anchorMax = new Vector2(0.5f, 0.5f);
                rect.pivot = new Vector2(0.5f, 0.5f);
                rect.anchoredPosition = pos;
                // Рамка со стрелками огибает окно поверх dual_rama.png
                rect.sizeDelta = new Vector2(146f, 188f);

                var img = go.GetComponent<Image>();
                img.sprite = frameSprite;
                img.raycastTarget = false;
                img.enabled = false;
                return img;
            }

            private static TextMeshProUGUI CreateLabelText(Transform parent, string name, Vector2 pos, TMP_FontAsset font, Material fontMat)
            {
                var go = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI));
                go.transform.SetParent(parent, false);

                var rect = go.GetComponent<RectTransform>();
                rect.anchorMin = new Vector2(0.5f, 0.5f);
                rect.anchorMax = new Vector2(0.5f, 0.5f);
                rect.pivot = new Vector2(0.5f, 0.5f);
                rect.anchoredPosition = pos;
                rect.sizeDelta = new Vector2(126f, 56f);

                var tmp = go.GetComponent<TextMeshProUGUI>();
                tmp.richText = true;
                tmp.fontSize = 15f;
                tmp.enableWordWrapping = false;
                tmp.overflowMode = TextOverflowModes.Ellipsis; // Обрезаем длинные строки с троеточием
                tmp.alignment = TextAlignmentOptions.Center;
                tmp.raycastTarget = false;

                if (font != null) tmp.font = font;
                if (fontMat != null) tmp.fontSharedMaterial = fontMat;

                return tmp;
            }

            private static OwlcatButton CreateClickArea(Transform parent, string name, Vector2 pos, OwlcatButton template)
            {
                GameObject go;
                OwlcatButton btn;

                if (template != null)
                {
                    go = UnityEngine.Object.Instantiate(template.gameObject, parent, false);
                    go.name = name;
                    btn = go.GetComponent<OwlcatButton>();
                    btn.OnLeftClick.RemoveAllListeners();

                    foreach (Transform child in go.transform)
                    {
                        UnityEngine.Object.Destroy(child.gameObject);
                    }
                }
                else
                {
                    go = new GameObject(name, typeof(RectTransform));
                    go.transform.SetParent(parent, false);
                    btn = go.AddComponent<OwlcatButton>();
                }

                var rect = go.GetComponent<RectTransform>();
                rect.anchorMin = new Vector2(0.5f, 0.5f);
                rect.anchorMax = new Vector2(0.5f, 0.5f);
                rect.pivot = new Vector2(0.5f, 0.5f);
                rect.anchoredPosition = pos;
                rect.sizeDelta = new Vector2(150f, 290f); // Половина всей рамки

                var img = go.GetComponent<Image>();
                if (img == null) img = go.AddComponent<Image>();
                img.color = Color.clear;
                img.raycastTarget = true;

                return btn;
            }
        }
    }
}
