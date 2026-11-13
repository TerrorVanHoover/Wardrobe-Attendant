using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;

namespace WardrobeAttendant
{
	[HarmonyPatch(typeof(JobDriver), nameof(JobDriver.Cleanup))]
	public static class JobDriver_Cleanup_WardrobeLifecycle_Patch
	{
		public static void Postfix(JobDriver __instance, JobCondition condition)
		{
			try
			{
				Job job = __instance?.job;

                if (job == null ||
    (job.def != WardrobeJobDefOf.WardrobeAttendant_RemoveApparel &&
     job.def != JobDefOf.ForceTargetWear))
                {
                    return;
                }

                if (WardrobeModSettingsExtensions.DebugLoggingEnabled &&
                    job.def == JobDefOf.ForceTargetWear)
                {
                    Log.Message(
                        $"[Wardrobe] ForceTargetWear cleanup: " +
                        $"condition={condition}, " +
                        $"target={job.targetA.Thing?.LabelShortCap ?? "null"}, " +
                        $"apparel={job.targetB.Thing?.LabelShortCap ?? "null"}");
                }

                WardrobeGameComponent.Get?
                    .ClearPendingJobsForJob(job, condition);
            }
			catch (System.Exception ex)
			{
				Log.Error(
					"[Wardrobe Attendant] Job lifecycle cleanup failed: " + ex);
			}
		}
	}
}