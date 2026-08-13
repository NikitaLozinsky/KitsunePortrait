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
        private static OwlcatToggle _formToggle;

        // Элементы стилизованного тумблера
        private static OwlcatButton _foxButton;
        private static OwlcatButton _humanButton;
        private static Image _foxBgImage;
        private static Image _humanBgImage;
        private static TextMeshProUGUI _foxText;
        private static TextMeshProUGUI _humanText;

        private static IDisposable _toggleSubscription;

        // Цветовая палитра Pathfinder: Wrath of the Righteous
        private static readonly Color ColorGoldActive = new Color(0.89f, 0.69f, 0.33f, 1f);       // #E2B053 (Золото)
        private static readonly Color ColorInactiveText = new Color(0.55f, 0.55f, 0.55f, 0.8f);   // Тёмно-серый текст
        private static readonly Color ColorActiveBg = new Color(0.35f, 0.25f, 0.12f, 0.75f);      // Золотисто-тёмная подложка
        private static readonly Color ColorInactiveBg = new Color(0.05f, 0.05f, 0.05f, 0.6f);     // Тёмный пергамент/металл
        private static readonly Color ColorFrameBorder = new Color(0.45f, 0.36f, 0.22f, 0.9f);    // Медная рамка

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
                    Main.Logger?.Warning("[KitsuneUI] Не удалось получить m_PortraitView через AccessTools — " +
                                          "проверьте, что имя поля совпадает с реальным в текущей версии игры.");
                }
                return;
            }

            RectTransform portraitRect = portraitViewComponent.GetComponent<RectTransform>();
            if (portraitRect == null) return;

            // Осторожно: если у m_PortraitView (или чего-то между ним и Canvas) есть Mask/
            // RectMask2D, всё что мы повесим ВНУТРИ него и что выходит за его собственные
            // границы (а наша панель именно так и висит — ниже нижнего края), будет обрезано
            // и станет невидимым. Предупреждаем один раз, если такой компонент нашёлся.
            if (!_maskWarned && (portraitViewComponent.GetComponent<RectMask2D>() != null || portraitViewComponent.GetComponent<Mask>() != null))
            {
                _maskWarned = true;
                Main.Logger?.Warning("[KitsuneUI] На m_PortraitView найден Mask/RectMask2D — панель, вложенная в него, " +
                                      "может обрезаться по его границам. Если панель не видна, скажите — переключим " +
                                      "позиционирование на 'сосед по иерархии' вместо 'ребёнок'.");
            }

            if (_nativePanel.transform.parent != portraitRect)
            {
                _nativePanel.transform.SetParent(portraitRect, false);
            }

            var panelRect = _nativePanel.GetComponent<RectTransform>();
            panelRect.anchorMin = new Vector2(0.5f, 0f);
            panelRect.anchorMax = new Vector2(0.5f, 0f);
            panelRect.pivot = new Vector2(0.5f, 1f); // верхний край панели = нижний край портрета
            panelRect.anchoredPosition = new Vector2(0f, -10f);
        }

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

        private static void BuildUI(CharGenPortraitPhaseDetailedPCView instance)
        {
            if (_nativePanel != null)
            {
                _toggleSubscription?.Dispose();
                _toggleSubscription = null;
                UnityEngine.Object.Destroy(_nativePanel);
            }

            // Создаем корневой контейнер
            _nativePanel = new GameObject("KitsuneNativeFormSelector", typeof(RectTransform), typeof(HorizontalLayoutGroup));
            _nativePanel.transform.SetParent(instance.transform, false);

            var layoutElement = _nativePanel.AddComponent<LayoutElement>();
            layoutElement.ignoreLayout = true;

            var rect = _nativePanel.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 1f); // верхний край панели = точка привязки, растёт вниз
            rect.sizeDelta = new Vector2(540f, 65f);

            var layout = _nativePanel.GetComponent<HorizontalLayoutGroup>();
            layout.spacing = 16f;
            layout.childAlignment = TextAnchor.MiddleCenter;
            layout.childControlWidth = false;
            layout.childControlHeight = false;

            // Пробуем найти живой OwlcatToggle
            OwlcatToggle toggleTemplate = FindToggleTemplate(instance);
            if (toggleTemplate != null)
            {
                BuildWithNativeToggle(toggleTemplate);
                Main.Logger?.Log("[KitsuneUI] Построен нативный OwlcatToggle.");
            }
            else
            {
                // Если OwlcatToggle нет — строим стилизованную двухсегментную панель из ассетов сцены
                BuildPathfinderSegmentedToggle(instance);
                Main.Logger?.Log("[KitsuneUI] Построен стилизованный сегментированный переключатель Pathfinder.");
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

        private static void BuildWithNativeToggle(OwlcatToggle toggleTemplate)
        {
            _foxText = CreateLabel(_nativePanel.transform, "FoxLabel", null, null);
            _formToggle = UnityEngine.Object.Instantiate(toggleTemplate, _nativePanel.transform);
            _formToggle.name = "KitsuneFormToggle";
            _humanText = CreateLabel(_nativePanel.transform, "HumanLabel", null, null);

            _formToggle.Group = null;
            _formToggle.Set(CharGenState.CurrentForm == EditingPortraitForm.Human);

            _toggleSubscription = _formToggle.IsOn.Subscribe(isOn =>
            {
                CharGenState.CurrentForm = isOn ? EditingPortraitForm.Human : EditingPortraitForm.Fox;
                UpdateUIState();
            });
        }

        private static void BuildPathfinderSegmentedToggle(CharGenPortraitPhaseDetailedPCView instance)
        {
            // 1. Извлекаем родной шрифт и родной спрайт кнопки с текущего экрана игры
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

            // 2. Ищем шаблон OwlcatButton для клонирования обработчиков клика и звуков
            OwlcatButton buttonTemplate = instance.GetComponentInChildren<OwlcatButton>(true);

            // 3. Создаем общую внешнюю рамку-трек
            var trackGO = new GameObject("KitsuneToggleTrack", typeof(RectTransform), typeof(Image));
            trackGO.transform.SetParent(_nativePanel.transform, false);
            
            var trackRect = trackGO.GetComponent<RectTransform>();
            trackRect.sizeDelta = new Vector2(360f, 52f);

            var trackImage = trackGO.GetComponent<Image>();
            if (nativeButtonSprite != null)
            {
                trackImage.sprite = nativeButtonSprite;
                trackImage.type = Image.Type.Sliced;
            }
            trackImage.color = ColorFrameBorder;

            // 4. Создаем два плотно прилегающих сегмента (Лиса / Человек)
            _foxButton = CreateSegmentButton(
                trackGO.transform, 
                "FoxSegment", 
                new Vector2(0f, 0f), 
                new Vector2(0.5f, 1f), 
                new Vector4(3f, 3f, 1.5f, 3f),
                buttonTemplate, 
                nativeButtonSprite, 
                nativeFont, 
                nativeFontMaterial, 
                out _foxBgImage, 
                out _foxText
            );

            _humanButton = CreateSegmentButton(
                trackGO.transform, 
                "HumanSegment", 
                new Vector2(0.5f, 0f), 
                new Vector2(1f, 1f), 
                new Vector4(1.5f, 3f, 3f, 3f),
                buttonTemplate, 
                nativeButtonSprite, 
                nativeFont, 
                nativeFontMaterial, 
                out _humanBgImage, 
                out _humanText
            );

            // Настройка событий клика
            Action selectFox = () =>
            {
                CharGenState.CurrentForm = EditingPortraitForm.Fox;
                UpdateUIState();
                Main.Logger?.Log("[KitsuneUI] Выбрана форма: Лиса");
            };

            Action selectHuman = () =>
            {
                CharGenState.CurrentForm = EditingPortraitForm.Human;
                UpdateUIState();
                Main.Logger?.Log("[KitsuneUI] Выбрана форма: Человек");
            };

            if (_foxButton != null) _foxButton.OnLeftClick.AddListener(() => selectFox());
            if (_humanButton != null) _humanButton.OnLeftClick.AddListener(() => selectHuman());
        }

        private static OwlcatButton CreateSegmentButton(
            Transform parent, 
            string name, 
            Vector2 anchorMin, 
            Vector2 anchorMax, 
            Vector4 padding,
            OwlcatButton template, 
            Sprite bgSprite, 
            TMP_FontAsset font, 
            Material fontMaterial, 
            out Image bgImage, 
            out TextMeshProUGUI label)
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

            // Удаляем старые дочерние тексты, если они склонировались из шаблона
            foreach (Transform child in go.transform)
            {
                UnityEngine.Object.Destroy(child.gameObject);
            }

            label = CreateLabel(go.transform, "Label", font, fontMaterial);
            var labelRect = label.GetComponent<RectTransform>();
            labelRect.anchorMin = Vector2.zero;
            labelRect.anchorMax = Vector2.one;
            labelRect.offsetMin = Vector2.zero;
            labelRect.offsetMax = Vector2.zero;

            return button;
        }

        private static TextMeshProUGUI CreateLabel(Transform parent, string name, TMP_FontAsset font, Material fontMaterial)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);

            var rect = go.GetComponent<RectTransform>();
            rect.sizeDelta = new Vector2(190f, 60f);

            var text = go.AddComponent<TextMeshProUGUI>();
            text.richText = true;
            text.fontSize = 17f;
            text.alignment = TextAlignmentOptions.Center;

            if (font != null) text.font = font;
            if (fontMaterial != null) text.fontSharedMaterial = fontMaterial;

            return text;
        }

        public static void UpdateUIState()
        {
            if (_nativePanel == null) return;

            bool isKitsune = Main.IsKitsuneSelectedInCharGen;
            _nativePanel.SetActive(isKitsune);

            if (!isKitsune) return;

            bool isHumanActive = CharGenState.CurrentForm == EditingPortraitForm.Human;

            string foxPortraitName = !string.IsNullOrEmpty(Main.SelectedFoxPortrait) ? Main.SelectedFoxPortrait : "не выбран";
            string humanPortraitName = !string.IsNullOrEmpty(Main.TemporaryHumanPortrait) ? Main.TemporaryHumanPortrait : "не выбран";

            // Обновление форматирования текста
            if (_foxText != null)
            {
                _foxText.text = !isHumanActive
                    ? $"<b><color=#E2B053>🦊 ЛИСА</color></b>\n<size=11><color=#F0E6D2>{foxPortraitName}</color></size>"
                    : $"<color=#8A8A8A>🦊 Лиса</color>\n<size=11><color=#666666>{foxPortraitName}</color></size>";
            }

            if (_humanText != null)
            {
                _humanText.text = isHumanActive
                    ? $"<b><color=#E2B053>👤 ЧЕЛОВЕК</color></b>\n<size=11><color=#F0E6D2>{humanPortraitName}</color></size>"
                    : $"<color=#8A8A8A>👤 Человек</color>\n<size=11><color=#666666>{humanPortraitName}</color></size>";
            }

            // Динамическая подсветка сегментов тумблера
            if (_foxBgImage != null)
            {
                _foxBgImage.color = !isHumanActive ? ColorActiveBg : ColorInactiveBg;
            }

            if (_humanBgImage != null)
            {
                _humanBgImage.color = isHumanActive ? ColorActiveBg : ColorInactiveBg;
            }
        }
    }
}