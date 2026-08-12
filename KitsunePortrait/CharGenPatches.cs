using System;
using System.Diagnostics.CodeAnalysis;
using HarmonyLib;
using Kingmaker.Blueprints;
using Kingmaker.Blueprints.Classes;
using Kingmaker.EntitySystem.Entities;
using Kingmaker.PubSubSystem;
using Kingmaker.UI;
using Kingmaker.UI.MVVM._VM.CharGen.Phases;
using Kingmaker.UI.MVVM._VM.CharGen.Phases.Portrait;
using Kingmaker.UI.MVVM._VM.CharGen.Phases.Race;
using Kingmaker.UnitLogic.Class.LevelUp;
using UnityEngine;

// ReSharper disable InconsistentNaming
namespace KitsunePortrait
{
    public enum EditingPortraitForm
    {
        Fox,
        Human
    }

    public static class CharGenState
    {
        public static bool IsInPortraitPhase;
        public static EditingPortraitForm CurrentForm = EditingPortraitForm.Fox;
    }

    [HarmonyPatch(typeof(CharGenPortraitPhaseVM))]
    public static class CharGenPortraitPhaseLifecyclePatch
    {
        [HarmonyPatch(MethodType.Constructor, typeof(LevelUpController))]
        [HarmonyPostfix]
        public static void OnConstruct()
        {
            CharGenState.CurrentForm = EditingPortraitForm.Fox;

            Main.IsKitsuneSelectedInCharGen = false;
            Main.SelectedFoxPortrait = string.Empty;
            Main.TemporaryHumanPortrait = string.Empty;
            CharGenRaceSelectPatch.ResetSessionState();

            KitsunePortraitOverlay.EnsureInstance();
        }
    }

    [HarmonyPatch(typeof(CharGenPhaseBaseVM), "BeginDetailedView")]
    public static class CharGenPhaseBeginDetailedViewPatch
    {
        [SuppressMessage("ReSharper", "InconsistentNaming")]
        public static void Postfix(object __instance)
        {
            if (__instance is CharGenPortraitPhaseVM)
            {
                CharGenState.IsInPortraitPhase = true;
            }
        }
    }

    [HarmonyPatch(typeof(CharGenPhaseBaseVM), "EndDetailedView")]
    public static class CharGenPhaseEndDetailedViewPatch
    {
        [SuppressMessage("ReSharper", "InconsistentNaming")]
        public static void Postfix(object __instance)
        {
            if (__instance is CharGenPortraitPhaseVM)
            {
                CharGenState.IsInPortraitPhase = false;
            }
        }
    }

    // Напоминание в стиле игры: как только выбрана раса Кицунэ, подсказываем игроку
    // вернуться на вкладку "Портрет", чтобы назначить портрет для человеческой формы.
    // API подтверждён декомпиляцией: тот же IMessageModalUIHandler, что использует
    // сама игра (см. CharGenVM.TryWarnToDropLevelupPlan).
    [HarmonyPatch(typeof(CharGenRacePhaseVM), "SelectRaceInMechanic")]
    public static class CharGenRaceSelectPatch
    {
        private static bool _reminderShownThisSession;

        public static void Postfix(BlueprintRace race)
        {
            if (race == null) return;

            string raceGuid = race.AssetGuidThreadSafe;
            bool isKitsune = raceGuid == Guids.KitsuneRace
                             || race.name.Equals("KitsuneRace", StringComparison.OrdinalIgnoreCase);

            Main.IsKitsuneSelectedInCharGen = isKitsune;
            Main.Logger.Log($"[CharGen] Выбрана раса: '{race.name}'. Это Кицунэ? -> {isKitsune}");

            if (isKitsune && !_reminderShownThisSession)
            {
                _reminderShownThisSession = true;
                EventBus.RaiseEvent(delegate(IMessageModalUIHandler h)
                {
                    h.HandleOpen(
                        "Кицунэ умеют менять форму — лиса/человек. Вернитесь на вкладку «Портрет», " +
                        "чтобы назначить отдельный портрет для человеческой формы.",
                        MessageModalBase.ModalType.Message);
                });
            }
        }

        public static void ResetSessionState()
        {
            _reminderShownThisSession = false;
        }
    }

