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

        // Ссылки на инстансы этой сессии CharGen — нужны, чтобы программно переключить
        // активную фазу мастера (см. CharGenRaceSelectPatch) тем же способом, что и клик
        // по вкладке в родной навигации: CharGenVM.CurrentPhaseVM.Value = ...
        public static CharGenVM CachedCharGenVM;
        public static CharGenPortraitPhaseVM CachedPortraitPhaseVM;
    }

    [HarmonyPatch(typeof(CharGenPortraitPhaseVM))]
    public static class CharGenPortraitPhaseLifecyclePatch
    {
        [HarmonyPatch(MethodType.Constructor, typeof(LevelUpController))]
        [HarmonyPostfix]
        public static void OnConstruct(CharGenPortraitPhaseVM __instance)
        {
            CharGenState.CurrentForm = EditingPortraitForm.Fox;
            CharGenState.CachedPortraitPhaseVM = __instance;

            Main.IsKitsuneSelectedInCharGen = false;
            Main.SelectedFoxPortrait = string.Empty;
            Main.TemporaryHumanPortrait = string.Empty;
            CharGenRaceSelectPatch.ResetSessionState();

            KitsunePortraitOverlay.EnsureInstance();
        }
    }

    // Захватываем сам CharGenVM сессии — понадобится, чтобы программно переключить
    // активную фазу мастера (CharGenRaceSelectPatch), а не просто просить игрока
    // вернуться на вкладку портрета руками.
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

    // Как только выбрана раса Кицунэ — предлагаем игроку перейти на вкладку "Портрет"
    // диалоговым окном с явным согласием, а не молча телепортируем: если раса выбрана
    // случайно, внезапный переход на другой экран — плохой UX. Переход происходит
    // только по нажатию "Принять" — тем же способом, каким это делает сама игра при
    // клике по вкладке в навигации (CharGenVM.CurrentPhaseVM.Value = ...). Никакого
    // отдельного попапа с сеткой портретов не строим — весь пикер остаётся родным.
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
                        messageText: "Кицунэ умеют менять форму — лиса/человек. Перейти на вкладку «Портрет», " +
                                     "чтобы назначить портрет для человеческой формы?",
                        modalType: MessageModalBase.ModalType.Dialog,
                        onClose: delegate(MessageModalBase.ButtonType button)
                        {
                            if (button == MessageModalBase.ButtonType.Yes)
                            {
                                NavigateToPortraitPhase();
                            }
                            else
                            {
                                Main.Logger.Log("[CharGen] Игрок отклонил переход на вкладку 'Портрет' (закрыл окно/отменил).");
                            }
                        },
                        yesLabel: "Принять");
                });
            }
        }

        private static void NavigateToPortraitPhase()
        {
            // Сразу выставляем "Человек" активной формой — игрок попадёт на экран
            // портрета, где тумблер уже готов принимать клик по нужному слоту.
            CharGenState.CurrentForm = EditingPortraitForm.Human;

            if (CharGenState.CachedCharGenVM != null && CharGenState.CachedPortraitPhaseVM != null)
            {
                CharGenState.CachedCharGenVM.CurrentPhaseVM.Value = CharGenState.CachedPortraitPhaseVM;
                Main.Logger.Log("[CharGen] Переключились на вкладку 'Портрет' по согласию игрока.");
            }
            else
            {
                Main.Logger.Warning("[CharGen] Не удалось переключиться на вкладку 'Портрет' — " +
                                     "нет сохранённой ссылки на CharGenVM или CharGenPortraitPhaseVM.");
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
                CharGenState.CachedCharGenVM = null;
                CharGenState.CachedPortraitPhaseVM = null;
                CharGenRaceSelectPatch.ResetSessionState();
            }
            catch (Exception ex)
            {
                Main.Logger.Error($"[CharGen] Ошибка при финализации: {ex}");
            }
        }
    }
}