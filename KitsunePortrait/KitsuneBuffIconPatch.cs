using System.Reflection;
using HarmonyLib;
using Kingmaker.Blueprints;
using Kingmaker.Blueprints.JsonSystem;
using Kingmaker.UnitLogic;
using Kingmaker.UnitLogic.ActivatableAbilities;
using UnityEngine;

namespace KitsunePortrait
{
    public static class KitsuneBuffIconPatch
    {
        private const string AssetFoxIcon = "icon_form_metamorphose(fox).png";
        private const string AssetHumanIcon = "icon_form_metamorphose(human).png";

        private static Sprite _foxIcon;
        private static Sprite _humanIcon;

        private static BlueprintScriptableObject _kitsuneBuffBlueprint;
        private static BlueprintScriptableObject _kitsuneAbilityBlueprint;

        [HarmonyPatch(typeof(BlueprintsCache), nameof(BlueprintsCache.Init))]
        public static class BlueprintsCache_Init_Patch
        {
            [HarmonyPostfix]
            public static void Postfix()
            {
                _foxIcon = Assets.LoadCustomSprite(AssetFoxIcon);
                _humanIcon = Assets.LoadCustomSprite(AssetHumanIcon);

                _kitsuneBuffBlueprint = ResourcesLibrary.TryGetBlueprint<BlueprintScriptableObject>(Guids.KitsuneHumanBuff);
                _kitsuneAbilityBlueprint = ResourcesLibrary.TryGetBlueprint<BlueprintScriptableObject>(Guids.KitsuneChangeShapeAbility);
            }
        }

        [HarmonyPatch]
        public static class UnitFact_GetIcon_Patch
        {
            [HarmonyTargetMethod]
            public static MethodBase TargetMethod()
            {
                // Динамический поиск геттера Icon с проходом по базовым классам
                return AccessTools.PropertyGetter(typeof(UnitFact), nameof(UnitFact.Icon));
            }

            [HarmonyPostfix]
            public static void Postfix(UnitFact __instance, ref Sprite __result)
            {
                if (__instance == null || __instance.Blueprint == null) return;

                // 1. Бафф человеческой формы
                if (ReferenceEquals(__instance.Blueprint, _kitsuneBuffBlueprint))
                {
                    if (_humanIcon != null) 
                        __result = _humanIcon;
                }
                // 2. Переключатель способности
                else if (ReferenceEquals(__instance.Blueprint, _kitsuneAbilityBlueprint))
                {
                    if (__instance is ActivatableAbility ability)
                    {
                        if (ability.IsOn && _humanIcon != null)
                            __result = _humanIcon;
                        else if (!ability.IsOn && _foxIcon != null)
                            __result = _foxIcon;
                    }
                }
            }
        }
    }
}