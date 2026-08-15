using HarmonyLib;
using Kingmaker.Blueprints;
using Kingmaker.UI.MVVM._VM.CharGen.Phases.Portrait;

namespace KitsunePortrait
{
    [HarmonyPatch(typeof(CharGenPortraitPhaseVM))]
    public static class KitsunePortraitSelectionPatch
    {
        [HarmonyPatch("UpdatePortraitInLevelupController")]
        [HarmonyPrefix]
        public static bool UpdatePortraitInLevelupController_Prefix(BlueprintPortrait portrait)
        {
            if (portrait == null)
                return true;

            string portraitName = !string.IsNullOrEmpty(portrait.Data?.CustomId)
                ? portrait.Data.CustomId
                : portrait.AssetGuidThreadSafe;

            // 1. ДО выбора расы ИЛИ в режиме Лисы — ВСЕГДА сохраняем портрет как форму Лисы
            if (!Main.IsKitsuneSelectedInCharGen || CharGenState.CurrentForm == EditingPortraitForm.Fox)
            {
                Main.SelectedFoxPortrait = portraitName;
                KitsuneCharGenUIPatch.UpdateUIState();

                Main.Logger?.Log($"[Kitsune] Зафиксирован портрет Лисы: {portraitName}");
                return true; // Разрешаем родную запись в контроллер персонажа
            }

            // 2. В режиме Человека (только когда Кицунэ уже выбрана и активирован переключатель)
            if (CharGenState.CurrentForm == EditingPortraitForm.Human)
            {
                Main.TemporaryHumanPortrait = portraitName;
                KitsuneCharGenUIPatch.UpdateUIState();

                Main.Logger?.Log($"[Kitsune] Выбран человеческий портрет: {portraitName}");

                return false; // Блокируем запись человеческого портрета в игровой контроллер
            }

            return true;
        }
    }
}