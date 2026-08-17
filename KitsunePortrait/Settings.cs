using System;
using System.Collections.Generic;
using System.Xml.Serialization;
using UnityModManagerNet;

namespace KitsunePortrait
{
    [Serializable]
    public class PortraitPair
    {
        public string FoxPortrait;
        public string HumanPortrait;
    }

    [Serializable]
    public class CharacterPortraitEntry
    {
        public string CharacterId;
        public string FoxPortrait;
        public string HumanPortrait;
    }

    public class Settings : UnityModManager.ModSettings
    {
        // Координаты оверлея
        public float OverlayX = 10f;
        public float OverlayY = 10f;

        // Язык интерфейса мода (код локали, напр. "ruRU", "enGB", "zhCN").
        // Null/пусто = автоопределение по языку игры (см. Localization.ResolveActiveLocale).
        public string Language;

        // Список для XML-сериализации
        public List<CharacterPortraitEntry> PortraitEntries = new List<CharacterPortraitEntry>();

        // Основной словарь для работы в коде (игнорируется при сериализации)
        [XmlIgnore]
        public Dictionary<string, PortraitPair> CharacterPortraits = new Dictionary<string, PortraitPair>();

        public override void Save(UnityModManager.ModEntry modEntry)
        {
            PortraitEntries.Clear();
            foreach (var kvp in CharacterPortraits)
            {
                if (kvp.Value != null)
                {
                    PortraitEntries.Add(new CharacterPortraitEntry
                    {
                        CharacterId = kvp.Key,
                        FoxPortrait = kvp.Value.FoxPortrait,
                        HumanPortrait = kvp.Value.HumanPortrait
                    });
                }
            }

            Save(this, modEntry);
        }

        public void OnAfterLoad()
        {
            CharacterPortraits.Clear();
            if (PortraitEntries != null)
            {
                foreach (var entry in PortraitEntries)
                {
                    if (!string.IsNullOrEmpty(entry.CharacterId))
                    {
                        CharacterPortraits[entry.CharacterId] = new PortraitPair
                        {
                            FoxPortrait = entry.FoxPortrait,
                            HumanPortrait = entry.HumanPortrait
                        };
                    }
                }
            }
        }
    }
}