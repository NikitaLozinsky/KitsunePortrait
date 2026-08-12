using System;
using System.Reflection;
using HarmonyLib;
using Kingmaker.UI.MVVM._PCView.CharGen.Phases.Portrait;
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
        private static OwlcatToggle _formToggle;      // используется, если нашёлся живой шаблон
        private static Button _fallbackFoxButton;     // используется, если шаблона нет
        private static Button _fallbackHumanButton;
        private static Image _fallbackFoxBg;
        private static Image _fallbackHumanBg;
        private static TextMeshProUGUI _foxText;
        private static TextMeshProUGUI _humanText;
        private static IDisposable _toggleSubscription;

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

                Transform rootTransform = __instance.transform;

                if (_nativePanel == null || _nativePanel.transform.parent != rootTransform)
                {
                    OwlcatToggle toggleTemplate = FindToggleTemplate(__instance);
                    BuildNativeUI(rootTransform, toggleTemplate);
                }

                UpdateUIState();
            }
            catch (Exception ex)
            {
                Main.Logger?.Error($"[KitsuneUI] Ошибка создания нативного UI: {ex}");
            }
        }
        // ReSharper restore InconsistentNaming

        private static OwlcatToggle FindToggleTemplate(CharGenPortraitPhaseDetailedPCView instance)
        {
            // 1. Ищем прямо на экране портрета.
            OwlcatToggle toggle = instance.GetComponentInChildren<OwlcatToggle>(true);
            if (toggle != null) return toggle;

            // 2. Поднимаемся к корню той же иерархии/сцены, где лежит сам экран портрета.
            if (instance.transform.root != null)
            {
                toggle = instance.transform.root.GetComponentInChildren<OwlcatToggle>(true);
                if (toggle != null) return toggle;
            }

            // 3. Экран с тумблером сейчас может быть вообще не заинстанциирован в сцене
            // CharGen (например, живёт только в Настройках, которые сейчас не открыты).
            // Ищем среди ВСЕХ объектов, загруженных в память игры целиком — включая
            // неактивные и лежащие в других, не связанных с CharGen корнях иерархии.
            OwlcatToggle[] allToggles = Resources.FindObjectsOfTypeAll<OwlcatToggle>();
            foreach (var candidate in allToggles)
            {
                if (candidate == null) continue;
                // Отсеиваем "сырые" ассеты, не относящиеся ни к одной загруженной сцене —
                // шаблон нужен именно из реальной сцены, а не из базы ассетов на диске.
                if (!candidate.gameObject.scene.IsValid()) continue;

                return candidate;
            }

            return null;
        }

        private static void BuildNativeUI(Transform parent, OwlcatToggle toggleTemplate)
        {
            if (_nativePanel != null)
            {
                _toggleSubscription?.Dispose();
                _toggleSubscription = null;
                UnityEngine.Object.Destroy(_nativePanel);
            }

            _nativePanel = new GameObject("KitsuneNativeFormSelector", typeof(RectTransform), typeof(HorizontalLayoutGroup));
            _nativePanel.transform.SetParent(parent, false);

            var layoutElement = _nativePanel.AddComponent<LayoutElement>();
            layoutElement.ignoreLayout = true;

            var rect = _nativePanel.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0.5f, 0f);
            rect.anchorMax = new Vector2(0.5f, 0f);
            rect.pivot = new Vector2(0.5f, 0f);
            rect.anchoredPosition = new Vector2(-150f, 250f);
            rect.sizeDelta = new Vector2(520f, 65f);

            var layout = _nativePanel.GetComponent<HorizontalLayoutGroup>();
            layout.spacing = 18f;
            layout.childAlignment = TextAnchor.MiddleCenter;
            layout.childControlWidth = false;
            layout.childControlHeight = false;

            if (toggleTemplate != null)
            {
                BuildWithNativeToggle(toggleTemplate);
                Main.Logger?.Log("[KitsuneUI] Тумблер Лиса/Человек построен из нативного OwlcatToggle.");
            }
            else
            {
                BuildFallbackToggle();
                Main.Logger?.Log("[KitsuneUI] Живой OwlcatToggle нигде не найден — построен собственный переключатель (fallback).");
            }
        }

        private static void BuildWithNativeToggle(OwlcatToggle toggleTemplate)
        {
            _foxText = CreateLabel(_nativePanel.transform, "FoxLabel");
            _formToggle = UnityEngine.Object.Instantiate(toggleTemplate, _nativePanel.transform);
            _formToggle.name = "KitsuneFormToggle";
            _humanText = CreateLabel(_nativePanel.transform, "HumanLabel");

            // Тумблер не должен быть частью радио-группы, в которой он был склонирован
            // (иначе клик по нему мог бы затронуть исходную группу на другом экране).
            _formToggle.Group = null;
            _formToggle.Set(CharGenState.CurrentForm == EditingPortraitForm.Human);

            _toggleSubscription = _formToggle.IsOn.Subscribe(isOn =>
            {
                CharGenState.CurrentForm = isOn ? EditingPortraitForm.Human : EditingPortraitForm.Fox;
                UpdateUIState();
                Main.Logger?.Log($"[KitsuneUI] Активная форма: {(isOn ? "Человек" : "Лиса")}");
            });
        }

        // Самодостаточный переключатель без внешних зависимостей: два кликабельных
        // сегмента на общей подложке. Не претендует на 1:1 копию нативного OwlcatToggle,
        // но ведёт себя как тумблер (виден только один активный вариант) и не зависит
        // от того, загружен ли где-то в памяти игры настоящий шаблон.
        private static void BuildFallbackToggle()
        {
            var trackGO = new GameObject("KitsuneToggleTrack", typeof(RectTransform), typeof(Image));
            trackGO.transform.SetParent(_nativePanel.transform, false);
            var trackRect = trackGO.GetComponent<RectTransform>();
            trackRect.sizeDelta = new Vector2(320f, 50f);
            var trackImage = trackGO.GetComponent<Image>();
            trackImage.color = new Color(0.08f, 0.08f, 0.08f, 0.85f);

            _fallbackFoxButton = CreateToggleHalf(trackGO.transform, "FoxHalf", new Vector2(0f, 0f), new Vector2(0.5f, 1f), out _fallbackFoxBg, out _foxText);
            _fallbackHumanButton = CreateToggleHalf(trackGO.transform, "HumanHalf", new Vector2(0.5f, 0f), new Vector2(1f, 1f), out _fallbackHumanBg, out _humanText);

            _fallbackFoxButton.onClick.AddListener(() =>
            {
                CharGenState.CurrentForm = EditingPortraitForm.Fox;
                UpdateUIState();
                Main.Logger?.Log("[KitsuneUI] Активная форма: Лиса");
            });

            _fallbackHumanButton.onClick.AddListener(() =>
            {
                CharGenState.CurrentForm = EditingPortraitForm.Human;
                UpdateUIState();
                Main.Logger?.Log("[KitsuneUI] Активная форма: Человек");
            });
        }

        private static Button CreateToggleHalf(Transform parent, string name, Vector2 anchorMin, Vector2 anchorMax, out Image background, out TextMeshProUGUI label)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button));
            go.transform.SetParent(parent, false);

            var rect = go.GetComponent<RectTransform>();
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;

            background = go.GetComponent<Image>();
            background.color = new Color(0f, 0f, 0f, 0f);

            var button = go.GetComponent<Button>();
            button.targetGraphic = background;
            button.transition = Selectable.Transition.ColorTint;

            var textGO = new GameObject("Label", typeof(RectTransform));
            textGO.transform.SetParent(go.transform, false);
            var textRect = textGO.GetComponent<RectTransform>();
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = Vector2.zero;
            textRect.offsetMax = Vector2.zero;

            label = textGO.AddComponent<TextMeshProUGUI>();
            label.richText = true;
            label.fontSize = 18f;
            label.alignment = TextAlignmentOptions.Center;

            return button;
        }

        private static TextMeshProUGUI CreateLabel(Transform parent, string name)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);

            var rect = go.GetComponent<RectTransform>();
            rect.sizeDelta = new Vector2(190f, 60f);

            var text = go.AddComponent<TextMeshProUGUI>();
            text.richText = true;
            text.fontSize = 18f;
            text.alignment = TextAlignmentOptions.Center;

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

            if (_foxText != null)
            {
                _foxText.text = !isHumanActive
                    ? $"<b><color=#E2B053>🦊 ЛИСА</color></b>\n<size=11><color=#FFFFFF>{foxPortraitName}</color></size>"
                    : $"<color=#8C8C8C>🦊 Лиса</color>\n<size=11><color=#909090>{foxPortraitName}</color></size>";
            }

            if (_humanText != null)
            {
                _humanText.text = isHumanActive
                    ? $"<b><color=#E2B053>👤 ЧЕЛОВЕК</color></b>\n<size=11><color=#FFFFFF>{humanPortraitName}</color></size>"
                    : $"<color=#8C8C8C>👤 Человек</color>\n<size=11><color=#909090>{humanPortraitName}</color></size>";
            }

            // Подсветка активного сегмента — только для fallback-варианта (у нативного
            // OwlcatToggle подсветка своя, встроенная в его собственный визуал).
            if (_fallbackFoxBg != null)
            {
                _fallbackFoxBg.color = !isHumanActive
                    ? new Color(0.89f, 0.69f, 0.33f, 0.35f)
                    : new Color(0f, 0f, 0f, 0f);
            }

            if (_fallbackHumanBg != null)
            {
                _fallbackHumanBg.color = isHumanActive
                    ? new Color(0.89f, 0.69f, 0.33f, 0.35f)
                    : new Color(0f, 0f, 0f, 0f);
            }
        }
    }
}