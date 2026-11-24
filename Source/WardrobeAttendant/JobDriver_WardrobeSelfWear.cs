using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace WardrobeAttendant
{
    public class JobDriver_WardrobeSelfWear : JobDriver
    {
        private const TargetIndex ApparelInd = TargetIndex.B;

        private Apparel Apparel => (Apparel)job.GetTarget(ApparelInd).Thing;

        private int unequipBuffer;

        private int issuedSlotOrderVersion;

        public override void ExposeData()
        {
            base.ExposeData();

            Scribe_Values.Look(
                ref issuedSlotOrderVersion,
                "issuedSlotOrderVersion",
                0);
        }

        public override bool TryMakePreToilReservations(bool errorOnFailed)
        {
            job.count = 1;

            WardrobeGameComponent component = WardrobeGameComponent.Get;

            issuedSlotOrderVersion = component?.GetSlotOrderVersion(pawn) ?? 0;

            if (!WorkGiver_WardrobeAttendant.IsWearStillValid(pawn, Apparel, pawn))
            {
                return false;
            }

            // Self-service: the pawn must not reserve itself.
            return pawn.Reserve(Apparel, job, 1, -1, null, errorOnFailed, false);
        }

        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOnDestroyedOrNull(ApparelInd);
            this.FailOnDespawnedNullOrForbidden(ApparelInd);
            this.FailOn(() => Apparel == null || !Apparel.Spawned);

            int duration = Mathf.Max(
                1,
                (int)(Apparel.GetStatValue(StatDefOf.EquipDelay) * 60f));

            unequipBuffer = 0;

            bool forbidDroppedApparel =
                pawn.Faction != null &&
                pawn.Faction.HostileTo(Faction.OfPlayer);

            Toil gotoApparel = Toils_Goto.GotoThing(ApparelInd, PathEndMode.ClosestTouch);
            yield return gotoApparel;

            Toil workToil = ToilMaker.MakeToil("WardrobeSelfWearWork");

            workToil.tickIntervalAction = (int delta) =>
            {
                unequipBuffer += delta;

                TryUnequipConflictingApparel(forbidDroppedApparel);
            };

            workToil.WithProgressBarToilDelay(ApparelInd, false, -0.5f);
            workToil.FailOnDestroyedOrNull(ApparelInd);
            workToil.FailOnDespawnedNullOrForbidden(ApparelInd);
            workToil.defaultCompleteMode = ToilCompleteMode.Delay;
            workToil.defaultDuration = duration;

            yield return workToil;

            Toil wear = ToilMaker.MakeToil("WardrobeSelfWearWear");
            wear.initAction = () =>
            {
                if (!IsStillHighestPriorityApparel())
                {
                    EndJobWith(JobCondition.Incompletable);
                    return;
                }

                if (!WorkGiver_WardrobeAttendant.IsWearStillValid(pawn, Apparel, pawn))
                {
                    EndJobWith(JobCondition.Errored);
                    return;
                }

                if (!WorkGiver_WardrobeAttendant.TryGetSelfWearSlot(pawn, Apparel, out WardrobeSlot slot))
                {
                    EndJobWith(JobCondition.Errored);
                    return;
                }

                pawn.apparel.Wear(Apparel, true);
            };
            wear.defaultCompleteMode = ToilCompleteMode.Instant;

            yield return wear;
        }

        private bool IsStillHighestPriorityApparel()
        {
            WardrobeGameComponent component = WardrobeGameComponent.Get;

            if (component == null ||
                issuedSlotOrderVersion == component.GetSlotOrderVersion(pawn))
            {
                return true;
            }

            return WorkGiver_WardrobeAttendant.IsWearStillHighestPriority(
                pawn,
                Apparel,
                pawn,
                issuedSlotOrderVersion);
        }

        private void TryUnequipConflictingApparel(bool forbidDroppedApparel)
        {
            if (!IsStillHighestPriorityApparel())
            {
                EndJobWith(JobCondition.Incompletable);
                return;
            }

            if (!WorkGiver_WardrobeAttendant.TryGetSelfWearSlot(pawn, Apparel, out WardrobeSlot slot))
            {
                EndJobWith(JobCondition.Errored);
                return;
            }

            List<Apparel> wornApparel = pawn.apparel.WornApparel;

            int i = wornApparel.Count - 1;

            while (i >= 0)
            {
                Apparel worn = wornApparel[i];

                if (worn == null || worn == Apparel)
                {
                    i--;
                    continue;
                }

                if (ApparelUtility.CanWearTogether(Apparel.def, worn.def, pawn.RaceProps.body))
                {
                    i--;
                    continue;
                }

                int wornDelay = Mathf.Max(
                    1,
                    (int)(worn.GetStatValue(StatDefOf.EquipDelay) * 60f));

                if (unequipBuffer < wornDelay)
                {
                    break;
                }

                bool locked = WorkGiver_WardrobeAttendant.IsApparelLocked(pawn, worn);

                if (locked && !slot.allowWorkerRemoveLocked)
                {
                    EndJobWith(JobCondition.Errored);
                    return;
                }

                if (!pawn.apparel.TryDrop(
                        worn,
                        out Apparel dropped,
                        pawn.PositionHeld,
                        forbidDroppedApparel))
                {
                    EndJobWith(JobCondition.Errored);
                    return;
                }

                break;
            }
        }
    }
}