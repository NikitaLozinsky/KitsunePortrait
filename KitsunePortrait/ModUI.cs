using System.Collections.Generic;
using Kingmaker;
using Kingmaker.EntitySystem.Entities;
using UnityEngine;
using UnityModManagerNet;

namespace KitsunePortrait
{
    public static class ModUI
    {
        // Буферы ввода для текстовых полей [UnitUniqueId_Form -> Text]
        private static readonly Dictionary<string, string> InputBuffers = new Dictionary<string, string>();

        public static void OnGUI(UnityModManager.ModEntry modEntry)
        {
            if (Game.Instance?.Player == null)
            {
                GUILayout.Label("Загрузите сохранение для управления портретами.");
                return;
            }

            GUILayout.Label("<b>Kitsune Portrait Switcher — Управление портретами</b>");
            GUILayout.Label($"Резервный портрет человека по умолчанию: <b>{PortraitManager.FallbackHumanPortraitId}</b>");
            GUILayout.Space(10);

            var party = Game.Instance.Player.Party;
            if (party == null || party.Count == 0) return;

            foreach (UnitEntityData unit in party)
            {
                if (!PortraitManager.IsKitsune(unit)) continue;

                string unitId = unit.UniqueId;
                bool inHuman = PortraitManager.IsInHumanForm(unit);

                if (!Main.Settings.CharacterPortraits.TryGetValue(unitId, out PortraitPair pair))
                {
                    pair = new PortraitPair();
                    Main.Settings.CharacterPortraits[unitId] = pair;
                }

                // Инициализация буферов ввода при первом открытии
                string foxKey = unitId + "_fox";
                string humanKey = unitId + "_human";

                if (!InputBuffers.ContainsKey(foxKey)) InputBuffers[foxKey] = pair.FoxPortrait ?? "";
                if (!InputBuffers.ContainsKey(humanKey)) InputBuffers[humanKey] = pair.HumanPortrait ?? "";

                GUILayout.BeginVertical(GUI.skin.box);
                
                GUILayout.Label($"<b>{unit.CharacterName}</b> | Форма в игре: {(inHuman ? "<color=yellow>Человек</color>" : "<color=orange>Лиса</color>")}");
                GUILayout.Space(5);

                // Поле ввода для Лисы
                GUILayout.BeginHorizontal();
                GUILayout.Label("Портрет Лисы (ID / GUID):", GUILayout.Width(180));
                InputBuffers[foxKey] = GUILayout.TextField(InputBuffers[foxKey], GUILayout.Width(220));
                GUILayout.EndHorizontal();

                // Поле ввода для Человека
                GUILayout.BeginHorizontal();
                GUILayout.Label("Портрет Человека (ID / GUID):", GUILayout.Width(180));
                InputBuffers[humanKey] = GUILayout.TextField(InputBuffers[humanKey], GUILayout.Width(220));
                GUILayout.EndHorizontal();

                GUILayout.Space(5);

                // Кнопка применения
                if (GUILayout.Button("Применить изменения", GUILayout.Width(180), GUILayout.Height(25)))
                {
                    pair.FoxPortrait = InputBuffers[foxKey].Trim();
                    pair.HumanPortrait = InputBuffers[humanKey].Trim();

                    Main.Settings.Save(modEntry);

                    // Сразу форсированно обновляем портрет в игре
                    PortraitManager.UpdatePortrait(unit);
                    Main.Logger?.Log($"[ModUI] Обновлены портреты для {unit.CharacterName}. Лиса: '{pair.FoxPortrait}', Человек: '{pair.HumanPortrait}'");
                }

                GUILayout.EndVertical();
                GUILayout.Space(8);
            }
        }
    }
}