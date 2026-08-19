namespace KitsunePortrait
{
    public static class Guids
    {
        // Раса Кицунэ (BlueprintRace)
        public const string KitsuneRace = "fd188bb7bb0002e49863aec93bfb9d99";

        // Бафф человеческой формы Кицунэ-игрока (BlueprintBuff, KitsunePolymorphBuff.jbp)
        public const string KitsuneHumanBuff = "ee6c7f5437a57ad48aaf47320129df33";

        // Способность "Смена формы" Кицунэ-игрока (BlueprintActivatableAbility,
        // ChangeShapeKitsuneToggleAbility.jbp)
        public const string KitsuneChangeShapeAbility = "4252c9d9a25549146b8683c5ea45e14e";

        // Ненио (компаньон расы Кицунэ, m_Race = KitsuneRace — подтверждено дампом блупринтов)
        // использует СВОИ ОТДЕЛЬНЫЕ блупринты способности/баффа смены формы, а не общие для
        // playable-расы — поэтому их нужно отслеживать отдельно (см. KitsuneBuffIconPatch).
        // Найдено через официальный дамп блупринтов игры (blueprints.zip):
        //   Traits/Races/Kitsune/ChangeShapeKitsune/ChangeShapeKitsuneToggleAbility_Nenio.jbp
        //   Traits/Races/Kitsune/ChangeShapeKitsune/KitsunePolymorphBuff_Nenio.jbp
        //   Traits/Races/Kitsune/ChangeShapeKitsune/KitsunePolymorphBuff_NenioSpecial.jbp
        public const string NenioChangeShapeAbility = "52bed4c5617625e4faf029b5c750667f";
        public const string NenioHumanBuff = "a13e2e71485901045b1722824019d6f5";

        // Отдельный бафф превращения, применяемый во время сюжетных Etude (см.
        // NenioSpecialPolymorphWhileEtudePlaying в игре) — используется другой блупринт баффа,
        // но не отдельная способность.
        public const string NenioSpecialHumanBuff = "047b715d404d5f245ad37019b5b6f1de";
    }
}
