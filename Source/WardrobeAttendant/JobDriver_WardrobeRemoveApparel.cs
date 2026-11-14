using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.AI;

namespace WardrobeAttendant
{
    public class JobDriver_WardrobeRemoveApparel : JobDriver
    {
        private const int RemoveWorkTicksPerItem = 90;
        private const int WorkToilIndex = 1;
        private const int TargetHoldBufferTicks = 30;

        private int _totalWorkTicks = RemoveWorkTicksPerItem;

        // Retained for compatibility with saves from the previous driver.
        private int _remainingWorkTicks = RemoveWorkTicksPerItem;

        private Pawn TargetPawn => job?.targetA.Thing as Pawn;

        private Apparel TargetApparel => job?.targetB.Thing as Apparel;

        public override void ExposeData()
        {
            if (Scribe.mode == LoadSaveMode.Saving &&
                CurToilIndex == WorkToilIndex)
            {
                _remainingWorkTicks = ticksLeftThisToil;
            }

            base.ExposeData();

            Scribe_Values.Look(
                ref _totalWorkTicks,
                "totalWorkTicks",
                RemoveWorkTicksPerItem);

            Scribe_Values.Look(
                ref _remainingWorkTicks,
                "remainingWorkTicks",
                RemoveWorkTicksPerItem);

            if (Scribe.mode == LoadSaveMode.LoadingVars &&
                CurToilIndex == WorkToilIndex)
            {
                ticksLeftThisToil = _remainingWorkTicks;
            }
        }

        public override bool TryMakePreToilReservations(bool errorOnFailed)
        {
            Pawn target = TargetPawn;

            if (!IsTargetAvailable(target) ||
                !IsWornByTarget(TargetApparel, target))
            {
                return false;
            }

            return pawn.Reserve(
                target,
                job,
                1,
                -1,
                null,
                errorOnFailed);
        }

        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOnDestroyedOrNull(TargetIndex.A);
            this.FailOnDestroyedOrNull(TargetIndex.B);

            this.FailOn(() =>
                WardrobeModSettingsExtensions.SkipDraftedTargets &&
                TargetPawn?.Drafted == true);

            this.FailOn(() =>
            {
                Pawn target = TargetPawn;

                return !IsTargetAvailable(target) ||
                       !IsWornByTarget(TargetApparel, target);
            });

            yield return Toils_Goto.GotoThing(
                    TargetIndex.A,
                    PathEndMode.ClosestTouch)
                .FailOnDestroyedOrNull(TargetIndex.A);

            Toil workToil = ToilMaker.MakeToil(
                "RemoveWardrobeApparelWork");

            workToil.defaultCompleteMode = ToilCompleteMode.Delay;
            workToil.defaultDuration = RemoveWorkTicksPerItem;

            workToil.initAction = () =>
            {
                pawn.pather.StopDead();

                Pawn target = TargetPawn;
                Apparel primaryApparel = TargetApparel;

                if (!CanRemoveApparelNow(target, primaryApparel))
                {
                    EndJobWith(JobCondition.Incompletable);
                    return;
                }

                int itemCount = 1 + GetAdditionalApparelCount(target);

                _totalWorkTicks = RemoveWorkTicksPerItem * itemCount;
                _remainingWorkTicks = _totalWorkTicks;
                ticksLeftThisToil = _totalWorkTicks;

                HoldTargetForRemoval(target);
            };

            workToil.tickAction = () =>
            {
                Pawn target = TargetPawn;
                Apparel primaryApparel = TargetApparel;

                if (target != null &&
                    target.IsHashIntervalTick(30) &&
                    !CanRemoveApparelNow(target, primaryApparel))
                {
                    EndJobWith(JobCondition.Incompletable);
                }
            };

            workToil.FailOn(() => !CanWorkAtTarget());

            workToil.WithProgressBar(
                TargetIndex.A,
                () => _totalWorkTicks > 0
                    ? 1f - ticksLeftThisToil / (float)_totalWorkTicks
                    : 0f);

            yield return workToil;

            Toil removeToil = ToilMaker.MakeToil(
                "RemoveWardrobeApparelCommit");

