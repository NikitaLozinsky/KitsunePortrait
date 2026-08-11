using System.Collections.Generic;
using Kingmaker.Blueprints;
using Kingmaker.EntitySystem.Entities;

namespace KitsunePortrait
{
    public static class PortraitManager
    {
        public const string KitsuneRaceGuid = "e4d122b3783a77d4001cd133fd06fdd8";
        public const string KitsuneHumanBuffGuid = "ee6c7f5437a57ad48aaf47320129df33";
        public const string NenioBlueprintGuid = "096fc4a96d438b64e8d266be7ba5546b";

        // Захардкоженный ID портрета человека для тестов
        public const string HardcodedHumanPortraitId = "002";

        private static readonly Dictionary<string, string> OriginalFoxPortraits = new Dictionary<string, string>();

        public static bool IsKitsune(UnitEntityData unit)
        {
            if (unit?.Progression?.Race == null) return false;
            return unit.Progression.Race.AssetGuidThreadSafe == KitsuneRaceGuid;
        }

        public static bool IsNenio(UnitEntityData unit)
        {
            return unit?.Blueprint?.AssetGuidThreadSafe == NenioBlueprintGuid;
        }

        public static bool IsInHumanForm(UnitEntityData unit)
        {
            if (unit?.Buffs == null) return false;
            foreach (var buff in unit.Buffs)
            {
                if (buff.Blueprint?.AssetGuidThreadSafe == KitsuneHumanBuffGuid)
                    return true;
            }
            return false;
        }

        public static void UpdatePortrait(UnitEntityData unit)
        {
            if (unit == null) return;

            string unitId = unit.UniqueId;
            bool inHumanForm = IsInHumanForm(unit);

            Main.Logger?.Log($"[KitsunePortrait] Проверка формы для {unit.CharacterName}. Человек: {inHumanForm}");

            if (inHumanForm)
            {
                if (!OriginalFoxPortraits.ContainsKey(unitId) && unit.UISettings?.Portrait != null)
                {
                    OriginalFoxPortraits[unitId] = unit.UISettings.Portrait.CustomId;
                    Main.Logger?.Log($"[KitsunePortrait] Запомнили портрет лисы для {unit.CharacterName}: '{OriginalFoxPortraits[unitId]}'");
                }

                ApplyCustomPortrait(unit, HardcodedHumanPortraitId);
            }
            else
            {
                if (OriginalFoxPortraits.TryGetValue(unitId, out string foxId) && !string.IsNullOrEmpty(foxId))
                {
                    ApplyCustomPortrait(unit, foxId);
                }
            }
        }

        private static void ApplyCustomPortrait(UnitEntityData unit, string customPortraitId)
        {
            if (unit?.UISettings == null || string.IsNullOrEmpty(customPortraitId)) return;

            if (unit.UISettings.Portrait != null && unit.UISettings.Portrait.CustomId == customPortraitId)
                return;

            Main.Logger?.Log($"[KitsunePortrait] Ставим портрет '{customPortraitId}' персонажу {unit.CharacterName}");

            var portraitData = new PortraitData(customPortraitId);
            unit.UISettings.SetPortrait(portraitData);
        }
    }
}