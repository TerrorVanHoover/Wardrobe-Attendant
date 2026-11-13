using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.AI;

namespace WardrobeAttendant
{
    public class WardrobeGameComponent : GameComponent
    {
        private const int CurrentSaveVersion = 3;
        private const int PendingJobDurationTicks = 600;
        private const int CleanupIntervalTicks = 600;

        private int saveVersion;
        private int lastCleanupTick = -CleanupIntervalTicks;

        private List<WardrobeAssignment> assignments = new List<WardrobeAssignment>();

        private readonly Dictionary<int, WardrobeAssignment> assignmentsByPawnId =
            new Dictionary<int, WardrobeAssignment>();

        private readonly Dictionary<int, PendingWardrobeJob> pendingTargetJobs =
            new Dictionary<int, PendingWardrobeJob>();

        private readonly Dictionary<int, PendingWardrobeJob> pendingRemoveApparelJobs =
            new Dictionary<int, PendingWardrobeJob>();

        private readonly Dictionary<int, PendingWardrobeJob> pendingLockApparelJobs =
            new Dictionary<int, PendingWardrobeJob>();

        private static readonly List<int> ExpiredPendingJobIds =
            new List<int>();

        public WardrobeGameComponent(Game game)
        {
        }

        public static WardrobeGameComponent Get =>
            Current.Game?.GetComponent<WardrobeGameComponent>();

        public WardrobeAssignment GetAssignment(Pawn pawn)
        {
            if (pawn == null)
            {
                return null;
            }

            if (assignmentsByPawnId.TryGetValue(pawn.thingIDNumber, out var cached) &&
                cached != null &&
                cached.target == pawn)
            {
                return cached;
            }

            var assignment = new WardrobeAssignment
            {
                target = pawn
            };

            assignments.Add(assignment);
            assignmentsByPawnId[pawn.thingIDNumber] = assignment;

            return assignment;
        }

        public void NotifySlotModeChanged(Pawn pawn, WardrobeSlot slot)
        {
            if (pawn == null ||
                slot == null ||
                slot.mode != WardrobeSlotMode.FullOutfit)
            {
                return;
            }

            if (!TryGetAssignment(pawn, out var assignment))
            {
                return;
            }

            if (HasActiveFullOutfit(assignment, slot))
            {
                slot.enabled = false;

                ShowDuplicateSlotMessage(
                    "WardrobeAttendant.Slot.DuplicateFullOutfit");
            }
        }

        public void NotifySlotEnabled(Pawn pawn, WardrobeSlot slot)
        {
            if (pawn == null ||
                slot == null ||
                !slot.enabled ||
                !TryGetAssignment(pawn, out var assignment))
            {
                return;
            }

            if (slot.mode == WardrobeSlotMode.FullOutfit)
            {
                DisableOtherActiveFullOutfits(assignment, slot);
                return;
            }

            if (slot.mode == WardrobeSlotMode.ExactItem &&
                slot.exactApparelDef != null)
            {
                DisableOtherActiveExactSlots(
                    assignment,
                    slot,
                    slot.exactApparelDef);
            }
        }

        public void NotifyExactDefSelected(Pawn pawn, WardrobeSlot slot)
        {
            if (pawn == null ||
                slot == null ||
                slot.exactApparelDef == null ||
                !TryGetAssignment(pawn, out var assignment))
            {
                return;
            }

            if (slot.enabled &&
                HasOtherActiveExactSlot(
                    assignment,
                    slot,
                    slot.exactApparelDef))
            {
                slot.enabled = false;

                ShowDuplicateSlotMessage(
                    "WardrobeAttendant.Slot.DuplicateExactItem");
            }
        }

        private static bool HasActiveFullOutfit(
            WardrobeAssignment assignment,
            WardrobeSlot excludeSlot)
        {
            if (assignment?.slots == null)
            {
                return false;
            }

            for (int i = 0; i < assignment.slots.Count; i++)
            {
                var slot = assignment.slots[i];

                if (slot == null ||
                    slot == excludeSlot ||
                    !slot.enabled ||
                    slot.mode != WardrobeSlotMode.FullOutfit)
                {
                    continue;
                }

                return true;
            }

            return false;
        }

