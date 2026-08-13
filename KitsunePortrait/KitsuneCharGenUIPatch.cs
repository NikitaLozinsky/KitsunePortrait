using System;
using System.Reflection;
using HarmonyLib;
using Kingmaker.UI.MVVM._PCView.CharGen.Phases.Portrait;
using Owlcat.Runtime.UI.Controls.Button;
using Owlcat.Runtime.UI.Controls.Toggles;
using TMPro;
using UniRx;
using UnityEngine;
using UnityEngine.UI;

namespace KitsunePortrait
{
    [HarmonyPatch]
    public static class KitsuneCharGenUIPatch
    {
        private static GameObject _nativePanel;

        // Нативный путь (когда где-то в памяти нашёлся живой OwlcatToggle)
        private static OwlcatToggle _formToggle;
        private static TextMeshProUGUI _toggleCenterIcon; // тот самый "1" в центре тумблера — заменяем на 🦊/👤

        // Общие для обоих путей: подписи по бокам с тёмной подложкой (как "НАЗАД"/"ДАЛЕЕ")
        private static Image _foxBgImage;
        private static Image _humanBgImage;
        private static Image _foxThumbnail;
        private static Image _humanThumbnail;
        private static TextMeshProUGUI _foxText;
        private static TextMeshProUGUI _humanText;

        // Fallback-путь (сегментированные кнопки, когда OwlcatToggle нигде не нашёлся)
        private static OwlcatButton _foxButton;
        private static OwlcatButton _humanButton;

        private static IDisposable _toggleSubscription;

        // Цветовая палитра Pathfinder: Wrath of the Righteous
        private static readonly Color ColorActiveBg = new Color(0.16f, 0.12f, 0.06f, 0.92f);      // Глубокий тёмный фон активного сегмента
        private static readonly Color ColorInactiveBg = new Color(0.05f, 0.05f, 0.05f, 0.55f);    // Полупрозрачный тёмный фон неактивного
        private static readonly Color ColorFrameBorder = new Color(0.45f, 0.36f, 0.22f, 0.9f);    // Медная рамка (для трека fallback-тумблера)

