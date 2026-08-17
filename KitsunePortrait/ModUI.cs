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
            DrawLanguageSwitcher();
            GUILayout.Space(6);

            if (Game.Instance?.Player == null)
            {
                GUILayout.Label(Localization.Get("Kitsune.ModUI.LoadSaveMessage"));
                return;
            }

            GUILayout.Label(Localization.Get("Kitsune.ModUI.Title"));
            GUILayout.Label(Localization.Get("Kitsune.ModUI.FallbackLine", PortraitManager.FallbackHumanPortraitId));
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

                string formLabel = inHuman
                    ? $"<color=yellow>{Localization.Get("Kitsune.Form.Human")}</color>"
                    : $"<color=orange>{Localization.Get("Kitsune.Form.Fox")}</color>";
                GUILayout.Label(Localization.Get("Kitsune.ModUI.CharacterFormLine", unit.CharacterName, formLabel));
                GUILayout.Space(5);

                // Поле ввода для Лисы
                GUILayout.BeginHorizontal();
                GUILayout.Label(Localization.Get("Kitsune.ModUI.FoxPortraitLabel"), GUILayout.Width(180));
                InputBuffers[foxKey] = GUILayout.TextField(InputBuffers[foxKey], GUILayout.Width(220));
                GUILayout.EndHorizontal();

                // Поле ввода для Человека
                GUILayout.BeginHorizontal();
                GUILayout.Label(Localization.Get("Kitsune.ModUI.HumanPortraitLabel"), GUILayout.Width(180));
                InputBuffers[humanKey] = GUILayout.TextField(InputBuffers[humanKey], GUILayout.Width(220));
                GUILayout.EndHorizontal();

                GUILayout.Space(5);

                // Кнопка применения
                if (GUILayout.Button(Localization.Get("Kitsune.ModUI.ApplyButton"), GUILayout.Width(180), GUILayout.Height(25)))
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

        private static void DrawLanguageSwitcher()
        {
            var locales = Localization.GetAvailableLocales();
            if (locales.Count <= 1) return;

            GUILayout.BeginHorizontal();
            GUILayout.Label(Localization.Get("Kitsune.ModUI.LanguageLabel"), GUILayout.Width(140));

            foreach (string locale in locales)
            {
                bool isActive = locale == Localization.CurrentLocale;
                GUI.enabled = !isActive;

                if (GUILayout.Button(locale, GUILayout.Width(70)))
                {
                    Localization.SetLanguage(locale);
                }

                GUI.enabled = true;
            }

            GUILayout.EndHorizontal();
        }
    }
}