        private static void DisableOtherActiveFullOutfits(
            WardrobeAssignment assignment,
            WardrobeSlot keepSlot)
        {
            if (assignment?.slots == null)
            {
                return;
            }

            for (int i = 0; i < assignment.slots.Count; i++)
            {
                var slot = assignment.slots[i];

                if (slot == null ||
                    slot == keepSlot ||
                    !slot.enabled ||
                    slot.mode != WardrobeSlotMode.FullOutfit)
                {
                    continue;
                }

                slot.enabled = false;
            }
        }

        private static bool HasOtherActiveExactSlot(
            WardrobeAssignment assignment,
            WardrobeSlot excludeSlot,
            ThingDef apparelDef)
        {
            if (assignment?.slots == null ||
                apparelDef == null)
            {
                return false;
            }

            for (int i = 0; i < assignment.slots.Count; i++)
            {
                var slot = assignment.slots[i];

                if (slot == null ||
                    slot == excludeSlot ||
                    !slot.enabled ||
                    slot.mode != WardrobeSlotMode.ExactItem ||
                    slot.exactApparelDef != apparelDef)
                {
                    continue;
                }

                return true;
            }

            return false;
        }

        private static void DisableOtherActiveExactSlots(
            WardrobeAssignment assignment,
            WardrobeSlot keepSlot,
            ThingDef apparelDef)
        {
            if (assignment?.slots == null ||
                apparelDef == null)
            {
                return;
            }

            for (int i = 0; i < assignment.slots.Count; i++)
            {
                var slot = assignment.slots[i];

                if (slot == null ||
                    slot == keepSlot ||
                    !slot.enabled ||
                    slot.mode != WardrobeSlotMode.ExactItem ||
                    slot.exactApparelDef != apparelDef)
                {
                    continue;
                }

                slot.enabled = false;
            }
        }

        private static void ShowDuplicateSlotMessage(string key)
        {
            string message = key.Translate().ToString();

            Find.WindowStack?.Add(new WardrobeMessageWindow(message));
        }

        public bool TryGetAssignment(Pawn pawn, out WardrobeAssignment assignment)
        {
            assignment = null;

            if (pawn == null)
            {
                return false;
            }

            if (assignmentsByPawnId.TryGetValue(pawn.thingIDNumber, out var cached) &&
                cached != null &&
                cached.target == pawn)
            {
                assignment = cached;
                return true;
            }

            for (int i = 0; i < assignments.Count; i++)
            {
                var candidate = assignments[i];

                if (candidate != null && candidate.target == pawn)
                {
                    assignmentsByPawnId[pawn.thingIDNumber] = candidate;
                    assignment = candidate;
                    return true;
                }
            }

            return false;
        }

        public List<WardrobeSlot> GetSlots(Pawn pawn)
        {
            return TryGetAssignment(pawn, out var assignment)
                ? assignment.slots
                : null;
        }

        public bool TryGetSlots(Pawn pawn, out List<WardrobeSlot> slots)
        {
            slots = null;

            if (!TryGetAssignment(pawn, out var assignment) ||
                assignment.slots == null)
            {
                return false;
            }

            slots = assignment.slots;
            return slots.Count > 0;
        }

        public WardrobeSlot AddSlot(Pawn pawn)
        {
            var assignment = GetAssignment(pawn);

            if (assignment == null)
            {
                return null;
            }

            bool isFirstSlot = assignment.slots.Count == 0;

            var slot = new WardrobeSlot
            {
                slotName = "WardrobeAttendant.DefaultSlotName".Translate(
                    assignment.slots.Count + 1).ToString(),
                mode = isFirstSlot
                    ? WardrobeSlotMode.FullOutfit
                    : WardrobeSlotMode.ExactItem,
                enabled = true
            };

            assignment.slots.Add(slot);

            return slot;
        }