        [HarmonyTargetMethod]
        public static MethodBase TargetMethod()
        {
            Type currentType = typeof(CharGenPortraitPhaseDetailedPCView);
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

        // ReSharper disable InconsistentNaming
        [HarmonyPostfix]
        public static void Postfix(CharGenPortraitPhaseDetailedPCView __instance)
        {
            try
            {
                if (__instance == null) return;

                if (_nativePanel == null)
                {
                    BuildUI(__instance);
                }

                RepositionUnderPortrait(__instance);
                UpdateUIState();
            }
            catch (Exception ex)
            {
                Main.Logger?.Error($"[KitsuneUI] Ошибка создания нативного UI: {ex}");
            }
        }
        // ReSharper restore InconsistentNaming

        // ---------- Позиционирование внутри m_PortraitView ----------

        private static Component GetPortraitViewComponent(CharGenPortraitPhaseDetailedPCView instance)
        {
            return AccessTools.Field(typeof(CharGenPortraitPhaseDetailedPCView), "m_PortraitView")
                               ?.GetValue(instance) as Component;
        }

        private static bool _portraitViewMissingWarned;
        private static bool _maskWarned;

        private static void RepositionUnderPortrait(CharGenPortraitPhaseDetailedPCView instance)
        {
            if (_nativePanel == null || instance == null) return;

            Component portraitViewComponent = GetPortraitViewComponent(instance);
            if (portraitViewComponent == null)
            {
                if (!_portraitViewMissingWarned)
                {
                    _portraitViewMissingWarned = true;
                    Main.Logger?.Warning("[KitsuneUI] Не удалось получить m_PortraitView через AccessTools.");
                }
                return;
            }

            RectTransform portraitRect = portraitViewComponent.GetComponent<RectTransform>();
            if (portraitRect == null) return;

            if (!_maskWarned && (portraitViewComponent.GetComponent<RectMask2D>() != null || portraitViewComponent.GetComponent<Mask>() != null))
            {
                _maskWarned = true;
                Main.Logger?.Warning("[KitsuneUI] На m_PortraitView найден Mask/RectMask2D — панель может обрезаться.");
            }

            if (_nativePanel.transform.parent != portraitRect)
            {
                _nativePanel.transform.SetParent(portraitRect, false);
            }

            var panelRect = _nativePanel.GetComponent<RectTransform>();
            panelRect.anchorMin = new Vector2(0.5f, 0f);
            panelRect.anchorMax = new Vector2(0.5f, 0f);
            panelRect.pivot = new Vector2(0.5f, 0f); // нижний край панели = нижний край портрета, растёт вверх
            panelRect.anchoredPosition = new Vector2(0f, 22f); // заходит внутрь арта, не задевая внешнюю рамку
        }

        // ---------- Построение UI ----------

        private static void BuildUI(CharGenPortraitPhaseDetailedPCView instance)
        {
            if (_nativePanel != null)
            {
                _toggleSubscription?.Dispose();
                _toggleSubscription = null;
                UnityEngine.Object.Destroy(_nativePanel);
            }

            _nativePanel = new GameObject("KitsuneNativeFormSelector", typeof(RectTransform), typeof(HorizontalLayoutGroup));
            _nativePanel.transform.SetParent(instance.transform, false);

            var layoutElement = _nativePanel.AddComponent<LayoutElement>();
            layoutElement.ignoreLayout = true;

            var rect = _nativePanel.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0.5f, 0f);
            rect.anchorMax = new Vector2(0.5f, 0f);
            rect.pivot = new Vector2(0.5f, 0f); // сразу перезаписывается в RepositionUnderPortrait
            rect.sizeDelta = new Vector2(540f, 150f);

            var layout = _nativePanel.GetComponent<HorizontalLayoutGroup>();
            layout.spacing = 12f;
            layout.childAlignment = TextAnchor.MiddleCenter;
            layout.childControlWidth = false;
            layout.childControlHeight = false;

            // Родной шрифт/материал и родной спрайт кнопки — нужны обоим путям, чтобы
            // подписи и подложки выглядели так же, как остальной UI этого экрана.
            TMP_FontAsset nativeFont = null;
            Material nativeFontMaterial = null;
            Sprite nativeButtonSprite = null;

            var existingText = instance.GetComponentInChildren<TextMeshProUGUI>(true);
            if (existingText != null)
            {
                nativeFont = existingText.font;
                nativeFontMaterial = existingText.fontSharedMaterial;
            }

            var existingImage = instance.GetComponentInChildren<Image>(true);
            if (existingImage != null && existingImage.sprite != null)
            {
                nativeButtonSprite = existingImage.sprite;
            }

            OwlcatToggle toggleTemplate = FindToggleTemplate(instance);
            if (toggleTemplate != null)
            {
                BuildWithNativeToggle(toggleTemplate, instance, nativeButtonSprite, nativeFont, nativeFontMaterial);
                Main.Logger?.Log("[KitsuneUI] Построен нативный OwlcatToggle.");
            }
            else
            {
                BuildPathfinderSegmentedToggle(instance, nativeButtonSprite, nativeFont, nativeFontMaterial);
                Main.Logger?.Log("[KitsuneUI] Живой OwlcatToggle нигде не найден — построен fallback-переключатель.");
            }
        }

        private static OwlcatToggle FindToggleTemplate(CharGenPortraitPhaseDetailedPCView instance)
        {
            OwlcatToggle toggle = instance.GetComponentInChildren<OwlcatToggle>(true);
            if (toggle != null) return toggle;

            if (instance.transform.root != null)
            {
                toggle = instance.transform.root.GetComponentInChildren<OwlcatToggle>(true);
                if (toggle != null) return toggle;
            }

            var allToggles = Resources.FindObjectsOfTypeAll<OwlcatToggle>();
            foreach (var candidate in allToggles)
            {
                if (candidate == null || !candidate.gameObject.scene.IsValid()) continue;
                return candidate;
            }

            return null;
        }

        // ---------- Путь 1: нативный OwlcatToggle ----------

        private static void BuildWithNativeToggle(OwlcatToggle toggleTemplate, CharGenPortraitPhaseDetailedPCView instance,
            Sprite nativeSprite, TMP_FontAsset font, Material fontMaterial)
        {
            // Подписи по бокам — с тёмной подложкой и миниатюрой портрета сверху.
            CreateLabelPanel(_nativePanel.transform, "FoxLabel", nativeSprite, font, fontMaterial, out _foxBgImage, out _foxThumbnail, out _foxText);

            _formToggle = UnityEngine.Object.Instantiate(toggleTemplate, _nativePanel.transform);
            _formToggle.name = "KitsuneFormToggle";
            _formToggle.Group = null;

            // В центре клонированного тумблера обычно лежит бейдж с цифрой (например,
            // счётчик из экрана, откуда мы его скопировали) — превращаем его в
            // динамический значок текущей формы.
            _toggleCenterIcon = _formToggle.GetComponentInChildren<TextMeshProUGUI>(true);

            CreateLabelPanel(_nativePanel.transform, "HumanLabel", nativeSprite, font, fontMaterial, out _humanBgImage, out _humanThumbnail, out _humanText);

            _formToggle.Set(CharGenState.CurrentForm == EditingPortraitForm.Human);

            _toggleSubscription = _formToggle.IsOn.Subscribe(isOn =>
            {
                CharGenState.CurrentForm = isOn ? EditingPortraitForm.Human : EditingPortraitForm.Fox;
                UpdateUIState();
                Main.Logger?.Log($"[KitsuneUI] Активная форма: {(isOn ? "Человек" : "Лиса")}");
            });
        }

