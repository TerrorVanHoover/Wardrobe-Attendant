using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;

namespace WardrobeAttendant
{
    [HarmonyPatch(
        typeof(JobGiver_OptimizeApparel),
        "TryGiveJob")]
    public static class JobGiver_OptimizeApparel_TryGiveJob_Patch
    {
        public static void Postfix(Pawn pawn, ref Job __result)
        {
            try
            {
                if (!WardrobeModSettingsExtensions
                        .PreventVanillaSelfEquippingRemovalTargets)
                {
                    return;
                }

                if (__result == null ||
                    __result.def != JobDefOf.Wear)
                {
                    return;
                }

                Apparel apparel = __result.targetA.Thing as Apparel;

                if (apparel == null ||
                    !WardrobeRemovalRules.ShouldPreventSelfEquipping(
                        pawn,
                        apparel))
                {
                    return;
                }

                __result = null;
            }
            catch (System.Exception ex)
            {
                Log.Error(
                    "[Wardrobe Attendant] Vanilla apparel optimization patch failed: " +
                    ex);
            }
        }
    }

    public static class WardrobeRemovalRules
    {
        public static bool ShouldPreventSelfEquipping(
            Pawn pawn,
            Apparel apparel)
        {
            if (pawn == null ||
                apparel == null ||
                apparel.Destroyed ||
                pawn.apparel == null ||
                pawn.outfits?.forcedHandler == null ||
                pawn.outfits.forcedHandler.IsForced(apparel))
            {
                return false;
            }

            if (WardrobeDisplacedApparelTracker.IsRecentlyDisplaced(apparel))
            {
                return true;
            }

            WardrobeGameComponent component = WardrobeGameComponent.Get;

            if (component == null ||
                !component.TryGetSlots(pawn, out List<WardrobeSlot> slots))
            {
                return false;
            }

            int count = slots.Count;

            for (int i = 0; i < count; i++)
            {
                WardrobeSlot slot = slots[i];

                if (slot == null || !slot.enabled)
                {
                    continue;
                }

                bool failsQualityFilter =
                    !slot.MatchesApparelIgnoreDurability(apparel);

                bool belowDurabilityThreshold =
                    slot.useDurabilityThreshold &&
                    GetCondition(apparel) < slot.durabilityThreshold;

                if (slot.removeAtDurabilityThreshold &&
                    (failsQualityFilter || belowDurabilityThreshold))
                {
                    return true;
                }

                if (belowDurabilityThreshold &&
                    HasVanillaReplacement(pawn, apparel, slot))
                {
                    return true;
                }

                if (slot.mode == WardrobeSlotMode.ExactItem)
                {
                    if (apparel.def == slot.exactApparelDef &&
                        failsQualityFilter)
                    {
                        return true;
                    }

                    continue;
                }

                if (failsQualityFilter)
                {
                    return true;
                }
            }

            return false;
        }

        private static bool HasVanillaReplacement(
            Pawn pawn,
            Apparel oldApparel,
            WardrobeSlot slot)
        {
            Map map = pawn.Map;

            if (map == null)
            {
                return false;
            }

            ApparelPolicy policy = pawn.outfits?.CurrentApparelPolicy;
            List<Thing> mapApparel =
                map.listerThings.ThingsInGroup(ThingRequestGroup.Apparel);

            int count = mapApparel.Count;

            for (int i = 0; i < count; i++)
            {
                if (mapApparel[i] is not Apparel candidate ||
                    candidate == oldApparel ||
                    candidate.Destroyed ||
                    candidate.IsBurning() ||
                    candidate.IsForbidden(pawn))
                {
                    continue;
                }

                if (slot.mode == WardrobeSlotMode.ExactItem &&
                    candidate.def != slot.exactApparelDef)
                {
                    continue;
                }

                if (!slot.AllowsApparel(candidate) ||
                    !slot.AllowsCorpseApparel(candidate))
                {
                    continue;
                }

                if (policy != null &&
                    !policy.filter.Allows(candidate))
                {
                    continue;
                }

                if (!ApparelUtility.HasPartsToWear(pawn, candidate.def) ||
                    candidate.def.apparel == null ||
                    !candidate.def.apparel.PawnCanWear(pawn))
                {
                    continue;
                }

                return true;
            }

            return false;
        }

        private static float GetCondition(Apparel apparel)
        {
            return apparel.HitPoints /
                   (float)System.Math.Max(1, apparel.MaxHitPoints);
        }
    }
}