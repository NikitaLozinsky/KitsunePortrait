using System;
using System.Diagnostics.CodeAnalysis;
using HarmonyLib;
using Kingmaker.Blueprints;
using Kingmaker.Blueprints.Classes;
using Kingmaker.EntitySystem.Entities;
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

    [HarmonyPatch(typeof(CharGenRacePhaseVM), "SelectRaceInMechanic")]
    public static class CharGenRaceSelectPatch
    {
        public static void Postfix(BlueprintRace race)
        {
            if (race == null) return;

            string raceGuid = race.AssetGuidThreadSafe;
            bool isKitsune = raceGuid == Guids.KitsuneRace
                             || race.name.Equals("KitsuneRace", StringComparison.OrdinalIgnoreCase);

            Main.IsKitsuneSelectedInCharGen = isKitsune;
            Main.Logger.Log($"[CharGen] Выбрана раса: '{race.name}'. Это Кицунэ? -> {isKitsune}");
        }
    }

    [HarmonyPatch(typeof(CharGenPortraitPhaseVM), "UpdatePortraitInLevelupController")]
    public static class CharGenPortraitSelectPatch
    {
        public static bool Prefix(BlueprintPortrait portrait)
        {
            if (portrait == null) return true;

            // Кастомный портрет имеет CustomId, у ванильного сохраняем GUID блупринта
            string portraitId = portrait.Data?.CustomId;
            if (string.IsNullOrEmpty(portraitId))
            {
                portraitId = portrait.AssetGuidThreadSafe;
            }

            if (Main.IsKitsuneSelectedInCharGen)
            {
                if (CharGenState.CurrentForm == EditingPortraitForm.Human)
                {
                    Main.TemporaryHumanPortrait = portraitId;
                    Main.Logger.Log($"[CharGen] Зафиксирован портрет ЧЕЛОВЕКА: '{portraitId}'");
                    return true;
                }

                Main.SelectedFoxPortrait = portraitId;
                Main.Logger.Log($"[CharGen] Зафиксирован портрет ЛИСЫ: '{portraitId}'");
            }

            return true;
        }
    }

    public class KitsunePortraitOverlay : MonoBehaviour
    {
        private static KitsunePortraitOverlay _instance;

        public static void EnsureInstance()
        {
            if (_instance != null) return;

            var go = new GameObject("[KitsunePortraitOverlay]");
            DontDestroyOnLoad(go);
            _instance = go.AddComponent<KitsunePortraitOverlay>();
        }

        private void OnGUI()
        {
            if (!Main.IsKitsuneSelectedInCharGen) return;

            if (CharGenState.IsInPortraitPhase)
            {
                DrawToggle();
            }
            else if (string.IsNullOrEmpty(Main.TemporaryHumanPortrait))
            {
                DrawReminder();
            }
        }

        private void DrawToggle()
        {
            GUI.depth = -1000;
            float width = 520f;
            float height = 55f;
            float left = (Screen.width - width) / 2f;
            float top = 12f;

            GUILayout.BeginArea(new Rect(left, top, width, height), GUI.skin.box);
            GUILayout.BeginHorizontal();

            GUIStyle btnStyle = new GUIStyle(GUI.skin.button) { richText = true };

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
            GUILayout.EndArea();
        }

        private void DrawReminder()
        {
            GUI.depth = -1000;
            float width = 480f;
            float height = 28f;
            float left = (Screen.width - width) / 2f;
            float top = 12f;

            GUIStyle style = new GUIStyle(GUI.skin.box) { richText = true };
            SetCenterAlignment(style);
            GUI.Box(new Rect(left, top, width, height),
                "<color=#FFD700>🦊 Вернитесь на вкладку портрета для настройки формы человека</color>", style);
        }

        private static void SetCenterAlignment(GUIStyle style)
        {
            try
            {
                var alignmentProp = typeof(GUIStyle).GetProperty("alignment");
                if (alignmentProp == null) return;
                alignmentProp.SetValue(style, Enum.ToObject(alignmentProp.PropertyType, 4));
            }
            catch
            {
                // Игнорируем ошибки выравнивания
            }
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
            }
            catch (Exception ex)
            {
                Main.Logger.Error($"[CharGen] Ошибка при финализации: {ex}");
            }
        }
    }
}