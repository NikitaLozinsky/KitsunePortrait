using Kingmaker.EntitySystem.Entities;
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
            if (buff?.Blueprint?.AssetGuidThreadSafe == PortraitManager.KitsuneHumanBuffGuid)
            {
                Main.Logger?.Log($"[KitsunePortrait] Перехвачено изменение баффа смены формы у {buff.Owner?.CharacterName}!");
                PortraitManager.UpdatePortrait(buff.Owner);
            }
        }
    }
}