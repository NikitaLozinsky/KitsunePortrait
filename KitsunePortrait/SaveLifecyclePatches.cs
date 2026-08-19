using System;
using System.Collections.Generic;
using HarmonyLib;
using Kingmaker;
using Kingmaker.EntitySystem.Entities;
using Kingmaker.EntitySystem.Persistence;

namespace KitsunePortrait
{
    /// <summary>
    /// Привязывает кэш портретов (Main.Settings.CharacterPortraits) к конкретному файлу
    /// сохранения, а не держит его глобальным между сейвами. Без этого: если поменять
    /// портреты через UMM (что сразу пишет в Main.Settings и на диск), но не сохранить саму
    /// игру, а затем загрузить старый/другой сейв — в памяти оставались новые портреты из
    /// UMM вместо тех, что реально были актуальны для загружаемого сейва (то же самое — при
    /// повторном визите в респек). Хук на SaveManager.LoadRoutine — единственная точка,
    /// через которую проходит абсолютно любая загрузка (обычная, quickload, автосейв,
    /// главное меню), поэтому кэш пересобирается именно здесь, а не через
    /// UnitEntityData.OnAreaDidLoad/IAreaActivationHandler — те срабатывают при ЛЮБОМ переходе
    /// между локациями, не только при загрузке сейва, и не годятся как признак "это именно
    /// загрузка сохранения".
    /// </summary>
    public static class SaveLifecyclePatches
    {
        /// <summary>
        /// LoadRoutine/SaveRoutine — coroutine-методы (IEnumerator&lt;object&gt;). Обычный
        /// Postfix сработал бы только в момент СОЗДАНИЯ enumerator'а, а не по завершении
        /// загрузки/сохранения — поэтому оборачиваем реальный __result в свой enumerator,
        /// который прокачивает оригинальный до конца и только затем выполняет свою логику.
        /// </summary>
        private static IEnumerator<object> WrapWithCallback(IEnumerator<object> original, Action callback)
        {
            while (original.MoveNext())
            {
                yield return original.Current;
            }

            callback();
        }

        private static string GetSaveKey(SaveInfo saveInfo)
        {
            if (saveInfo == null) return null;

            // FolderName — путь к конкретному файлу сохранения (уникален на слот, стабилен при
            // повторных сохранениях в тот же слот). GameId — более грубый, привязан ко всему
            // прохождению целиком; используется только как запасной вариант, пока у сейва ещё
            // нет файла на диске (см. SaveManager.PrepareSave/IsActuallySaved).
            if (!string.IsNullOrEmpty(saveInfo.FolderName)) return saveInfo.FolderName;
            return saveInfo.GameId;
        }

        [HarmonyPatch(typeof(SaveManager), nameof(SaveManager.LoadRoutine))]
        public static class LoadRoutinePatch
        {
            [HarmonyPostfix]
            public static void Postfix(SaveInfo saveInfo, ref IEnumerator<object> __result)
            {
                __result = WrapWithCallback(__result, () => OnLoadCompleted(saveInfo));
            }
        }

        /// <summary>
        /// Именно PrepareSave, а не SaveRoutine. SaveRoutine ВНУТРИ СЕБЯ пересоздаёт локальную
        /// переменную saveInfo как новый объект SaveInfo (копирует только Type/Name) и вызывает
        /// PrepareSave уже на НЕЙ — тот самый объект, что реально получает FolderName и
        /// сохраняется. Постфикс на SaveRoutine видел бы только исходный (внешний) параметр,
        /// который эту подмену не переживает — FolderName на нём так и остаётся пустым.
        /// PrepareSave — обычный (не coroutine) метод, и его параметр save — это как раз
        /// тот самый пересозданный объект, уже с корректно проставленным FolderName.
        /// </summary>
        [HarmonyPatch(typeof(SaveManager), nameof(SaveManager.PrepareSave))]
        public static class PrepareSavePatch
        {
            [HarmonyPostfix]
            public static void Postfix(SaveInfo save)
            {
                OnSaveCompleted(save);
            }
        }

        private static void OnLoadCompleted(SaveInfo saveInfo)
        {
            try
            {
                string saveKey = GetSaveKey(saveInfo);
                if (string.IsNullOrEmpty(saveKey)) return;

                // Полный сброс — забываем всё, что было в памяти (в т.ч. несохранённые правки
                // через UMM/CharGen для другого сейва), и перечитываем только то, что реально
                // сохранено для этого конкретного файла.
                Main.Settings.LoadForSave(saveKey);
                PortraitManager.ClearRuntimeCaches();

                var party = Game.Instance?.Player?.Party;
                if (party != null)
                {
                    foreach (UnitEntityData unit in party)
                    {
                        PortraitManager.UpdatePortrait(unit);
                    }
                }

                Main.Logger?.Log($"[KitsunePortrait] Сейв загружен, кэш портретов синхронизирован ('{saveKey}').");
            }
            catch (Exception ex)
            {
                Main.Logger?.Error($"[KitsunePortrait] Ошибка синхронизации кэша портретов после загрузки: {ex}");
            }
        }

        private static void OnSaveCompleted(SaveInfo saveInfo)
        {
            try
            {
                string saveKey = GetSaveKey(saveInfo);
                if (string.IsNullOrEmpty(saveKey)) return;

                // Это ЕДИНСТВЕННОЕ место, где текущее состояние CharacterPortraits разрешено
                // переноситься в постоянную (на диске) запись сейва — потому что это единственное
                // место, где ИГРА САМА реально пишет сейв на диск. SyncCurrentSaveAndFlush
                // перезаписывает запись ИМЕННО под saveKey (текущий слот — тот же при обычном
                // пересохранении, новый при "Сохранить как"/первом сейве новой игры), остальные
                // сейвы не трогает.
                Main.Settings.CurrentSaveKey = saveKey;
                Main.Settings.SyncCurrentSaveAndFlush(Main.ModEntry);
            }
            catch (Exception ex)
            {
                Main.Logger?.Error($"[KitsunePortrait] Ошибка синхронизации кэша портретов после сохранения: {ex}");
            }
        }
    }
}
