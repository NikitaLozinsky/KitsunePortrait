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

            // Игра периодически сама "эхо"-обновляет SelectedPortrait обратно на текущий
            // реальный портрет контроллера (CharGenPortraitPhaseVM.TryUpdatePortraitFromState,
            // вызывается из LevelUpController.UpdateCommand и при входе во вкладку портрета).
            // Так как запись портрета Человека ниже ВСЕГДА блокируется (return false), реальный
            // портрет контроллера — это ВСЕГДА портрет Лисы. Без этой проверки такое эхо в
            // режиме Человека неотличимо от настоящего клика и затирает TemporaryHumanPortrait
            // портретом Лисы — портрет Лисы "дублируется" на обе формы. Сам оригинальный метод
            // в такой ситуации ничего не делает (см. его реализацию: вызывает SelectPortrait,
            // только если Preview.Portrait != portrait.Data) — повторяем ту же проверку здесь,
            // до нашей логики выбора формы.
            var levelUpController = CharGenState.CachedLevelUpController;
            if (levelUpController?.Preview?.Portrait != null && levelUpController.Preview.Portrait == portrait.Data)
            {
                return true;
            }

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