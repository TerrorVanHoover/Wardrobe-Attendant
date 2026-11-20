using System;
using HarmonyLib;
using RimWorld;
using Verse;

namespace WardrobeAttendant
{
    [HarmonyPatch(typeof(Pawn_ApparelTracker), nameof(Pawn_ApparelTracker.Wear))]
    public static class Pawn_ApparelTracker_Wardrobe_Patch
    {
        public static bool Prefix(
            Pawn_ApparelTracker __instance,
            Apparel newApparel,
            bool dropReplacedApparel,
            ref bool locked)
        {
            try
            {
                Pawn target = __instance?.pawn;
                WardrobeGameComponent component = WardrobeGameComponent.Get;

                if (target == null ||
                    newApparel == null ||
                    component == null ||
                    !component.TryGetLockPending(newApparel, out var pending))
                {
                    return true;
                }

                bool isPolicyAllowed =
                    target.outfits?.CurrentApparelPolicy == null ||
                    target.outfits.CurrentApparelPolicy.filter.Allows(newApparel);

                bool isValid =
                    target.Spawned &&
                    !target.Dead &&
                    target.apparel != null &&
                    !target.apparel.WornApparel.Contains(newApparel) &&
                    !newApparel.Destroyed &&
                    !newApparel.IsBurning() &&
                    !newApparel.IsForbidden(target) &&
                    isPolicyAllowed;

                if (WardrobeModSettingsExtensions.DebugLoggingEnabled)
                {
                    Log.Message(
                        $"[Wardrobe] Wear validation for {target.LabelShortCap}: " +
                        $"{newApparel.def.defName}, " +
                        $"spawned={target.Spawned}, " +
                        $"dead={target.Dead}, " +
                        $"alreadyWorn={target.apparel != null && target.apparel.WornApparel.Contains(newApparel)}, " +
                        $"destroyed={newApparel.Destroyed}, " +
                        $"burning={newApparel.IsBurning()}, " +
                        $"forbidden={newApparel.IsForbidden(target)}, " +
                        $"policyAllowed={isPolicyAllowed}, " +
                        $"corpse={newApparel.WornByCorpse}");
                }

                if (isValid)
                {
                    if (WardrobeModSettingsExtensions.DebugLoggingEnabled)
                    {
                        Log.Message(
                            $"[Wardrobe] Wear Prefix: " +
                            $"pending.slot={(pending.slot != null ? pending.slot.slotName : "null")}, " +
                            $"lockApparel={(pending.slot?.lockApparel ?? false)}");
                    }

                    // Do not use Pawn_ApparelTracker's vanilla locked-apparel state.
                    // It prevents the player from dropping the item. Wardrobe Attendant
                    // applies its optional post-wear lock through forcedHandler instead.
                    locked = false;

                    return true;
                }

                component.ClearLockPending(newApparel);

                if (WardrobeModSettingsExtensions.DebugLoggingEnabled)
                {
                    Log.Message(
                        $"[Wardrobe] Cancelled invalid wear for " +
                        $"{target.LabelShortCap}: " +
                        $"{newApparel.def.defName} is no longer wearable.");
                }

                return false;
            }
            catch (Exception ex)
            {
                Log.Error(
                    "[Wardrobe Attendant] Wear validation patch failed: " + ex);

                return true;
            }
        }

