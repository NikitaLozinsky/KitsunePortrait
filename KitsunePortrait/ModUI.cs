using Kingmaker;
using Kingmaker.EntitySystem.Entities;
using UnityEngine;
using UnityModManagerNet;

namespace KitsunePortrait
{
    public static class ModUI
    {
        public static void OnGUI(UnityModManager.ModEntry modEntry)
        {
            if (Game.Instance?.Player == null)
            {
                GUILayout.Label("Загрузите сохранение для отображения данных мода.");
                return;
            }

            GUILayout.Label("<b>Kitsune Portrait Switcher</b>");
            GUILayout.Label($"Резервная папка портрета человека: <b>{PortraitManager.FallbackHumanPortraitId}</b>");
            GUILayout.Space(10);

            var party = Game.Instance.Player.Party;
            if (party == null || party.Count == 0) return;

            GUILayout.Label("<b>Установленные портреты персонажей группы:</b>");
            GUILayout.Space(5);

            foreach (UnitEntityData unit in party)
            {
                if (!PortraitManager.IsKitsune(unit)) continue;

                bool inHuman = PortraitManager.IsInHumanForm(unit);
                Main.Settings.CharacterPortraits.TryGetValue(unit.UniqueId, out PortraitPair pair);

                string foxPortrait = !string.IsNullOrEmpty(pair?.FoxPortrait) ? pair.FoxPortrait : "Оригинальный (из игры)";
                string humanPortrait = !string.IsNullOrEmpty(pair?.HumanPortrait) ? pair.HumanPortrait : $"{PortraitManager.FallbackHumanPortraitId} (Резерв)";

                GUILayout.BeginVertical(GUI.skin.box);
                GUILayout.Label($"Персонаж: <b>{unit.CharacterName}</b> | Форма: {(inHuman ? "<color=yellow>Человек</color>" : "<color=orange>Лиса</color>")}");
                GUILayout.Label($"  • Портрет формы лисы: <b><color=#80D0FF>{foxPortrait}</color></b>");
                GUILayout.Label($"  • Портрет формы человека: <b><color=#FFD080>{humanPortrait}</color></b>");
                GUILayout.EndVertical();
                GUILayout.Space(4);
            }
        }
    }
}