        public void RemoveSlot(Pawn pawn, WardrobeSlot slot)
        {
            if (!TryGetAssignment(pawn, out var assignment) ||
                assignment.slots == null ||
                slot == null)
            {
                return;
            }

            assignment.slots.Remove(slot);
        }

        public bool IsTargetJobPending(Pawn target)
        {
            return IsPending(pendingTargetJobs, target?.thingIDNumber ?? -1);
        }

        public bool TryMarkTargetJobPending(Pawn target, Job job)
        {
            return TryMarkPending(pendingTargetJobs, target, job);
        }

        public bool IsRemovalPending(Apparel apparel)
        {
            return IsPending(
                pendingRemoveApparelJobs,
                apparel?.thingIDNumber ?? -1);
        }

        public bool TryMarkRemovalPending(Apparel apparel, Job job)
        {
            return TryMarkPending(pendingRemoveApparelJobs, apparel, job);
        }

        public bool IsLockPending(Apparel apparel)
        {
            return IsPending(
                pendingLockApparelJobs,
                apparel?.thingIDNumber ?? -1);
        }

        public bool TryGetLockPending(
            Apparel apparel,
            out PendingWardrobeJob pending)
        {
            pending = null;

            if (apparel == null ||
                !pendingLockApparelJobs.TryGetValue(
                    apparel.thingIDNumber,
                    out pending) ||
                pending == null ||
                pending.expiresAt <= CurrentTick)
            {
                pending = null;
                return false;
            }

            return true;
        }

        public bool TryMarkLockPending(Apparel apparel, Job job)
        {
            return TryMarkPending(pendingLockApparelJobs, apparel, job);
        }

        public void ClearLockPending(Apparel apparel)
        {
            if (apparel != null)
            {
                pendingLockApparelJobs.Remove(apparel.thingIDNumber);
            }
        }

        public void ClearPendingJobsForJob(Job job, JobCondition condition)
        {
            if (job == null)
            {
                return;
            }

            int jobId = job.loadID;

            if (job.def == WardrobeJobDefOf.WardrobeAttendant_RemoveApparel)
            {
                // The removal cooldown remains after cancellation or failure.
                // It is cleared only after successful removal.
                if (condition == JobCondition.Succeeded)
                {
                    ClearByJobId(pendingTargetJobs, jobId);
                    ClearByJobId(pendingRemoveApparelJobs, jobId);
                }

                return;
            }

            if (job.def == JobDefOf.ForceTargetWear)
            {
                ClearByJobId(pendingLockApparelJobs, jobId);

                if (condition == JobCondition.Succeeded)
                {
                    ClearByJobId(pendingTargetJobs, jobId);
                }
            }
        }

        public void CleanupExpiredPendingJobs()
        {
            int now = CurrentTick;

            if (now - lastCleanupTick < CleanupIntervalTicks)
            {
                return;
            }

            lastCleanupTick = now;

            CleanupExpired(pendingTargetJobs, now);
            CleanupExpired(pendingRemoveApparelJobs, now);
            CleanupExpired(pendingLockApparelJobs, now);

            CleanupInvalidAssignments();
        }

        private static bool IsPending(
            Dictionary<int, PendingWardrobeJob> pendingJobs,
            int thingId)
        {
            return pendingJobs.TryGetValue(thingId, out var pending) &&
                   pending != null &&
                   pending.expiresAt > CurrentTick;
        }

        private static bool TryMarkPending(
            Dictionary<int, PendingWardrobeJob> pendingJobs,
            Thing thing,
            Job job)
        {
            if (thing == null || job == null)
            {
                return false;
            }

            var pending = new PendingWardrobeJob
            {
                jobId = job.loadID,
                workerId = job.GetTarget(TargetIndex.A).Thing?.thingIDNumber ?? -1,
                expiresAt = CurrentTick + PendingJobDurationTicks,
                job = job,
                worker = job.GetTarget(TargetIndex.A).Thing as Pawn
            };

            pendingJobs[thing.thingIDNumber] = pending;

            return true;
        }