    [HarmonyPatch(typeof(CharGenPortraitPhaseVM), "UpdatePortraitInLevelupController")]
    public static class CharGenPortraitSelectPatch
    {
        public static bool Prefix(BlueprintPortrait portrait)
        {
            if (portrait == null) return true;

            string portraitId = portrait.Data?.CustomId;
            if (string.IsNullOrEmpty(portraitId))
            {
                portraitId = portrait.AssetGuidThreadSafe;
            }

            if (CharGenState.CurrentForm == EditingPortraitForm.Human)
            {
                Main.TemporaryHumanPortrait = portraitId;
                Main.Logger.Log($"[CharGen] Зафиксирован портрет ЧЕЛОВЕКА: '{portraitId}'");
            }
            else
            {
                Main.SelectedFoxPortrait = portraitId;
                Main.Logger.Log($"[CharGen] Зафиксирован портрет ЛИСЫ: '{portraitId}'");
            }

            KitsuneCharGenUIPatch.UpdateUIState();

            return true;
        }
    }

    public class KitsunePortraitOverlay : MonoBehaviour
    {
        private static KitsunePortraitOverlay _instance;
        private Rect _windowRect;
        private const int WindowId = 98765;

        public static void EnsureInstance()
        {
            if (_instance != null) return;

            var go = new GameObject("[KitsunePortraitOverlay]");
            DontDestroyOnLoad(go);
            _instance = go.AddComponent<KitsunePortraitOverlay>();
        }

        private void Awake()
        {
            InitWindowRect();
        }

        private void InitWindowRect()
        {
            float width = 540f;
            float height = 85f;

            float left = Main.Settings.OverlayX >= 0 ? Main.Settings.OverlayX : (Screen.width - width) / 2f;
            float top = Main.Settings.OverlayY >= 0 ? Main.Settings.OverlayY : 15f;

            _windowRect = new Rect(left, top, width, height);
        }
        
        /*
        private void OnGUI()
        {
            if (!Main.IsKitsuneSelectedInCharGen) return;

            GUI.depth = -1000;

            if (CharGenState.IsInPortraitPhase || string.IsNullOrEmpty(Main.TemporaryHumanPortrait))
            {
                _windowRect = GUI.Window(WindowId, _windowRect, DrawWindowContent, "<b><color=#FFD700>Кицунэ Портреты</color></b>");
            }
        }
        */

        private void DrawWindowContent(int windowID)
        {
            // Позволяет перетаскивать окно за верхнюю плашку и за иконку в центре
            GUI.DragWindow(new Rect(0, 0, _windowRect.width, 22));

            if (Math.Abs(Main.Settings.OverlayX - _windowRect.x) > 1f || Math.Abs(Main.Settings.OverlayY - _windowRect.y) > 1f)
            {
                Main.Settings.OverlayX = _windowRect.x;
                Main.Settings.OverlayY = _windowRect.y;
                Main.Settings.Save(Main.ModEntry);
            }

            if (CharGenState.IsInPortraitPhase)
            {
                DrawToggle();
            }
            else
            {
                DrawReminder();
            }
        }

