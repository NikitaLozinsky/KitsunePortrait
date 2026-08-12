using Kingmaker.PubSubSystem;
using Kingmaker.UnitLogic.Buffs;

namespace KitsunePortrait
{
    public class KitsuneBuffWatcher : IUnitBuffHandler
    {
        public void HandleBuffDidAdded(Buff buff)
        {
            CheckAndApply(buff);
        }

        public void HandleBuffDidRemoved(Buff buff)
        {
            CheckAndApply(buff);
        }

        private void CheckAndApply(Buff buff)
        {
            if (buff?.Blueprint?.AssetGuidThreadSafe == Guids.KitsuneHumanBuff)
            {
                Main.Logger?.Log($"[KitsunePortrait] Изменение формы у {buff.Owner?.CharacterName}");
                PortraitManager.UpdatePortrait(buff.Owner);
            }
        }
    }
}