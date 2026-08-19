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

        // Ключ конкретного сохранения (SaveInfo.FolderName), которому принадлежит эта запись.
        // Пусто/null — "легаси"-запись, записанная до появления привязки к сохранению
        // (см. Settings.LoadForSave) — при первой загрузке любого сейва после обновления мода
        // она один раз мигрирует в SaveKey этого сейва.
        public string SaveKey;
    }

    public class Settings : UnityModManager.ModSettings
    {
        public float OverlayX = 10f;
        public float OverlayY = 10f;

        public int PortraitBrowserPageSize = 20;

        public List<CharacterPortraitEntry> PortraitEntries = new List<CharacterPortraitEntry>();

        [XmlIgnore]
        public Dictionary<string, PortraitPair> CharacterPortraits = new Dictionary<string, PortraitPair>();

        // Ключ сейва, данные которого сейчас лежат в CharacterPortraits. Не сериализуется —
        // выставляется заново каждую сессию хуками загрузки/сохранения (см. SaveLifecyclePatches).
        // Пока сейв ни разу не был загружен/сохранён в этой сессии (например, мод включили
        // посреди новой игры, ещё ни разу не сохранённой), остаётся пустым — CharacterPortraits
        // в этом случае работает как раньше, без разделения по сейвам (делить нечего: конфликтной
        // "старой" версии сейва ещё не существует).
        [XmlIgnore]
        public string CurrentSaveKey;

        // Обычный "плоский" flush — сохраняет PortraitEntries НА ДИСК КАК ЕСТЬ, не трогая и не
        // пересобирая их из CharacterPortraits. Именно этот метод дергают настройки, не
        // привязанные к конкретному сейву (OverlayX/Y, PortraitBrowserPageSize) — им незачем
        // (и не следует) переносить в постоянную запись живое, ещё не сохранённое в игре
        // состояние портретов. См. SyncCurrentSaveAndFlush для того, что реально обязано это
        // делать — и только в момент настоящего сохранения игры.
        public override void Save(UnityModManager.ModEntry modEntry)
        {
            Save(this, modEntry);
        }

        /// <summary>
        /// Переносит ТЕКУЩЕЕ состояние CharacterPortraits в постоянную запись под
        /// CurrentSaveKey и сохраняет на диск. Вызывать СТРОГО в момент настоящего сохранения
        /// игры (см. SaveLifecyclePatches, хук на SaveManager.PrepareSave) — а не при каждом
        /// изменении портрета в CharGen/UMM.
        ///
        /// Раньше эта логика жила прямо в Save() и вызывалась ИЗ ЛЮБОГО места, что меняло
        /// портрет (CharGenCompletePatch, кнопка Apply в UMM) — из-за этого несохранённые
        /// правки (например, сделанные во время Респека, если игрок ещё не сохранился после
        /// него) немедленно перезаписывали запись ПОСЛЕДНЕГО загруженного/сохранённого сейва
        /// на диске. Баг: сделать сейв №1 с парой (А) → провести респек, получить пару (Б),
        /// НЕ сохраняясь → загрузить сейв №1 — вместо (А) там оказывалась (Б), потому что (Б)
        /// уже успела затереть на диске запись сейва №1 в момент коммита CharGen, хотя игра
        /// в этот момент реально сейв №1 не переписывала. Теперь постоянная запись синкается
        /// с памятью ТОЛЬКО когда игра реально пишет сейв на диск — как и всё остальное её
        /// состояние.
        /// </summary>
        public void SyncCurrentSaveAndFlush(UnityModManager.ModEntry modEntry)
        {
            // Перезаписываем только записи текущего сейва — записи других сохранений (в том
            // числе других прохождений) не трогаем, иначе сохранение в один сейв стирало бы
            // портреты, сохранённые для другого.
            PortraitEntries.RemoveAll(e => e.SaveKey == CurrentSaveKey);

            foreach (var kvp in CharacterPortraits)
            {
                if (kvp.Value != null)
                {
                    PortraitEntries.Add(new CharacterPortraitEntry
                    {
                        CharacterId = kvp.Key,
                        FoxPortrait = kvp.Value.FoxPortrait,
                        HumanPortrait = kvp.Value.HumanPortrait,
                        SaveKey = CurrentSaveKey
                    });
                }
            }

            Save(this, modEntry);
        }

        /// <summary>
        /// Полностью сбрасывает и перечитывает CharacterPortraits под конкретный сейв —
        /// вызывается из хука загрузки сохранения (SaveLifecyclePatches), чтобы не удерживать
        /// портреты, выставленные через UMM или на другом сейве в оперативной памяти, а строго
        /// брать то, что реально было сохранено для ИМЕННО этого файла сохранения.
        /// </summary>
        public void LoadForSave(string saveKey)
        {
            CurrentSaveKey = saveKey ?? string.Empty;
            CharacterPortraits.Clear();

            if (PortraitEntries == null) return;

            // Одноразовая миграция легаси-записей (без SaveKey, от версий мода до привязки
            // к сейву) — считаем, что они принадлежат первому сейву, загруженному после
            // обновления, и помечаем их этим ключом, чтобы больше не "утекали" в другие сейвы.
            bool migrated = false;
            foreach (var entry in PortraitEntries)
            {
                if (string.IsNullOrEmpty(entry.SaveKey))
                {
                    entry.SaveKey = CurrentSaveKey;
                    migrated = true;
                }
            }

            foreach (var entry in PortraitEntries)
            {
                if (entry.SaveKey == CurrentSaveKey && !string.IsNullOrEmpty(entry.CharacterId))
                {
                    CharacterPortraits[entry.CharacterId] = new PortraitPair
                    {
                        FoxPortrait = entry.FoxPortrait,
                        HumanPortrait = entry.HumanPortrait
                    };
                }
            }

            if (migrated)
            {
                Save(Main.ModEntry);
            }
        }
    }
}