        private static void ClearByJobId(
            Dictionary<int, PendingWardrobeJob> pendingJobs,
            int jobId)
        {
            ExpiredPendingJobIds.Clear();

            foreach (var pair in pendingJobs)
            {
                if (pair.Value?.jobId == jobId)
                {
                    ExpiredPendingJobIds.Add(pair.Key);
                }
            }

            for (int i = 0; i < ExpiredPendingJobIds.Count; i++)
            {
                pendingJobs.Remove(ExpiredPendingJobIds[i]);
            }
        }

        private static void CleanupExpired(
    Dictionary<int, PendingWardrobeJob> pendingJobs,
    int now)
        {
            if (pendingJobs.Count == 0)
            {
                return;
            }

            ExpiredPendingJobIds.Clear();

            foreach (var pair in pendingJobs)
            {
                if (pair.Value == null || pair.Value.expiresAt <= now)
                {
                    ExpiredPendingJobIds.Add(pair.Key);
                }
            }

            for (int i = 0; i < ExpiredPendingJobIds.Count; i++)
            {
                pendingJobs.Remove(ExpiredPendingJobIds[i]);
            }
        }

        private void CleanupInvalidAssignments()
        {
            if (assignments == null)
            {
                assignments = new List<WardrobeAssignment>();
                assignmentsByPawnId.Clear();
                return;
            }

            for (int i = assignments.Count - 1; i >= 0; i--)
            {
                var assignment = assignments[i];

                if (assignment == null ||
                    assignment.target == null ||
                    assignment.target.Destroyed ||
                    assignment.slots == null)
                {
                    if (assignment?.target != null)
                    {
                        assignmentsByPawnId.Remove(assignment.target.thingIDNumber);
                    }

                    assignments.RemoveAt(i);
                    continue;
                }

                assignment.slots.RemoveAll(slot => slot == null);
            }
        }

        private static int CurrentTick =>
            Find.TickManager?.TicksGame ?? 0;

        public override void GameComponentTick()
        {
            CleanupExpiredPendingJobs();
        }

        public override void ExposeData()
        {
            base.ExposeData();

            Scribe_Values.Look(ref saveVersion, "saveVersion", 0);
            Scribe_Collections.Look(ref assignments, "assignments", LookMode.Deep);

            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                MigrateLoadedData();
                RebuildAssignmentCache();
                CleanUpInvalidAssignments();

                pendingTargetJobs.Clear();
                pendingRemoveApparelJobs.Clear();
                pendingLockApparelJobs.Clear();
            }
        }

        private void MigrateLoadedData()
        {
            if (saveVersion >= CurrentSaveVersion)
            {
                return;
            }

            // Save version 2: previous normalized format.
            // Save version 3: pending jobs are owned by their issuing job.
            //
            // Pending records are intentionally transient and are cleared on load.

            saveVersion = CurrentSaveVersion;
        }

        private void RebuildAssignmentCache()
        {
            assignmentsByPawnId.Clear();

            if (assignments == null)
            {
                assignments = new List<WardrobeAssignment>();
                return;
            }

            for (int i = 0; i < assignments.Count; i++)
            {
                var assignment = assignments[i];

                if (assignment?.target != null)
                {
                    assignmentsByPawnId[assignment.target.thingIDNumber] = assignment;
                }
            }
        }

        private void CleanUpInvalidAssignments()
        {
            if (assignments == null)
            {
                assignments = new List<WardrobeAssignment>();
                assignmentsByPawnId.Clear();
                return;
            }

            for (int i = assignments.Count - 1; i >= 0; i--)
            {
                var assignment = assignments[i];

                if (assignment == null ||
                    assignment.target == null ||
                    assignment.slots == null)
                {
                    if (assignment?.target != null)
                    {
                        assignmentsByPawnId.Remove(assignment.target.thingIDNumber);
                    }

                    assignments.RemoveAt(i);
                    continue;
                }

                assignment.slots.RemoveAll(slot => slot == null);
            }
        }

        public sealed class PendingWardrobeJob
        {
            public int jobId;
            public int workerId;
            public int expiresAt;
            public Job job;
            public Pawn worker;
        }
    }
}