using System;
using System.Diagnostics.CodeAnalysis;
using HarmonyLib;
using Kingmaker.Blueprints;
using Kingmaker.Blueprints.Classes;
using Kingmaker.EntitySystem.Entities;
using Kingmaker.UI.MVVM._VM.CharGen;
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

    // 1. Отслеживаем вход в экран выбора портрета
    [HarmonyPatch(typeof(CharGenPortraitPhaseVM))]
    public static class CharGenPortraitPhaseLifecyclePatch
    {
        [HarmonyPatch(MethodType.Constructor, typeof(LevelUpController))]
        [HarmonyPostfix]
        public static void OnConstruct()
        {
            CharGenState.IsInPortraitPhase = true;
            CharGenState.CurrentForm = EditingPortraitForm.Fox;

            KitsunePortraitOverlay.EnsureInstance();
        }
    }

    // 2. Перехватываем выбор расы
    [HarmonyPatch(typeof(CharGenRacePhaseVM), "SelectRaceInMechanic")]
    public static class CharGenRaceSelectPatch
    {
        public static void Postfix(BlueprintRace race)
        {
            if (race == null) return;

            string raceGuid = race.AssetGuid.ToString();
            bool isKitsune = raceGuid == "fd188bb7bb0002e49863aec93bfb9d99" 
                             || race.name.Equals("KitsuneRace", StringComparison.OrdinalIgnoreCase);

            Main.IsKitsuneSelectedInCharGen = isKitsune;
            Main.Logger.Log($"[CharGen] Выбрана раса: '{race.name}'. Это Кицунэ? -> {isKitsune}");
        }
    }

    // 3. Распределяем клики по портретам в зависимости от выбранного режима (Лиса / Человек)
    [HarmonyPatch(typeof(CharGenPortraitPhaseVM), "UpdatePortraitInLevelupController")]
    public static class CharGenPortraitSelectPatch
    {
        public static bool Prefix(BlueprintPortrait portrait)
        {
            if (portrait == null) return true;

            string portraitId = portrait.Data?.CustomId ?? portrait.name;

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

    // 4. GUI-оверлей без зависимости от FontStyle
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
            if (!Main.IsKitsuneSelectedInCharGen || !CharGenState.IsInPortraitPhase)
                return;

            GUI.depth = -1000;
            float width = 480f;
            float height = 45f;
            float left = (Screen.width - width) / 2f;
            float top = 12f;

            GUILayout.BeginArea(new Rect(left, top, width, height), GUI.skin.box);
            GUILayout.BeginHorizontal();

            // Включаем RichText для разметки тегами <b> и <color>
            GUIStyle btnStyle = new GUIStyle(GUI.skin.button) { richText = true };

            // Оформление кнопки "Лиса"
            bool isFoxActive = CharGenState.CurrentForm == EditingPortraitForm.Fox;
            string foxText = string.IsNullOrEmpty(Main.SelectedFoxPortrait) ? "Лиса (выберите)" : "Лиса (выбрано)";
            string foxLabel = isFoxActive 
                ? $"<b><color=#FFD700>🦊 {foxText}</color></b>" 
                : $"🦊 {foxText}";

            if (GUILayout.Button(foxLabel, btnStyle, GUILayout.Height(30)))
            {
                CharGenState.CurrentForm = EditingPortraitForm.Fox;
            }

            // Оформление кнопки "Человек"
            bool isHumanActive = CharGenState.CurrentForm == EditingPortraitForm.Human;
            string humanText = string.IsNullOrEmpty(Main.TemporaryHumanPortrait) ? "Человек (выберите)" : "Человек (выбрано)";
            string humanLabel = isHumanActive 
                ? $"<b><color=#FFD700>👤 {humanText}</color></b>" 
                : $"👤 {humanText}";

            if (GUILayout.Button(humanLabel, btnStyle, GUILayout.Height(30)))
            {
                CharGenState.CurrentForm = EditingPortraitForm.Human;
            }

            GUILayout.EndHorizontal();
            GUILayout.EndArea();
        }
    }

    // 5. Финализация и сохранение обоих портретов
    [HarmonyPatch(typeof(CharGenContextVM), "CompleteCharGen")]
    public static class CharGenCompletePatch
    {
        [SuppressMessage("ReSharper", "InconsistentNaming")]
        public static void Prefix(object __instance)
        {
            try
            {
                var controller = Traverse.Create(__instance)
                    .Field("m_LevelUpController")
                    .GetValue<LevelUpController>();

                UnitEntityData unit = controller?.Unit;
                if (unit == null) return;

                bool isKitsune = Main.IsKitsuneSelectedInCharGen || 
                                 (unit.Progression?.Race != null && unit.Progression.Race.AssetGuid.ToString() == "fd188bb7bb0002e49863aec93bfb9d99");

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
                        Main.TemporaryHumanPortrait = string.Empty;
                    }

                    Main.Settings.Save(Main.ModEntry);
                    Main.Logger.Log($"[CharGen] УСПЕШНО СОХРАНЕНО: {unit.CharacterName} | Лиса: '{Main.Settings.CharacterPortraits[unitId].FoxPortrait}' | Человек: '{Main.Settings.CharacterPortraits[unitId].HumanPortrait}'");
                }

                CharGenState.IsInPortraitPhase = false;
            }
            catch (Exception ex)
            {
                Main.Logger.Error($"[CharGen] Ошибка при финализации: {ex}");
            }
        }
    }
}