            removeToil.defaultCompleteMode = ToilCompleteMode.Instant;

            removeToil.initAction = () =>
            {
                Pawn target = TargetPawn;
                Apparel primaryApparel = TargetApparel;

                if (ticksLeftThisToil > 0 ||
                    !CanWorkAtTarget() ||
                    !IsWornByTarget(primaryApparel, target))
                {
                    EndJobWith(JobCondition.Incompletable);
                    return;
                }

                bool removedAnyApparel = false;

                if (CanRemoveApparelNow(target, primaryApparel))
                {
                    RemoveApparel(target, primaryApparel);
                    removedAnyApparel = true;
                }

                removedAnyApparel |= RemoveStillValidAdditionalApparel(
                    target,
                    primaryApparel);

                if (!removedAnyApparel)
                {
                    EndJobWith(JobCondition.Incompletable);
                }
            };

            yield return removeToil;
        }

        private bool CanRemoveApparelNow(Pawn target, Apparel apparel)
        {
            return IsWornByTarget(apparel, target) &&
                   WorkGiver_WardrobeAttendant.IsRemovalStillValid(
                       target,
                       apparel,
                       pawn);
        }

        private void HoldTargetForRemoval(Pawn target)
        {
            if (target == null ||
                target == pawn ||
                !IsTargetAvailable(target) ||
                target.Drafted ||
                target.InMentalState ||
                target.CurJob?.playerForced == true)
            {
                return;
            }

            PawnUtility.ForceWait(
                target,
                _totalWorkTicks + TargetHoldBufferTicks,
                pawn,
                maintainPosture: true,
                maintainSleep: false);
        }

        private bool RemoveStillValidAdditionalApparel(
            Pawn target,
            Apparel primaryApparel)
        {
            List<LocalTargetInfo> queue = job?.targetQueueB;

            if (queue == null || !IsTargetAvailable(target))
            {
                return false;
            }

            bool removedAnyApparel = false;
            int count = queue.Count;

            for (int i = 0; i < count; i++)
            {
                Apparel apparel = queue[i].Thing as Apparel;

                if (apparel == null ||
                    apparel == primaryApparel ||
                    !CanRemoveApparelNow(target, apparel))
                {
                    continue;
                }

                RemoveApparel(target, apparel);
                removedAnyApparel = true;
            }

            return removedAnyApparel;
        }

        private bool IsTargetAvailable(Pawn target)
        {
            return pawn != null &&
                   pawn.Spawned &&
                   target != null &&
                   !target.Destroyed &&
                   !target.Dead &&
                   target.Spawned &&
                   target.Map == pawn.Map &&
                   target.apparel != null;
        }

        private bool CanWorkAtTarget()
        {
            Pawn target = TargetPawn;

            return IsTargetAvailable(target) &&
                   pawn.Position.AdjacentTo8WayOrInside(target.Position);
        }

        private static bool IsWornByTarget(Apparel apparel, Pawn target)
        {
            return target?.apparel != null &&
                   apparel != null &&
                   !apparel.Destroyed &&
                   apparel.Wearer == target;
        }

        private int GetAdditionalApparelCount(Pawn target)
        {
            List<LocalTargetInfo> queue = job?.targetQueueB;

            if (queue == null)
            {
                return 0;
            }

            int result = 0;
            int count = queue.Count;

            for (int i = 0; i < count; i++)
            {
                Apparel apparel = queue[i].Thing as Apparel;

                if (IsWornByTarget(apparel, target))
                {
                    result++;
                }
            }

            return result;
        }

        private static void RemoveApparel(Pawn target, Apparel apparel)
        {
            if (target == null ||
                !target.Spawned ||
                target.Map == null ||
                !IsWornByTarget(apparel, target))
            {
                return;
            }

            target.apparel.Remove(apparel);

            WorkGiver_WardrobeAttendant.InvalidateExactCandidateCache();

            if (!apparel.Destroyed && apparel.holdingOwner == null)
            {
                GenPlace.TryPlaceThing(
                    apparel,
                    target.Position,
                    target.Map,
                    ThingPlaceMode.Near);
            }
        }
    }
}