        public static void Postfix(
            Pawn_ApparelTracker __instance,
            Apparel newApparel)
        {
            try
            {
                Pawn pawn = __instance?.pawn;
                WardrobeGameComponent component = WardrobeGameComponent.Get;

                if (WardrobeModSettingsExtensions.DebugLoggingEnabled)
                {
                    Log.Message(
                        $"[Wardrobe] Wear Postfix enter: " +
                        $"pawn={pawn?.LabelShortCap ?? "null"}, " +
                        $"apparel={newApparel?.def.defName ?? "null"}");
                }

                if (pawn == null ||
                    newApparel == null ||
                    component == null)
                {
                    if (WardrobeModSettingsExtensions.DebugLoggingEnabled)
                    {
                        Log.Message(
                            "[Wardrobe] Wear Postfix exit: missing pawn, apparel, or component.");
                    }

                    return;
                }

                bool hasPendingLock =
                    component.TryGetLockPending(newApparel, out var pending);

                if (WardrobeModSettingsExtensions.DebugLoggingEnabled)
                {
                    Log.Message(
                        $"[Wardrobe] Wear Postfix pending: " +
                        $"found={hasPendingLock}, " +
                        $"slot={pending?.slot?.slotName ?? "null"}, " +
                        $"slotEnabled={pending?.slot?.enabled ?? false}, " +
                        $"lockApparel={pending?.slot?.lockApparel ?? false}");
                }

                if (hasPendingLock)
                {
                    component.ClearLockPending(newApparel);
                }

                bool isWorn =
                    pawn.apparel?.WornApparel.Contains(newApparel) == true;

                if (WardrobeModSettingsExtensions.DebugLoggingEnabled)
                {
                    Log.Message(
                        $"[Wardrobe] Wear Postfix worn={isWorn}");
                }

                if (!isWorn)
                {
                    return;
                }

                WorkGiver_WardrobeAttendant.InvalidateExactCandidateCache();

                WardrobeSlot pendingSlot = hasPendingLock ? pending.slot : null;

                if (pendingSlot != null &&
                    pendingSlot.enabled &&
                    pendingSlot.lockApparel)
                {
                    if (WardrobeModSettingsExtensions.DebugLoggingEnabled)
                    {
                        Log.Message(
                            "[Wardrobe] Wear Postfix applying forced lock from pending slot.");
                    }

                    ApplyForcedLock(pawn, newApparel);
                    return;
                }

                if (WardrobeModSettingsExtensions.DebugLoggingEnabled)
                {
                    Log.Message(
                        "[Wardrobe] Wear Postfix pending lock not applicable; checking current slots.");
                }

                if (!component.TryGetSlots(pawn, out var slots))
                {
                    if (WardrobeModSettingsExtensions.DebugLoggingEnabled)
                    {
                        Log.Message(
                            "[Wardrobe] Wear Postfix exit: no wardrobe slots.");
                    }

                    return;
                }

                for (int i = 0; i < slots.Count; i++)
                {
                    WardrobeSlot slot = slots[i];

                    if (slot == null ||
                        !slot.enabled ||
                        !slot.lockApparel)
                    {
                        continue;
                    }

                    bool slotMatches = slot.mode == WardrobeSlotMode.ExactItem
                        ? slot.exactApparelDef == newApparel.def
                        : slot.AllowsApparel(newApparel);

                    if (WardrobeModSettingsExtensions.DebugLoggingEnabled)
                    {
                        Log.Message(
                            $"[Wardrobe] Wear Postfix slot check: " +
                            $"slot={slot.slotName}, " +
                            $"mode={slot.mode}, " +
                            $"matches={slotMatches}");
                    }

                    if (slotMatches)
                    {
                        if (WardrobeModSettingsExtensions.DebugLoggingEnabled)
                        {
                            Log.Message(
                                "[Wardrobe] Wear Postfix applying forced lock from current slot.");
                        }

                        ApplyForcedLock(pawn, newApparel);
                        return;
                    }
                }

                if (WardrobeModSettingsExtensions.DebugLoggingEnabled)
                {
                    Log.Message(
                        "[Wardrobe] Wear Postfix exit: no matching enabled lock slot.");
                }
            }
            catch (Exception ex)
            {
                Log.Error(
                    "[Wardrobe Attendant] Wear lock patch failed: " + ex);
            }
        }

        private static void ApplyForcedLock(Pawn pawn, Apparel apparel)
        {
            if (pawn.outfits?.forcedHandler == null)
            {
                if (WardrobeModSettingsExtensions.DebugLoggingEnabled)
                {
                    Log.Message(
                        "[Wardrobe] ApplyForcedLock exit: forcedHandler is null.");
                }

                return;
            }

            pawn.outfits.forcedHandler.SetForced(apparel, true);

            if (WardrobeModSettingsExtensions.DebugLoggingEnabled)
            {
                Log.Message(
                    $"[Wardrobe] ApplyForcedLock applied: " +
                    $"pawn={pawn.LabelShortCap}, " +
                    $"apparel={apparel.def.defName}, " +
                    $"forced={pawn.outfits.forcedHandler.IsForced(apparel)}");
            }
        }
    }
}