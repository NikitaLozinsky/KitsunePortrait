using System;
using System.Collections;
using System.Reflection;
using HarmonyLib;
using Kingmaker.Blueprints;
using Kingmaker.Blueprints.Classes;
using Kingmaker.EntitySystem.Entities;
using Kingmaker.UI.MVVM._VM.CharGen;
using Kingmaker.UI.MVVM._VM.CharGen.Phases.Portrait;
using Kingmaker.UI.MVVM._VM.CharGen.Phases.Race;
using Kingmaker.UnitLogic.Class.LevelUp;

namespace KitsunePortrait
{
    public static class CharGenState
    {
        // Храним ссылку на фазу "Человек" как object, чтобы не зависеть от типа
        public static object HumanPortraitPhaseInstance;
    }

    // 1. Патч на конструктор CharGenVM – добавляем вторую фазу портрета
    [HarmonyPatch(typeof(CharGenVM))]
    [HarmonyPatch(MethodType.Constructor, typeof(LevelUpController))]
    public static class CharGenVM_Constructor_Patch
    {
        public static void Postfix(CharGenVM __instance, LevelUpController levelUpController)
        {
            try
            {
                // Получаем список фаз
                var phasesList = Traverse.Create(__instance).Field("m_Phases").GetValue() as IList;
                if (phasesList == null) return;

                // Проверяем, не добавлена ли уже наша фаза (по DisplayName)
                foreach (var phase in phasesList)
                {
                    var displayName = Traverse.Create(phase).Field("m_DisplayName").GetValue<string>();
                    if (displayName == "Человек")
                        return;
                }

                // Создаём новую фазу портрета и меняем её DisplayName на "Человек"
                var humanPhase = new CharGenPortraitPhaseVM(levelUpController);
                Traverse.Create(humanPhase).Field("m_DisplayName").SetValue("Человек");
                CharGenState.HumanPortraitPhaseInstance = humanPhase;

                // Вставляем на вторую позицию (после основной фазы портрета)
                phasesList.Insert(2, humanPhase);
                Main.Logger.Log("[CharGen] Фаза 'Человек' добавлена в m_Phases.");

                // Теперь пытаемся добавить кнопку в навигацию
                // Вариант 1: поле m_NavigationEntities (если есть)
                var navField = Traverse.Create(__instance).Field("m_NavigationEntities");
                if (navField.FieldExists())
                {
                    var navList = navField.GetValue() as IList;
                    if (navList != null)
                    {
                        // Получаем тип CharGenNavigationEntityVM через рефлексию
                        Type navEntityType = Type.GetType("Kingmaker.UI.MVVM._VM.CharGen.CharGenNavigationEntityVM, Assembly-CSharp");
                        if (navEntityType != null)
                        {
                            // Ищем конструктор с параметром (object) или (object, bool)
                            var ctor = navEntityType.GetConstructor(new[] { typeof(object) });
                            if (ctor == null)
                                ctor = navEntityType.GetConstructor(new[] { typeof(object), typeof(bool) });
                            if (ctor != null)
                            {
                                // Создаём экземпляр кнопки, передавая нашу фазу
                                object navEntity = ctor.Invoke(new object[] { humanPhase, false });
                                if (navEntity != null)
                                {
                                    // Вставляем кнопку на ту же позицию (после основной кнопки портрета)
                                    navList.Insert(2, navEntity);
                                    Main.Logger.Log("[CharGen] Кнопка 'Человек' добавлена в m_NavigationEntities.");
                                }
                            }
                        }
                    }
                }
                else
                {
                    // Вариант 2: если поля нет, пытаемся вызвать метод обновления навигации
                    var updateMethod = AccessTools.Method(typeof(CharGenVM), "UpdateNavigation") 
                                       ?? AccessTools.Method(typeof(CharGenVM), "RefreshNavigation")
                                       ?? AccessTools.Method(typeof(CharGenVM), "BuildNavigation");
                    if (updateMethod != null)
                    {
                        updateMethod.Invoke(__instance, Array.Empty<object>());
                        Main.Logger.Log("[CharGen] Навигация обновлена через вызов метода.");
                    }
                    else
                    {
                        // Вариант 3: пробуем вызвать RaisePropertyChanged для свойства NavigationEntities
                        var prop = AccessTools.Property(typeof(CharGenVM), "NavigationEntities");
                        if (prop != null && prop.CanRead)
                        {
                            var raiseMethod = AccessTools.Method(typeof(CharGenVM), "RaisePropertyChanged", new[] { typeof(string) });
                            if (raiseMethod != null)
                            {
                                raiseMethod.Invoke(__instance, new object[] { "NavigationEntities" });
                                Main.Logger.Log("[CharGen] Вызван RaisePropertyChanged для NavigationEntities.");
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Main.Logger.Error($"[CharGen] Ошибка в конструкторе: {ex}");
            }
        }
    }

    // 2. Патч на выбор расы – запоминаем, выбрана ли кицунэ
    [HarmonyPatch(typeof(CharGenRacePhaseVM), "SelectRaceInMechanic")]
    public static class CharGenRaceSelectPatch
    {
        public static void Postfix(BlueprintRace race)
        {
            if (race == null) return;
            // Используем единый GUID из PortraitManager
            bool isKitsune = race.AssetGuidThreadSafe == PortraitManager.KitsuneRaceGuid;
            Main.IsKitsuneSelectedInCharGen = isKitsune;
            Main.Logger.Log($"[CharGen] Выбрана раса: '{race.name}'. Кицунэ? {isKitsune}");
        }
    }

    // 3. Патч на выбор портрета – определяем, какая вкладка активна
    [HarmonyPatch(typeof(CharGenPortraitPhaseVM), "UpdatePortraitInLevelupController")]
    public static class CharGenPortraitSelectPatch
    {
        public static bool Prefix(CharGenPortraitPhaseVM __instance, BlueprintPortrait portrait)
        {
            if (portrait == null) return true;
            string portraitId = portrait.Data?.CustomId ?? portrait.name;

            // Проверяем DisplayName фазы
            string displayName = Traverse.Create(__instance).Field("m_DisplayName").GetValue<string>();
            if (displayName == "Человек")
            {
                // Это наша фаза – запоминаем портрет человека
                Main.TemporaryHumanPortrait = portraitId;
                Main.Logger.Log($"[CharGen] Выбран портрет человека: '{portraitId}'");
            }
            else
            {
                // Основная фаза – если выбрана кицунэ, запоминаем как лисью
                if (Main.IsKitsuneSelectedInCharGen)
                {
                    Main.SelectedFoxPortrait = portraitId;
                    Main.Logger.Log($"[CharGen] Выбран портрет лисы: '{portraitId}'");
                }
            }
            return true;
        }
    }

    // 4. Патч на завершение генерации – сохраняем оба портрета
    [HarmonyPatch(typeof(CharGenContextVM), "CompleteCharGen")]
    public static class CharGenCompletePatch
    {
        public static void Prefix(CharGenContextVM __instance)
        {
            try
            {
                var controller = Traverse.Create(__instance).Field("m_LevelUpController").GetValue<LevelUpController>();
                var unit = controller?.Unit;
                if (unit == null) return;

                bool isKitsune = Main.IsKitsuneSelectedInCharGen ||
                                 (unit.Progression?.Race != null &&
                                  unit.Progression.Race.AssetGuidThreadSafe == PortraitManager.KitsuneRaceGuid);

                if (!isKitsune) return;

                string unitId = unit.UniqueId;
                if (!Main.Settings.CharacterPortraits.ContainsKey(unitId))
                    Main.Settings.CharacterPortraits[unitId] = new PortraitPair();

                if (!string.IsNullOrEmpty(Main.SelectedFoxPortrait))
                    Main.Settings.CharacterPortraits[unitId].FoxPortrait = Main.SelectedFoxPortrait;

                if (!string.IsNullOrEmpty(Main.TemporaryHumanPortrait))
                {
                    Main.Settings.CharacterPortraits[unitId].HumanPortrait = Main.TemporaryHumanPortrait;
                    Main.TemporaryHumanPortrait = string.Empty;
                }

                Main.Settings.Save(Main.ModEntry);
                Main.Logger.Log($"[CharGen] Сохранены портреты для {unit.CharacterName}");
            }
            catch (Exception ex)
            {
                Main.Logger.Error($"[CharGen] Ошибка в CompleteCharGen: {ex}");
            }
        }
    }
}