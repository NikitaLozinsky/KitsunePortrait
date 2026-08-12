using System.Collections.Generic;
using System.Reflection;
using Kingmaker.Blueprints;
using Kingmaker.EntitySystem.Entities;

namespace KitsunePortrait
{
    public static class PortraitManager
    {
        // Резервная папка портрета для формы человека
        public const string FallbackHumanPortraitId = "KitsuneHumanDefault";

        // Кэш для лисьих портретов персонажей из старых сохранений
        private static readonly Dictionary<string, string> OriginalFoxPortraits = new Dictionary<string, string>();

        public static bool IsKitsune(UnitEntityData unit)
        {
            if (unit?.Progression?.Race == null) return false;
            return unit.Progression.Race.AssetGuidThreadSafe == Guids.KitsuneRace;
        }

        public static bool IsNenio(UnitEntityData unit)
        {
            return unit?.Blueprint?.AssetGuidThreadSafe == Guids.NenioBlueprint;
        }

        public static bool IsInHumanForm(UnitEntityData unit)
        {
            if (unit?.Buffs == null) return false;
            foreach (var buff in unit.Buffs)
            {
                if (buff.Blueprint?.AssetGuidThreadSafe == Guids.KitsuneHumanBuff)
                    return true;
            }
            return false;
        }

        /// <summary>
        /// Безопасно извлекает CustomId или GUID блупринта из PortraitData через System.Reflection.
        /// </summary>
        public static string GetPortraitId(PortraitData portraitData)
        {
            if (portraitData == null) return null;

            // 1. Если портрет кастомный (из папки Portraits) — берём CustomId
            if (!string.IsNullOrEmpty(portraitData.CustomId))
                return portraitData.CustomId;

            // 2. Если портрет ванильный — читаем приватное поле m_Blueprint
            var field = typeof(PortraitData).GetField("m_Blueprint", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)
                     ?? typeof(PortraitData).GetField("Blueprint", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);

            if (field != null)
            {
                object value = field.GetValue(portraitData);
                if (value != null)
                {
                    if (value is BlueprintPortrait bp)
                        return bp.AssetGuidThreadSafe;

                    if (value is BlueprintReference<BlueprintPortrait> bpRef)
                        return bpRef.Guid.ToString();

                    var nameProp = value.GetType().GetProperty("name")?.GetValue(value) as string;
                    if (!string.IsNullOrEmpty(nameProp))
                        return nameProp;
                }
            }

            return null;
        }

        public static void UpdatePortrait(UnitEntityData unit)
        {
            if (unit == null || !IsKitsune(unit)) return;

            string unitId = unit.UniqueId;
            bool inHumanForm = IsInHumanForm(unit);

            Main.Settings.CharacterPortraits.TryGetValue(unitId, out PortraitPair savedPair);

            Main.Logger?.Log($"[KitsunePortrait] Проверка формы для {unit.CharacterName}. Человек: {inHumanForm}");

            if (inHumanForm)
            {
                // Запоминаем текущий (лисий) портрет для старых сохранений
                if (!OriginalFoxPortraits.ContainsKey(unitId) && unit.UISettings?.Portrait != null)
                {
                    string currentPortraitId = GetPortraitId(unit.UISettings.Portrait);
                    if (!string.IsNullOrEmpty(currentPortraitId))
                    {
                        OriginalFoxPortraits[unitId] = currentPortraitId;
                        Main.Logger?.Log($"[KitsunePortrait] Запомнили оригинальный портрет лисы для {unit.CharacterName}: '{OriginalFoxPortraits[unitId]}'");
                    }
                }

                string humanId = !string.IsNullOrEmpty(savedPair?.HumanPortrait)
                    ? savedPair.HumanPortrait
                    : FallbackHumanPortraitId;

                ApplyPortrait(unit, humanId);
            }
            else
            {
                string foxId = !string.IsNullOrEmpty(savedPair?.FoxPortrait)
                    ? savedPair.FoxPortrait
                    : (OriginalFoxPortraits.TryGetValue(unitId, out string cachedFoxId) ? cachedFoxId : null);

                if (!string.IsNullOrEmpty(foxId))
                {
                    ApplyPortrait(unit, foxId);
                }
            }
        }

        private static void ApplyPortrait(UnitEntityData unit, string portraitId)
        {
            if (unit?.UISettings == null || string.IsNullOrEmpty(portraitId)) return;

            var currentPortrait = unit.UISettings.Portrait;
            if (currentPortrait != null)
            {
                string currentId = GetPortraitId(currentPortrait);
                if (currentId == portraitId)
                    return;
            }

            Main.Logger?.Log($"[KitsunePortrait] Ставим портрет '{portraitId}' персонажу {unit.CharacterName}");

            // 1. Проверяем, является ли ID стандартным ванильным портретом игры
            BlueprintPortrait portraitBp = ResourcesLibrary.TryGetBlueprint<BlueprintPortrait>(portraitId);
            if (portraitBp != null)
            {
                unit.UISettings.SetPortrait(portraitBp);
                return;
            }

            // 2. Если блупринт игры не найден — применяем как кастомный портрет из папки
            var customPortraitData = new PortraitData(portraitId);
            unit.UISettings.SetPortrait(customPortraitData);
        }
    }
}