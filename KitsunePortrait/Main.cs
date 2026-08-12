using System;
using System.Reflection;
using HarmonyLib;
using Kingmaker.PubSubSystem;
using UnityModManagerNet;

namespace KitsunePortrait
{
    public static class Main
    {
        public static UnityModManager.ModEntry ModEntry;
        public static Settings Settings;
        public static UnityModManager.ModEntry.ModLogger Logger;
        public static bool Enabled;

        private static Harmony _harmonyInstance;
        private static KitsuneBuffWatcher _buffWatcher;

        // Временное состояние для экрана создания персонажа (CharGen)
        public static bool IsKitsuneSelectedInCharGen;
        public static string SelectedFoxPortrait = string.Empty;
        public static string TemporaryHumanPortrait = string.Empty;

        public static bool Load(UnityModManager.ModEntry modEntry)
        {
            ModEntry = modEntry;
            Logger = modEntry.Logger;

            Settings = UnityModManager.ModSettings.Load<Settings>(modEntry);

            modEntry.OnToggle = OnToggle;
            modEntry.OnGUI = OnGUI;
            modEntry.OnSaveGUI = OnSaveGUI;

            Logger.Log("KitsunePortrait успешно загружен.");
            return true;
        }

        public static bool OnToggle(UnityModManager.ModEntry modEntry, bool value)
        {
            if (Enabled == value) return true;
            Enabled = value;

            try
            {
                if (Enabled)
                {
                    _harmonyInstance ??= new Harmony(modEntry.Info.Id);
                    _harmonyInstance.PatchAll(Assembly.GetExecutingAssembly());

                    // Используем один экземпляр вочера
                    _buffWatcher ??= new KitsuneBuffWatcher();
                    EventBus.Subscribe(_buffWatcher);

                    Logger.Log("KitsunePortrait включен: Harmony-патчи и подписки применены.");
                }
                else
                {
                    if (_buffWatcher != null)
                    {
                        EventBus.Unsubscribe(_buffWatcher);
                    }

                    _harmonyInstance?.UnpatchAll(modEntry.Info.Id);

                    IsKitsuneSelectedInCharGen = false;
                    SelectedFoxPortrait = string.Empty;
                    TemporaryHumanPortrait = string.Empty;
                    CharGenState.IsInPortraitPhase = false;

                    Logger.Log("KitsunePortrait отключен: Harmony-патчи и подписки сняты.");
                }

                return true;
            }
            catch (Exception ex)
            {
                Logger.Error($"Ошибка при переключении мода: {ex}");
                return false;
            }
        }

        public static void OnGUI(UnityModManager.ModEntry modEntry)
        {
            ModUI.OnGUI(modEntry);
        }

        public static void OnSaveGUI(UnityModManager.ModEntry modEntry)
        {
            Settings.Save(modEntry);
        }
    }
}