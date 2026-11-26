using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;

namespace WardrobeAttendant
{
    [HarmonyPatch(typeof(JobDriver), nameof(JobDriver.ReadyForNextToil))]
    public static class JobDriver_ReadyForNextToil_Wardrobe_Patch
    {
        public static bool Prefix(JobDriver __instance)
        {
            try
            {
                Job job = __instance?.job;

                if (job == null)
                {
                    return true;
                }

                JobDef def = job.def;

                bool isForceWear = def == JobDefOf.ForceTargetWear;
                bool isSelfWear =
                    def == WardrobeJobDefOf.WardrobeAttendant_SelfWearApparel;

                if (!isForceWear && !isSelfWear)
                {
                    return true;
                }

                Pawn target = job.targetA.Thing as Pawn;
                Apparel apparel = job.targetB.Thing as Apparel;

                if (target == null || apparel == null)
                {
                    return true;
                }

                if (!WorkGiver_WardrobeAttendant.IsTargetUnavailableForService(target))
                {
                    return true;
                }

                WardrobeGameComponent component = WardrobeGameComponent.Get;

                component?.ClearLockPending(apparel);
                component?.ClearActiveWearJob(apparel);

                if (WardrobeModSettingsExtensions.DebugLoggingEnabled)
                {
                    Log.Message(
                        $"[Wardrobe] Cancelled {def.defName} before toil start for " +
                        $"{target.LabelShortCap}: " +
                        $"{apparel.def.defName} because the target is unavailable " +
                        $"for wardrobe service.");
                }

                __instance.EndJobWith(JobCondition.Incompletable);
                return false;
            }
            catch (System.Exception ex)
            {
                Log.Error(
                    "[Wardrobe Attendant] ReadyForNextToil validation failed: " +
                    ex);

                return true;
            }
        }
    }
}