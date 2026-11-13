using RimWorld;
using Verse;

namespace WardrobeAttendant
{
    public static class WardrobeTargetUtility
    {
        public static bool CanConfigureWardrobe(Pawn target)
        {
            if (target == null ||
                target.Destroyed ||
                !target.Spawned ||
                target.Dead ||
                target.apparel == null)
            {
                return false;
            }

            if (target.IsColonist ||
                target.IsSlaveOfColony ||
                target.IsPrisonerOfColony)
            {
                return true;
            }

            return target.RaceProps != null &&
                   target.RaceProps.Animal &&
                   target.Faction == Faction.OfPlayer;
        }
    }
}
