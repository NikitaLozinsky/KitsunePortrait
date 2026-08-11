using System.Collections.Generic;
using UnityModManagerNet;

namespace KitsunePortrait
{
    public class Settings : UnityModManager.ModSettings
    {
        public Dictionary<string, string> HumanPortraits = new Dictionary<string, string>();
        public Dictionary<string, string> FoxPortraits = new Dictionary<string, string>();

        public override void Save(UnityModManager.ModEntry modEntry)
        {
            Save(this, modEntry);
        }
    }
}