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
            if (!Main.IsKitsuneSelectedInCharGen)
                return true;

            if (portrait == null)
                return true;

            // Безопасно получаем имя портрета: если есть CustomId (кастомный) — берем его, иначе системное portrait.name
            string portraitName = !string.IsNullOrEmpty(portrait.Data?.CustomId) 
                ? portrait.Data.CustomId 
                : portrait.name;

            // 1. РЕЖИМ ЧЕЛОВЕКА (Только просмотр)
            if (CharGenState.CurrentForm == EditingPortraitForm.Human)
            {
                Main.TemporaryHumanPortrait = portraitName;
                KitsuneCharGenUIPatch.UpdateUIState();

                Main.Logger?.Log($"[Kitsune] Выбран человеческий портрет (просмотр): {portraitName}");

                // Блокируем запись человеческого портрета в контроллер создания персонажа
                return false;
            }

            // 2. РЕЖИМ ЛИСЫ (Основной портрет)
            if (CharGenState.CurrentForm == EditingPortraitForm.Fox)
            {
                Main.SelectedFoxPortrait = portraitName;
                KitsuneCharGenUIPatch.UpdateUIState();

                Main.Logger?.Log($"[Kitsune] Выбран портрет Лисы (активный): {portraitName}");

                return true;
            }

            return true;
        }
    }
}