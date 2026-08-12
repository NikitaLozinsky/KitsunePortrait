using System;
using System.Reflection;
using HarmonyLib;
using Kingmaker.UI.MVVM._PCView.CharGen.Phases.Portrait;
using Owlcat.Runtime.UI.Controls.Button;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace KitsunePortrait
{
    [HarmonyPatch]
    public static class KitsuneCharGenUIPatch
    {
        private static GameObject _nativePanel;
        private static OwlcatButton _foxButton;
        private static OwlcatButton _humanButton;
        private static TextMeshProUGUI _foxText;
        private static TextMeshProUGUI _humanText;

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

                Transform parentTransform = __instance.transform.Find("ContentWrapper");
                if (parentTransform == null) parentTransform = __instance.transform;

                OwlcatButton templateButton = __instance.GetComponentInChildren<OwlcatButton>(true);
                if (templateButton == null) return;

                if (_nativePanel == null || _nativePanel.transform.parent != parentTransform)
                {
                    BuildNativeUI(parentTransform, templateButton);
                }

                UpdateUIState();
            }
            catch (Exception ex)
            {
                Main.Logger?.Error($"[KitsuneUI] Ошибка создания нативного UI: {ex}");
            }
        }
        // ReSharper restore InconsistentNaming

        private static void BuildNativeUI(Transform parent, OwlcatButton template)
        {
            if (_nativePanel != null)
            {
                UnityEngine.Object.Destroy(_nativePanel);
            }

            _nativePanel = new GameObject("KitsuneNativeFormSelector", typeof(RectTransform), typeof(HorizontalLayoutGroup));
            _nativePanel.transform.SetParent(parent, false);

            // Поднимаем панель на Y = 250f (над кнопкой "ИЗМЕНИТЬ ПОРТРЕТ")
            var rect = _nativePanel.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0.5f, 0f);
            rect.anchorMax = new Vector2(0.5f, 0f);
            rect.pivot = new Vector2(0.5f, 0f);
            rect.anchoredPosition = new Vector2(-150f, 250f); // Сдвиг влево к блоку выборов и вверх
            rect.sizeDelta = new Vector2(520f, 65f);

            var layout = _nativePanel.GetComponent<HorizontalLayoutGroup>();
            layout.spacing = 18f;
            layout.childAlignment = TextAnchor.MiddleCenter;
            layout.childControlWidth = false;
            layout.childControlHeight = false;

            // Клонируем кнопки-слоты
            _foxButton = UnityEngine.Object.Instantiate(template, _nativePanel.transform);
            _foxButton.name = "KitsuneFoxButton";
            _foxText = _foxButton.GetComponentInChildren<TextMeshProUGUI>();

            _humanButton = UnityEngine.Object.Instantiate(template, _nativePanel.transform);
            _humanButton.name = "KitsuneHumanButton";
            _humanText = _humanButton.GetComponentInChildren<TextMeshProUGUI>();

            // Размер карточек слотов
            var foxRect = _foxButton.GetComponent<RectTransform>();
            var humanRect = _humanButton.GetComponent<RectTransform>();
            
            if (foxRect != null) foxRect.sizeDelta = new Vector2(240f, 60f);
            if (humanRect != null) humanRect.sizeDelta = new Vector2(240f, 60f);

            // Настройка кликов
            _foxButton.OnLeftClick.RemoveAllListeners();
            _foxButton.OnLeftClick.AddListener(() =>
            {
                CharGenState.CurrentForm = EditingPortraitForm.Fox;
                UpdateUIState();
                Main.Logger?.Log("[KitsuneUI] Активный слот переключен на: ФОРМА ЛИСЫ");
            });

            _humanButton.OnLeftClick.RemoveAllListeners();
            _humanButton.OnLeftClick.AddListener(() =>
            {
                CharGenState.CurrentForm = EditingPortraitForm.Human;
                UpdateUIState();
                Main.Logger?.Log("[KitsuneUI] Активный слот переключен на: ФОРМА ЧЕЛОВЕКА");
            });

            Main.Logger?.Log("[KitsuneUI] Слоты выбора портретов успешно построены!");
        }

        public static void UpdateUIState()
        {
            if (_nativePanel == null) return;

            bool isKitsune = Main.IsKitsuneSelectedInCharGen;
            _nativePanel.SetActive(isKitsune);

            if (!isKitsune) return;

            bool isFoxActive = CharGenState.CurrentForm == EditingPortraitForm.Fox;

            string foxPortraitName = !string.IsNullOrEmpty(CharGenState.FoxPortrait) ? CharGenState.FoxPortrait : "не выбран";
            string humanPortraitName = !string.IsNullOrEmpty(CharGenState.HumanPortrait) ? CharGenState.HumanPortrait : "не выбран";

            // Отрисовка слота Лисы
            if (_foxText != null)
            {
                _foxText.lineSpacing = -15f;
                if (isFoxActive)
                {
                    _foxText.text = "<b><color=#E2B053>🦊 ФОРМА ЛИСЫ</color></b>\n" +
                                    "<size=10><color=#00FF7F>[ НАЗНАЧЕНИЕ... ]</color></size>\n" +
                                    $"<size=11><color=#FFFFFF>Папка: {foxPortraitName}</color></size>";
                }
                else
                {
                    _foxText.text = "<b><color=#8C8C8C>🦊 ФОРМА ЛИСЫ</color></b>\n" +
                                    "<size=10><color=#707070>[ Выбрать слот ]</color></size>\n" +
                                    $"<size=11><color=#909090>Папка: {foxPortraitName}</color></size>";
                }
            }

            // Отрисовка слота Человека
            if (_humanText != null)
            {
                _humanText.lineSpacing = -15f;
                if (!isFoxActive)
                {
                    _humanText.text = "<b><color=#E2B053>👤 ФОРМА ЧЕЛОВЕКА</color></b>\n" +
                                      "<size=10><color=#00FF7F>[ НАЗНАЧЕНИЕ... ]</color></size>\n" +
                                      $"<size=11><color=#FFFFFF>Папка: {humanPortraitName}</color></size>";
                }
                else
                {
                    _humanText.text = "<b><color=#8C8C8C>👤 ФОРМА ЧЕЛОВЕКА</color></b>\n" +
                                      "<size=10><color=#707070>[ Выбрать слот ]</color></size>\n" +
                                      $"<size=11><color=#909090>Папка: {humanPortraitName}</color></size>";
                }
            }
        }
    }
}