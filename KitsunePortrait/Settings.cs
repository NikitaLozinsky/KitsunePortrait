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

        // Временное хранилище на время создания персонажа (пока нет UniqueId)
        public static string TemporaryHumanPortrait = "";

        public override void Save(UnityModManager.ModEntry modEntry)
        {
            Save(this, modEntry);
        }
    }
}