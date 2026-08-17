using System;
using System.Diagnostics.CodeAnalysis;
using HarmonyLib;
using Kingmaker.Blueprints;
using Kingmaker.Blueprints.Classes;
using Kingmaker.EntitySystem.Entities;
using Kingmaker.PubSubSystem;
using Kingmaker.UI;
using Kingmaker.UI.MVVM._VM.CharGen;
using Kingmaker.UI.MVVM._VM.CharGen.Phases;
using Kingmaker.UI.MVVM._VM.CharGen.Phases.Portrait;
using Kingmaker.UI.MVVM._VM.CharGen.Phases.Race;
using Kingmaker.UnitLogic.Class.LevelUp;

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

        public static CharGenVM CachedCharGenVM;
        public static CharGenPortraitPhaseVM CachedPortraitPhaseVM;
        public static LevelUpController CachedLevelUpController;

        public const string DefaultHumanPlaceholderId = "CustomBase";
    }

    [HarmonyPatch(typeof(CharGenPortraitPhaseVM))]
    public static class CharGenPortraitPhaseLifecyclePatch
    {
        [HarmonyPatch(MethodType.Constructor, typeof(LevelUpController))]
        [HarmonyPostfix]
        public static void OnConstruct(CharGenPortraitPhaseVM __instance, LevelUpController levelUpController)
        {
            CharGenState.CurrentForm = EditingPortraitForm.Fox;
            CharGenState.CachedPortraitPhaseVM = __instance;
            CharGenState.CachedLevelUpController = levelUpController;

            Main.IsKitsuneSelectedInCharGen = false;
            
            // Захватываем начальный портрет, если он уже предвыбран игрой
            var currentPortrait = levelUpController?.Unit?.UISettings?.PortraitBlueprint;
            if (currentPortrait != null)
            {
                Main.SelectedFoxPortrait = !string.IsNullOrEmpty(currentPortrait.Data?.CustomId)
                    ? currentPortrait.Data.CustomId
                    : currentPortrait.AssetGuidThreadSafe;
            }
            else
            {
                Main.SelectedFoxPortrait = string.Empty;
            }

            Main.TemporaryHumanPortrait = string.Empty;
            CharGenRaceSelectPatch.ResetSessionState();
        }
    }

    [HarmonyPatch(typeof(CharGenVM))]
    [HarmonyPatch(MethodType.Constructor, typeof(LevelUpController), typeof(Action), typeof(Action), typeof(LevelUpConfig))]
    public static class CharGenVMCapturePatch
    {
        public static void Postfix(CharGenVM __instance)
        {
            CharGenState.CachedCharGenVM = __instance;
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
        private static bool _reminderShownThisSession;

        public static void Postfix(BlueprintRace race)
        {
            if (race == null) return;

            string raceGuid = race.AssetGuidThreadSafe;
            bool isKitsune = raceGuid == Guids.KitsuneRace
                             || race.name.Equals("KitsuneRace", StringComparison.OrdinalIgnoreCase);

            Main.IsKitsuneSelectedInCharGen = isKitsune;

            if (isKitsune)
            {
                // 1. Если портрет Лисы НЕ был сохранен на 1-м шаге, пробуем взять фолбэк из UISettings
                if (string.IsNullOrEmpty(Main.SelectedFoxPortrait))
                {
                    var currentPortrait = CharGenState.CachedLevelUpController?.Unit?.UISettings?.PortraitBlueprint;
                    if (currentPortrait != null)
                    {
                        Main.SelectedFoxPortrait = !string.IsNullOrEmpty(currentPortrait.Data?.CustomId)
                            ? currentPortrait.Data.CustomId
                            : currentPortrait.AssetGuidThreadSafe;
                    }
                }

                // 2. Выставляем CustomBase по умолчанию для человека
                if (string.IsNullOrEmpty(Main.TemporaryHumanPortrait))
                {
                    Main.TemporaryHumanPortrait = CharGenState.DefaultHumanPlaceholderId;
                }

                KitsuneCharGenUIPatch.UpdateUIState();
                Main.Logger?.Log($"[CharGen] Выбрана Кицунэ. Портрет Лисы: '{Main.SelectedFoxPortrait}', Портрет Человека: '{Main.TemporaryHumanPortrait}'");

                if (!_reminderShownThisSession)
                {
                    _reminderShownThisSession = true;

                    EventBus.RaiseEvent(delegate(IMessageModalUIHandler h)
                    {
                        h.HandleOpen(
                            messageText: Localization.Get("Kitsune.FormReminder.Message"),
                            modalType: MessageModalBase.ModalType.Dialog,
                            onClose: delegate(MessageModalBase.ButtonType button)
                            {
                                if (button == MessageModalBase.ButtonType.Yes)
                                {
                                    NavigateToPortraitPhase();
                                }
                            },
                            yesLabel: Localization.Get("Kitsune.FormReminder.YesButton"));
                    });
                }
            }
        }

        private static void NavigateToPortraitPhase()
        {
            CharGenState.CurrentForm = EditingPortraitForm.Human;

            if (CharGenState.CachedCharGenVM != null && CharGenState.CachedPortraitPhaseVM != null)
            {
                CharGenState.CachedCharGenVM.CurrentPhaseVM.Value = CharGenState.CachedPortraitPhaseVM;
            }
        }

        public static void ResetSessionState()
        {
            _reminderShownThisSession = false;
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
                if (__instance?.Unit == null) return;

                var mode = __instance.State?.Mode;
                if (mode != LevelUpState.CharBuildMode.CharGen && mode != LevelUpState.CharBuildMode.Respec)
                    return;

                UnitEntityData unit = __instance.Unit;

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

                    if (string.IsNullOrEmpty(Main.TemporaryHumanPortrait))
                    {
                        Main.TemporaryHumanPortrait = CharGenState.DefaultHumanPlaceholderId;
                    }
                    Main.Settings.CharacterPortraits[unitId].HumanPortrait = Main.TemporaryHumanPortrait;

                    Main.Settings.Save(Main.ModEntry);
                    Main.Logger.Log($"[CharGen] УСПЕШНО СОХРАНЕНО: {unit.CharacterName} | Лиса: '{Main.Settings.CharacterPortraits[unitId].FoxPortrait}' | Человек: '{Main.Settings.CharacterPortraits[unitId].HumanPortrait}'");
                }

                Main.IsKitsuneSelectedInCharGen = false;
                Main.SelectedFoxPortrait = string.Empty;
                Main.TemporaryHumanPortrait = string.Empty;
                CharGenState.IsInPortraitPhase = false;
                CharGenState.CachedCharGenVM = null;
                CharGenState.CachedPortraitPhaseVM = null;
                CharGenState.CachedLevelUpController = null;
                CharGenRaceSelectPatch.ResetSessionState();
            }
            catch (Exception ex)
            {
                Main.Logger.Error($"[CharGen] Ошибка при финализации: {ex}");
            }
        }
    }
}