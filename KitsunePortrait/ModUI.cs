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
                GUILayout.Label("Загрузите сохранение для работы мода.");
                return;
            }

            GUILayout.Label("<b>Kitsune Portrait Switcher (Тестовый режим)</b>");
            GUILayout.Label($"Текущий папка портрета человека: <b>{PortraitManager.HardcodedHumanPortraitId}</b>");
            GUILayout.Space(10);

            var party = Game.Instance.Player.Party;
            if (party == null || party.Count == 0) return;

            foreach (UnitEntityData unit in party)
            {
                bool inHuman = PortraitManager.IsInHumanForm(unit);
                GUILayout.Label($"Персонаж: <b>{unit.CharacterName}</b> | Форма: {(inHuman ? "<color=yellow>Человек</color>" : "<color=orange>Лиса</color>")}");
            }
        }
    }
}