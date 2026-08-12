using System.Collections.Generic;
using UnityModManagerNet;

namespace KitsunePortrait
{
    public class PortraitPair
    {
        public string FoxPortrait { get; set; } = "";
        public string HumanPortrait { get; set; } = "";
    }

    public class Settings : UnityModManager.ModSettings
    {
        // Словарь: [UniqueId персонажа] -> { FoxPortrait, HumanPortrait }
        public Dictionary<string, PortraitPair> CharacterPortraits = new Dictionary<string, PortraitPair>();

        public override void Save(UnityModManager.ModEntry modEntry)
        {
            Save(this, modEntry);
        }
    }
}