using System.Diagnostics.CodeAnalysis;
using HarmonyLib;
using Kingmaker.EntitySystem.Entities;

namespace KitsunePortrait
{
    [HarmonyPatch(typeof(UnitEntityData), nameof(UnitEntityData.OnAreaDidLoad))]
    public static class UnitAreaLoadPatch
    {
        [SuppressMessage("ReSharper", "InconsistentNaming")]
        public static void Postfix(UnitEntityData __instance)
        {
            if (__instance != null)
            {
                PortraitManager.UpdatePortrait(__instance);
            }
        }
    }
}