        private void DrawToggle()
        {
            GUILayout.BeginHorizontal();

            GUIStyle btnStyle = new GUIStyle(GUI.skin.button) { richText = true };
            GUIStyle gripStyle = new GUIStyle(GUI.skin.box) { richText = true };

            // 1. Кнопка «Лиса»
            bool isFoxActive = CharGenState.CurrentForm == EditingPortraitForm.Fox;
            string foxStatus = string.IsNullOrEmpty(Main.SelectedFoxPortrait) ? "не выбран" : Main.SelectedFoxPortrait;
            string foxHeader = string.IsNullOrEmpty(Main.SelectedFoxPortrait) ? "Лиса (выберите)" : "Лиса (выбрано)";
            string foxLabel = isFoxActive
                ? $"<b><color=#FFD700>🦊 {foxHeader}</color></b>\n<size=11><color=#E0E0E0>[ {foxStatus} ]</color></size>"
                : $"🦊 {foxHeader}\n<size=11><color=#888888>[ {foxStatus} ]</color></size>";

            if (GUILayout.Button(foxLabel, btnStyle, GUILayout.Height(42)))
            {
                CharGenState.CurrentForm = EditingPortraitForm.Fox;
            }

            // 2. Центральная иконка перетаскивания окна
            GUILayout.Box("<b><size=16><color=#FFD700>✥</color></size></b>\n<size=9><color=#AAAAAA>тяни</color></size>", gripStyle, GUILayout.Width(42), GUILayout.Height(42));

            // 3. Кнопка «Человек»
            bool isHumanActive = CharGenState.CurrentForm == EditingPortraitForm.Human;
            string humanStatus = string.IsNullOrEmpty(Main.TemporaryHumanPortrait) ? "не выбран" : Main.TemporaryHumanPortrait;
            string humanHeader = string.IsNullOrEmpty(Main.TemporaryHumanPortrait) ? "Человек (выберите)" : "Человек (выбрано)";
            string humanLabel = isHumanActive
                ? $"<b><color=#FFD700>👤 {humanHeader}</color></b>\n<size=11><color=#E0E0E0>[ {humanStatus} ]</color></size>"
                : $"👤 {humanHeader}\n<size=11><color=#888888>[ {humanStatus} ]</color></size>";

            if (GUILayout.Button(humanLabel, btnStyle, GUILayout.Height(42)))
            {
                CharGenState.CurrentForm = EditingPortraitForm.Human;
            }

            GUILayout.EndHorizontal();
        }

        private void DrawReminder()
        {
            GUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();

            GUIStyle labelStyle = new GUIStyle(GUI.skin.label) { richText = true };
            GUILayout.Label("<color=#FFD700>🦊 Вернитесь на вкладку портрета для настройки формы человека</color>", labelStyle, GUILayout.Height(35));

            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
        }
    }

    [HarmonyPatch(typeof(LevelUpController), nameof(LevelUpController.Commit))]
    public static class CharGenCompletePatch
    {
        [SuppressMessage("ReSharper", "InconsistentNaming")]
        public static void Postfix(LevelUpController __instance)
        {
            try
            {
                if (__instance == null) return;

                var mode = __instance.State?.Mode;
                if (mode != LevelUpState.CharBuildMode.CharGen && mode != LevelUpState.CharBuildMode.Respec)
                    return;

                UnitEntityData unit = __instance.Unit;
                if (unit == null) return;

                bool isKitsune = Main.IsKitsuneSelectedInCharGen ||
                                 (unit.Progression?.Race != null && unit.Progression.Race.AssetGuidThreadSafe == Guids.KitsuneRace);

                if (isKitsune)
                {
                    string unitId = unit.UniqueId;

                    if (!Main.Settings.CharacterPortraits.ContainsKey(unitId))
                    {
                        Main.Settings.CharacterPortraits[unitId] = new PortraitPair();
                    }

                    if (!string.IsNullOrEmpty(Main.SelectedFoxPortrait))
                    {
                        Main.Settings.CharacterPortraits[unitId].FoxPortrait = Main.SelectedFoxPortrait;

                        BlueprintPortrait foxBp = ResourcesLibrary.TryGetBlueprint<BlueprintPortrait>(Main.SelectedFoxPortrait);
                        if (foxBp != null && unit.UISettings != null)
                        {
                            unit.UISettings.SetPortrait(foxBp);
                        }
                    }

                    if (!string.IsNullOrEmpty(Main.TemporaryHumanPortrait))
                    {
                        Main.Settings.CharacterPortraits[unitId].HumanPortrait = Main.TemporaryHumanPortrait;
                    }

                    Main.Settings.Save(Main.ModEntry);
                    Main.Logger.Log($"[CharGen] УСПЕШНО СОХРАНЕНО: {unit.CharacterName} | Лиса: '{Main.Settings.CharacterPortraits[unitId].FoxPortrait}' | Человек: '{Main.Settings.CharacterPortraits[unitId].HumanPortrait}'");
                }

                Main.IsKitsuneSelectedInCharGen = false;
                Main.SelectedFoxPortrait = string.Empty;
                Main.TemporaryHumanPortrait = string.Empty;
                CharGenState.IsInPortraitPhase = false;
                CharGenRaceSelectPatch.ResetSessionState();
            }
            catch (Exception ex)
            {
                Main.Logger.Error($"[CharGen] Ошибка при финализации: {ex}");
            }
        }
    }
}