        // Подпись формы с тёмной подложкой (стиль нижних кнопок навигации) + миниатюра
        // выбранного портрета сверху.
        private static void CreateLabelPanel(Transform parent, string name, Sprite bgSprite,
            TMP_FontAsset font, Material fontMaterial, out Image bgImage, out Image thumbnail, out TextMeshProUGUI label)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);

            var rect = go.GetComponent<RectTransform>();
            rect.sizeDelta = new Vector2(190f, 140f);

            bgImage = go.GetComponent<Image>();
            if (bgSprite != null)
            {
                bgImage.sprite = bgSprite;
                bgImage.type = Image.Type.Sliced;
            }

            thumbnail = CreateThumbnail(go.transform);

            label = CreateLabel(go.transform, "Label", font, fontMaterial);
            var labelRect = label.GetComponent<RectTransform>();
            labelRect.anchorMin = new Vector2(0f, 0f);
            labelRect.anchorMax = new Vector2(1f, 0f);
            labelRect.pivot = new Vector2(0.5f, 0f);
            labelRect.sizeDelta = new Vector2(0f, 54f);
            labelRect.anchoredPosition = new Vector2(0f, 6f);
        }

        // Квадратная миниатюра портрета, прижатая к верху родительской плашки.
        private static Image CreateThumbnail(Transform parent)
        {
            var go = new GameObject("Thumbnail", typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);

            var rect = go.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0.5f, 1f);
            rect.anchorMax = new Vector2(0.5f, 1f);
            rect.pivot = new Vector2(0.5f, 1f);
            rect.anchoredPosition = new Vector2(0f, -8f);
            rect.sizeDelta = new Vector2(68f, 68f);

            var image = go.GetComponent<Image>();
            image.preserveAspect = true;
            image.enabled = false; // включаем, когда появится реальный спрайт

            return image;
        }

        // ---------- Путь 2: fallback (сегментированные кнопки) ----------

        private static void BuildPathfinderSegmentedToggle(CharGenPortraitPhaseDetailedPCView instance,
            Sprite nativeButtonSprite, TMP_FontAsset nativeFont, Material nativeFontMaterial)
        {
            OwlcatButton buttonTemplate = instance.GetComponentInChildren<OwlcatButton>(true);

            var trackGO = new GameObject("KitsuneToggleTrack", typeof(RectTransform), typeof(Image));
            trackGO.transform.SetParent(_nativePanel.transform, false);

            var trackRect = trackGO.GetComponent<RectTransform>();
            trackRect.sizeDelta = new Vector2(400f, 140f);

            var trackImage = trackGO.GetComponent<Image>();
            if (nativeButtonSprite != null)
            {
                trackImage.sprite = nativeButtonSprite;
                trackImage.type = Image.Type.Sliced;
            }
            trackImage.color = ColorFrameBorder;

            _foxButton = CreateSegmentButton(
                trackGO.transform, "FoxSegment",
                new Vector2(0f, 0f), new Vector2(0.5f, 1f), new Vector4(3f, 3f, 1.5f, 3f),
                buttonTemplate, nativeButtonSprite, nativeFont, nativeFontMaterial,
                out _foxBgImage, out _foxThumbnail, out _foxText);

            _humanButton = CreateSegmentButton(
                trackGO.transform, "HumanSegment",
                new Vector2(0.5f, 0f), new Vector2(1f, 1f), new Vector4(1.5f, 3f, 3f, 3f),
                buttonTemplate, nativeButtonSprite, nativeFont, nativeFontMaterial,
                out _humanBgImage, out _humanThumbnail, out _humanText);

            if (_foxButton != null)
            {
                _foxButton.OnLeftClick.AddListener(() =>
                {
                    CharGenState.CurrentForm = EditingPortraitForm.Fox;
                    UpdateUIState();
                    Main.Logger?.Log("[KitsuneUI] Выбрана форма: Лиса");
                });
            }

            if (_humanButton != null)
            {
                _humanButton.OnLeftClick.AddListener(() =>
                {
                    CharGenState.CurrentForm = EditingPortraitForm.Human;
                    UpdateUIState();
                    Main.Logger?.Log("[KitsuneUI] Выбрана форма: Человек");
                });
            }
        }

        private static OwlcatButton CreateSegmentButton(
            Transform parent, string name, Vector2 anchorMin, Vector2 anchorMax, Vector4 padding,
            OwlcatButton template, Sprite bgSprite, TMP_FontAsset font, Material fontMaterial,
            out Image bgImage, out Image thumbnail, out TextMeshProUGUI label)
        {
            GameObject go;
            OwlcatButton button;

            if (template != null)
            {
                go = UnityEngine.Object.Instantiate(template.gameObject, parent, false);
                go.name = name;
                button = go.GetComponent<OwlcatButton>();
                button.OnLeftClick.RemoveAllListeners();
            }
            else
            {
                go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(OwlcatButton));
                go.transform.SetParent(parent, false);
                button = go.GetComponent<OwlcatButton>();
            }

            var rect = go.GetComponent<RectTransform>();
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.offsetMin = new Vector2(padding.x, padding.y);
            rect.offsetMax = new Vector2(-padding.z, -padding.w);

            bgImage = go.GetComponent<Image>();
            if (bgImage == null) bgImage = go.AddComponent<Image>();

            if (bgSprite != null)
            {
                bgImage.sprite = bgSprite;
                bgImage.type = Image.Type.Sliced;
            }

            foreach (Transform child in go.transform)
            {
                UnityEngine.Object.Destroy(child.gameObject);
            }

            thumbnail = CreateThumbnail(go.transform);

            label = CreateLabel(go.transform, "Label", font, fontMaterial);
            var labelRect = label.GetComponent<RectTransform>();
            labelRect.anchorMin = new Vector2(0f, 0f);
            labelRect.anchorMax = new Vector2(1f, 0f);
            labelRect.pivot = new Vector2(0.5f, 0f);
            labelRect.sizeDelta = new Vector2(0f, 54f);
            labelRect.anchoredPosition = new Vector2(0f, 6f);

            return button;
        }

        private static TextMeshProUGUI CreateLabel(Transform parent, string name, TMP_FontAsset font, Material fontMaterial)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);

            var rect = go.GetComponent<RectTransform>();
            rect.sizeDelta = new Vector2(190f, 58f);

            var text = go.AddComponent<TextMeshProUGUI>();
            text.richText = true;
            text.fontSize = 16f;
            text.enableWordWrapping = false;
            text.alignment = TextAlignmentOptions.Center;

            if (font != null) text.font = font;
            if (fontMaterial != null) text.fontSharedMaterial = fontMaterial;

            return text;
        }

        // ---------- Обновление состояния ----------

        public static void UpdateUIState()
        {
            if (_nativePanel == null) return;

            bool isKitsune = Main.IsKitsuneSelectedInCharGen;
            _nativePanel.SetActive(isKitsune);

            if (!isKitsune) return;

            bool isHumanActive = CharGenState.CurrentForm == EditingPortraitForm.Human;

            string foxPortraitName = !string.IsNullOrEmpty(Main.SelectedFoxPortrait) ? Main.SelectedFoxPortrait : "не выбран";
            string humanPortraitName = !string.IsNullOrEmpty(Main.TemporaryHumanPortrait) ? Main.TemporaryHumanPortrait : "не выбран";

            // Активная форма: яркий золотой заголовок + чёткий текст.
            // Неактивная: приглушённый серый, без выделения.
            // Без эмодзи — по той же причине, что и у центральной иконки тумблера.
            if (_foxText != null)
            {
                _foxText.text = !isHumanActive
                    ? $"<b><color=#E2B053>ЛИСА</color></b>\n<size=11><color=#F0E6D2>{foxPortraitName}</color></size>"
                    : $"<color=#8A8A8A>Лиса</color>\n<size=11><color=#6E6E6E>{foxPortraitName}</color></size>";
            }

            if (_humanText != null)
            {
                _humanText.text = isHumanActive
                    ? $"<b><color=#E2B053>ЧЕЛОВЕК</color></b>\n<size=11><color=#F0E6D2>{humanPortraitName}</color></size>"
                    : $"<color=#8A8A8A>Человек</color>\n<size=11><color=#6E6E6E>{humanPortraitName}</color></size>";
            }

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

            if (_foxBgImage != null)
            {
                _foxBgImage.color = !isHumanActive ? ColorActiveBg : ColorInactiveBg;
            }

            if (_humanBgImage != null)
            {
                _humanBgImage.color = isHumanActive ? ColorActiveBg : ColorInactiveBg;
            }

            // Центральный значок нативного тумблера (если этот путь активен).
            // Без эмодзи: родной шрифт игры почти наверняка не содержит 🦊/👤 в своём
            // атласе символов — TMP не рисует то, чего нет в конкретном шрифте-ассете.
            if (_toggleCenterIcon != null)
            {
                _toggleCenterIcon.text = isHumanActive ? "Ч" : "Л";
            }
        }
    }
}