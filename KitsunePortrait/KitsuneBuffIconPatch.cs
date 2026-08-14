using System;
using System.Reflection;
using HarmonyLib;
using Kingmaker.Blueprints;
using Kingmaker.Blueprints.JsonSystem;
using Kingmaker.UnitLogic.ActivatableAbilities;
using Kingmaker.UnitLogic.Buffs;
using Kingmaker.UnitLogic.Buffs.Blueprints;
using UnityEngine;

namespace KitsunePortrait
{
    public static class KitsuneBuffIconPatch
    {
        private const string AssetFoxIcon = "icon_form_metamorphose(fox).png";
        private const string AssetHumanIcon = "icon_form_metamorphose(human).png";

        private static Sprite _foxIcon;
        private static Sprite _humanIcon;

        private static BlueprintBuff _kitsuneBuff;
        private static BlueprintActivatableAbility _kitsuneAbility;

        // Кэшированные рефлексией поля и методы для выполнения за 0 мс
        private static FieldInfo _buffIconField;
        private static FieldInfo _abilityIconField;
        private static Action _refreshUiAction;

        // 1. Инициализация и кэширование при старте игры
        [HarmonyPatch(typeof(BlueprintsCache), nameof(BlueprintsCache.Init))]
        public static class BlueprintsCache_Init_Patch
        {
            [HarmonyPostfix]
            public static void Postfix()
            {
                try
                {
                    _foxIcon = Assets.LoadCustomSprite(AssetFoxIcon);
                    _humanIcon = Assets.LoadCustomSprite(AssetHumanIcon);

                    _kitsuneBuff = ResourcesLibrary.TryGetBlueprint<BlueprintBuff>(Guids.KitsuneHumanBuff);
                    _kitsuneAbility = ResourcesLibrary.TryGetBlueprint<BlueprintActivatableAbility>(Guids.KitsuneChangeShapeAbility);

                    // Кэшируем ссылки на приватные поля заранее
                    if (_kitsuneBuff != null)
                        _buffIconField = GetIconField(_kitsuneBuff.GetType());

                    if (_kitsuneAbility != null)
                        _abilityIconField = GetIconField(_kitsuneAbility.GetType());

                    // Кэшируем вызов обновления UI
                    CacheRefreshUiDelegate();

                    // По умолчанию ставим иконку Лисы
                    SetCurrentIcon(_foxIcon);
                }
                catch (Exception ex)
                {
                    Main.Logger?.Error($"[Kitsune] Ошибка инициализации иконок формы: {ex}");
                }
            }
        }

        // 2. Включение баффа -> переход в форму ЧЕЛОВЕКА
        [HarmonyPatch]
        public static class Buff_OnTurnOn_Patch
        {
            [HarmonyTargetMethod]
            public static MethodBase TargetMethod()
            {
                return FindMethodInHierarchy(typeof(Buff), "OnTurnOn");
            }

            [HarmonyPostfix]
            public static void Postfix(Buff __instance)
            {
                if (__instance?.Blueprint?.AssetGuidThreadSafe == Guids.KitsuneHumanBuff)
                {
                    if (_humanIcon == null)
                        _humanIcon = Assets.LoadCustomSprite(AssetHumanIcon);

                    SetCurrentIcon(_humanIcon);
                    _refreshUiAction?.Invoke();
                }
            }
        }

        // 3. Выключение баффа -> возврат в форму ЛИСЫ
        [HarmonyPatch]
        public static class Buff_OnTurnOff_Patch
        {
            [HarmonyTargetMethod]
            public static MethodBase TargetMethod()
            {
                return FindMethodInHierarchy(typeof(Buff), "OnTurnOff");
            }

            [HarmonyPostfix]
            public static void Postfix(Buff __instance)
            {
                if (__instance?.Blueprint?.AssetGuidThreadSafe == Guids.KitsuneHumanBuff)
                {
                    if (_foxIcon == null)
                        _foxIcon = Assets.LoadCustomSprite(AssetFoxIcon);

                    SetCurrentIcon(_foxIcon);
                    _refreshUiAction?.Invoke();
                }
            }
        }

        // Быстрая установка иконки в закэшированные поля
        private static void SetCurrentIcon(Sprite icon)
        {
            if (icon == null) return;

            if (_kitsuneBuff != null && _buffIconField != null)
                _buffIconField.SetValue(_kitsuneBuff, icon);

            if (_kitsuneAbility != null && _abilityIconField != null)
                _abilityIconField.SetValue(_kitsuneAbility, icon);
        }

        // Вспомогательный поиск поля m_Icon (выполняется только 1 раз при запуске)
        private static FieldInfo GetIconField(Type type)
        {
            Type current = type;
            while (current != null && current != typeof(object))
            {
                var field = AccessTools.Field(current, "m_Icon");
                if (field != null) return field;
                current = current.BaseType;
            }
            return null;
        }

        // Однократная сборка делегата вызова EventBus
        private static void CacheRefreshUiDelegate()
        {
            try
            {
                Type eventBusType = AccessTools.TypeByName("Kingmaker.PubSub.EventBus")
                                 ?? AccessTools.TypeByName("Kingmaker.PubSub.Core.EventBus");

                Type handlerType = AccessTools.TypeByName("Kingmaker.UI.Common.IUnitCommandBarHandler")
                                ?? AccessTools.TypeByName("Kingmaker.UI.ActionBar.IUnitActionBarHandler");

                if (eventBusType == null || handlerType == null) return;

                var updateMethod = AccessTools.Method(handlerType, "HandleUnitCommandBarUpdate");
                if (updateMethod == null) return;

                foreach (var method in eventBusType.GetMethods(BindingFlags.Public | BindingFlags.Static))
                {
                    if (method.Name == "RaiseEvent" && method.IsGenericMethodDefinition && method.GetParameters().Length == 1)
                    {
                        var genericMethod = method.MakeGenericMethod(handlerType);
                        var delegateType = typeof(Action<>).MakeGenericType(handlerType);
                        var delegateInstance = Delegate.CreateDelegate(delegateType, null, updateMethod, false);
                        if (delegateInstance != null)
                        {
                            _refreshUiAction = () => genericMethod.Invoke(null, new object[] { delegateInstance });
                        }
                        break;
                    }
                }
            }
            catch (Exception ex)
            {
                Main.Logger?.Error($"[Kitsune] Ошибка кэширования обновления UI: {ex}");
            }
        }

        private static MethodBase FindMethodInHierarchy(Type startType, string methodName)
        {
            Type current = startType;
            while (current != null && current != typeof(object))
            {
                var method = AccessTools.Method(current, methodName);
                if (method != null) return method;
                current = current.BaseType;
            }
            return null;
        }
    }
}