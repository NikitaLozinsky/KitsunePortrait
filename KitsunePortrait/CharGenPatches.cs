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
            // CharGenMythicPortraitPhaseVM (экран портрета на 9-м мифическом уровне) наследуется
            // от CharGenPortraitPhaseVM и использует ЭТОТ ЖЕ конструктор базового класса — то есть
            // в рамках ОДНОЙ сессии LevelUpController (например, респек, где мастер переходит с
            // обычной вкладки портрета на вкладку мифического портрета) этот патч может сработать
            // несколько раз. Если LevelUpController тот же, что и в прошлый раз — это продолжение
            // уже идущей сессии, и НЕЛЬЗЯ повторно затирать Main.SelectedFoxPortrait/
            // TemporaryHumanPortrait сохранёнными (старыми) данными из настроек — иначе только что
            // сделанный игроком выбор на предыдущей вкладке этой же сессии теряется и на Commit
            // сохраняются старые значения из прошлого визита в мастер.
            bool isSameSession = ReferenceEquals(CharGenState.CachedLevelUpController, levelUpController);

            CharGenState.CachedPortraitPhaseVM = __instance;
            CharGenState.CachedLevelUpController = levelUpController;

            if (isSameSession)
            {
                return;
            }

            CharGenState.CurrentForm = EditingPortraitForm.Fox;

            UnitEntityData unit = levelUpController?.Unit;
            string unitId = unit?.UniqueId;
            bool isCurrentlyKitsune = PortraitManager.IsKitsune(unit);

            // Если юнит СЕЙЧАС (на момент открытия вкладки портрета) уже Кицунэ и для него
            // есть сохранённая пара портретов — считаем сессию продолжением редактирования
            // существующего персонажа (например, повторный экран портрета при повышении
            // мифического уровня), а не первым созданием.
            if (isCurrentlyKitsune && !string.IsNullOrEmpty(unitId) && Main.Settings.CharacterPortraits.TryGetValue(unitId, out PortraitPair existingPair))
            {
                Main.IsKitsuneSelectedInCharGen = true;
                Main.SelectedFoxPortrait = existingPair.FoxPortrait ?? string.Empty;
                Main.TemporaryHumanPortrait = existingPair.HumanPortrait ?? string.Empty;
            }
            else
            {
                Main.IsKitsuneSelectedInCharGen = false;

                // Захватываем начальный портрет, если он уже предвыбран игрой
                var currentPortrait = unit?.UISettings?.PortraitBlueprint;
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
            }

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

                UnitEntityData unit = __instance.Unit;
                var mode = __instance.State?.Mode;

                bool isKitsune = Main.IsKitsuneSelectedInCharGen ||
                                 (unit.Progression?.Race != null && unit.Progression.Race.AssetGuidThreadSafe == Guids.KitsuneRace);

                if (isKitsune)
                {
                    string unitId = unit.UniqueId;

                    if (!Main.Settings.CharacterPortraits.TryGetValue(unitId, out PortraitPair pair))
                    {
                        pair = new PortraitPair();
                        Main.Settings.CharacterPortraits[unitId] = pair;
                    }

                    bool changed = false;

                    // Захватываем новый портрет, если игрок выбрал его при повышении мифического уровня
                    // 1. Применяем портреты, выбранные во вкладках мода
                    if (!string.IsNullOrEmpty(Main.SelectedFoxPortrait) && Main.SelectedFoxPortrait != pair.FoxPortrait)
                    {
                        pair.FoxPortrait = Main.SelectedFoxPortrait;
                        changed = true;
                    }

                    bool humanExplicitlyPicked = !string.IsNullOrEmpty(Main.TemporaryHumanPortrait)
                                                  && Main.TemporaryHumanPortrait != CharGenState.DefaultHumanPlaceholderId;

                    if (humanExplicitlyPicked && Main.TemporaryHumanPortrait != pair.HumanPortrait)
                    {
                        pair.HumanPortrait = Main.TemporaryHumanPortrait;
                        changed = true;
                    }

                    // 2. Захватываем финальный портрет (например, мифический), если игра
                    // применила его в обход перехвата — но только для формы Лисы. Выбор
                    // формы Человека всегда блокируется в KitsunePortraitSelectionPatch
                    // (return false) и поэтому никогда не попадает в unit.UISettings — если
                    // читать его отсюда для формы Человека, вместо человеческого портрета
                    // сюда всегда попадает последний реально применённый портрет лисы.
                    if (CharGenState.CurrentForm == EditingPortraitForm.Fox)
                    {
                        string activePortraitId = PortraitManager.GetPortraitId(unit);
                        if (!string.IsNullOrEmpty(activePortraitId) && pair.FoxPortrait != activePortraitId)
                        {
                            pair.FoxPortrait = activePortraitId;
                            changed = true;
                        }
                    }

                    // 3. Фолбэк для пустой формы человека
                    if (string.IsNullOrEmpty(pair.HumanPortrait))
                    {
                        pair.HumanPortrait = CharGenState.DefaultHumanPlaceholderId;
                        changed = true;
                    }

                    if (changed)
                    {
                        Main.Settings.Save(Main.ModEntry);
                        Main.Logger.Log($"[CharGen] Сохранено: {unit.CharacterName} | Лиса: '{pair.FoxPortrait}' | Человек: '{pair.HumanPortrait}' | Mode: {mode}");
                    }

                    // Принудительно заставляем мод обновить портрет под текущую форму
                    PortraitManager.UpdatePortrait(unit);

                    bool isCreationFlow = mode == LevelUpState.CharBuildMode.CharGen || mode == LevelUpState.CharBuildMode.Respec;
                    if (isCreationFlow && pair.HumanPortrait == CharGenState.DefaultHumanPlaceholderId)
                    {
                        EventBus.RaiseEvent(delegate(IMessageModalUIHandler h)
                        {
                            h.HandleOpen(
                                messageText: Localization.Get("Kitsune.CommitWarning.NoHumanPortrait", unit.CharacterName),
                                modalType: MessageModalBase.ModalType.Dialog,
                                onClose: delegate(MessageModalBase.ButtonType button) { },
                                yesLabel: Localization.Get("Kitsune.CommitWarning.OkButton"));
                        });
                    }
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