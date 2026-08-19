using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Kingmaker.Blueprints;
using Kingmaker.Blueprints.JsonSystem;
using Kingmaker.EntitySystem.Entities;
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

        // Разные Кицунэ в игре используют РАЗНЫЕ блупринты баффа/способности смены формы —
        // общий для playable-расы и отдельные, свои собственные у Ненио (подтверждено дампом
        // блупринтов игры, см. Guids.cs). Поэтому отслеживаем МНОЖЕСТВО известных блупринтов,
        // а не один жёстко заданный.
        private static readonly HashSet<BlueprintScriptableObject> HumanFormBuffBlueprints = new HashSet<BlueprintScriptableObject>();
        private static readonly HashSet<BlueprintScriptableObject> ChangeShapeAbilityBlueprints = new HashSet<BlueprintScriptableObject>();

        [HarmonyPatch(typeof(BlueprintsCache), nameof(BlueprintsCache.Init))]
        public static class BlueprintsCache_Init_Patch
        {
            [HarmonyPostfix]
            public static void Postfix()
            {
                _foxIcon = Assets.LoadCustomSprite(AssetFoxIcon);
                _humanIcon = Assets.LoadCustomSprite(AssetHumanIcon);

                HumanFormBuffBlueprints.Clear();
                AddBlueprint(HumanFormBuffBlueprints, Guids.KitsuneHumanBuff);
                AddBlueprint(HumanFormBuffBlueprints, Guids.NenioHumanBuff);
                AddBlueprint(HumanFormBuffBlueprints, Guids.NenioSpecialHumanBuff);

                ChangeShapeAbilityBlueprints.Clear();
                AddBlueprint(ChangeShapeAbilityBlueprints, Guids.KitsuneChangeShapeAbility);
                AddBlueprint(ChangeShapeAbilityBlueprints, Guids.NenioChangeShapeAbility);
            }

            private static void AddBlueprint(HashSet<BlueprintScriptableObject> set, string guid)
            {
                var blueprint = ResourcesLibrary.TryGetBlueprint<BlueprintScriptableObject>(guid);
                if (blueprint != null)
                {
                    set.Add(blueprint);
                }
                else
                {
                    Main.Logger?.Log($"[KitsuneBuffIconPatch] Блупринт не найден по GUID '{guid}' — иконка для него подменяться не будет.");
                }
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

                bool isHumanFormBuff = HumanFormBuffBlueprints.Contains(__instance.Blueprint);
                bool isChangeShapeAbility = !isHumanFormBuff && ChangeShapeAbilityBlueprints.Contains(__instance.Blueprint);

                if (!isHumanFormBuff && !isChangeShapeAbility) return;

                // Владелец факта должен быть персонажем расы Кицунэ — сами блупринты выше и так
                // уникальны для Кицунэ, так что это лишь подстраховка, а не сужение выборки.
                // Портреты владельца этот патч не трогает — только спрайт иконки факта.
                UnitEntityData owner = __instance.Owner?.Unit;
                if (!PortraitManager.IsKitsune(owner)) return;

                if (isHumanFormBuff)
                {
                    if (_humanIcon != null)
                        __result = _humanIcon;
                }
                else if (__instance is ActivatableAbility ability)
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
