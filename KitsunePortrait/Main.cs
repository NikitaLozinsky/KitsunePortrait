using System;
using System.Reflection;
using HarmonyLib;
using UnityModManagerNet;

namespace KitsunePortrait
{
    public static class Main
    {
        public static UnityModManager.ModEntry.ModLogger Logger;
        public static bool Enabled;
        private static Harmony _harmonyInstance;

        public static bool Load(UnityModManager.ModEntry modEntry)
        {
            Logger = modEntry.Logger;
            modEntry.OnToggle = OnToggle;

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
                    Logger.Log("KitsunePortrait включен: Harmony-патчи применены.");
                }
                else
                {
                    _harmonyInstance?.UnpatchAll(modEntry.Info.Id);
                    Logger.Log("KitsunePortrait отключен: Harmony-патчи сняты.");
                }

                return true;
            }
            catch (Exception ex)
            {
                Logger.Error($"Ошибка при переключении мода: {ex}");
                return false;
            }
        }
    }
}