using System.Collections.Generic;
using Kingmaker.Blueprints;
using Kingmaker.EntitySystem.Entities;
using Kingmaker.PubSubSystem;
using UnityEngine;

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
        
        public static string GetPortraitId(UnitEntityData unit)
        {
            if (unit?.UISettings == null) return null;

            if (unit.UISettings.PortraitBlueprint != null)
                return unit.UISettings.PortraitBlueprint.AssetGuidThreadSafe;

            return unit.UISettings.Portrait?.CustomId;
        }

        public static string GetPortraitId(PortraitData portraitData)
        {
            return portraitData?.CustomId;
        }
        
        public static Sprite GetSmallPortraitSprite(string portraitId)
        {
            if (string.IsNullOrEmpty(portraitId)) return null;

            BlueprintPortrait blueprintPortrait = ResourcesLibrary.TryGetBlueprint<BlueprintPortrait>(portraitId);
            if (blueprintPortrait?.Data != null)
            {
                return blueprintPortrait.Data.SmallPortrait;
            }

            // Не нашли такой блупринт в игре — считаем, что это кастомный портрет из
            // папки Portraits, и грузим его тем же путём, что и ApplyPortrait ниже.
            var customPortraitData = new PortraitData(portraitId);
            if (customPortraitData.IsCustom)
            {
                customPortraitData.EnsureImages();
            }

            return customPortraitData.SmallPortrait;
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
                    string currentPortraitId = GetPortraitId(unit);
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
                string currentId = GetPortraitId(unit);
                if (currentId == portraitId)
                    return;
            }

            Main.Logger?.Log($"[KitsunePortrait] Ставим портрет '{portraitId}' персонажу {unit.CharacterName}");

            // 1. Проверяем, является ли ID стандартным ванильным портретом игры
            BlueprintPortrait portraitBp = ResourcesLibrary.TryGetBlueprint<BlueprintPortrait>(portraitId);
            if (portraitBp != null)
            {
                unit.UISettings.SetPortrait(portraitBp);
                EventBus.RaiseEvent((IUnitPortraitChangedHandler h) => h.HandlePortraitChanged(unit));
                return;
            }

            // 2. Если блупринт игры не найден — применяем как кастомный портрет из папки
            var customPortraitData = new PortraitData(portraitId);
            unit.UISettings.SetPortrait(customPortraitData);
            EventBus.RaiseEvent((IUnitPortraitChangedHandler h) => h.HandlePortraitChanged(unit));
        }

        public static void ClearRuntimeCaches()
        {
            OriginalFoxPortraits.Clear();
        }
    }
}