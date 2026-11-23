using System;
using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace WardrobeAttendant
{
    public class WorkGiver_WardrobeAttendant : WorkGiver_Scanner
    {
        private const int JobResultCacheMaxAgeTicks = 300;

        private const float EarlyReplacementScoreMargin = 5f;

        private static readonly List<ApparelLayerDef> AllApparelLayers =
            new List<ApparelLayerDef>();

        private static readonly Dictionary<Map, WardrobeJobScanCache> ScanCaches =
            new Dictionary<Map, WardrobeJobScanCache>();

        private static readonly Dictionary<Map, WardrobeApparelCache> ApparelCaches =
            new Dictionary<Map, WardrobeApparelCache>();

        private static readonly HashSet<ThingDef> ProtectedExactDefsScratch =
            new HashSet<ThingDef>();

        private static readonly List<ThingDef> AvailableExactDefsScratch =
            new List<ThingDef>();

        private static readonly List<long> StaleJobResultKeysScratch =
            new List<long>();

        private sealed class WardrobeJobScanCache
        {
            public int Tick;
            public readonly HashSet<int> DressingTargetIds = new HashSet<int>();
            public readonly HashSet<int> RemovingApparelIds = new HashSet<int>();
        }

        private sealed class WardrobeApparelCache
        {
            public int Tick;
            public List<Thing> Apparel;
        }

        private sealed class WardrobeJobResultCache
        {
            public int Tick;
            public Job Job;
        }

        private static readonly Dictionary<long, WardrobeJobResultCache> JobResultCache =
            new Dictionary<long, WardrobeJobResultCache>();

        // Exact candidate cache: one map scan per (worker, target, slot) per tick
        // instead of repeated scans from job issuing, replacement checks and
        // driver revalidation paths.
        private static readonly Dictionary<(int WorkerId, int TargetId, WardrobeSlot Slot), Apparel> ExactCandidateCache =
            new Dictionary<(int WorkerId, int TargetId, WardrobeSlot Slot), Apparel>();

        private static int ExactCandidateCacheTick = -1;

        private static readonly Dictionary<(int PawnId, int ApparelDefHash), bool> ExactDefWearabilityCache =
            new Dictionary<(int PawnId, int ApparelDefHash), bool>();

        private static int ExactDefWearabilityCacheTick = -1;

        public override ThingRequest PotentialWorkThingRequest =>
            ThingRequest.ForGroup(ThingRequestGroup.Pawn);

        public override bool HasJobOnThing(Pawn worker, Thing thing, bool forced = false)
        {
            return GetCachedJob(worker, thing, issueJob: false) != null;
        }

        public override Job JobOnThing(Pawn worker, Thing thing, bool forced = false)
        {
            var target = thing as Pawn;
            var component = WardrobeGameComponent.Get;
            var job = GetCachedJob(worker, thing, issueJob: true, out var jobSlot);

            if (job == null || target == null || component == null)
            {
                return job;
            }

            component.TryMarkTargetJobPending(target, job);

            if (job.def == WardrobeJobDefOf.WardrobeAttendant_RemoveApparel)
            {
                if (job.targetB.Thing is Apparel primaryApparel)
                {
                    component.TryMarkRemovalPending(primaryApparel, job);
                }

                if (job.targetQueueB != null)
                {
                    for (int i = 0; i < job.targetQueueB.Count; i++)
                    {
                        if (job.targetQueueB[i].Thing is Apparel additionalApparel)
                        {
                            component.TryMarkRemovalPending(additionalApparel, job);
                        }
                    }
                }
            }
            else if (job.def == JobDefOf.ForceTargetWear &&
                job.targetB.Thing is Apparel apparelToWear)
            {
                component.TryMarkLockPending(apparelToWear, job, jobSlot);

                MarkApparelDisplacedByEarlyReplacement(
                    target,
                    apparelToWear);
            }
            else if (job.def == WardrobeJobDefOf.WardrobeAttendant_SelfWearApparel &&
                job.targetB.Thing is Apparel selfWearApparel)
            {
                component.TryMarkLockPending(selfWearApparel, job, jobSlot);

                MarkApparelDisplacedByEarlyReplacement(
                    target,
                    selfWearApparel);
            }

            return job;
        }

        private static void MarkApparelDisplacedByEarlyReplacement(
    Pawn target,
    Apparel candidate)
        {
            if (target?.apparel == null ||
                target.RaceProps?.body == null ||
                candidate == null ||
                candidate.Destroyed ||
                candidate.def?.apparel == null)
            {
                return;
            }

            WardrobeGameComponent component = WardrobeGameComponent.Get;

            if (component == null ||
                !component.TryGetSlots(target, out List<WardrobeSlot> slots))
            {
                return;
            }

            ApparelPolicy policy = target.outfits?.CurrentApparelPolicy;
            HashSet<ThingDef> protectedExactDefs = GetProtectedExactDefs(target);

            List<Apparel> wornApparel = target.apparel.WornApparel;
            int slotCount = slots.Count;

            for (int i = 0; i < slotCount; i++)
            {
                WardrobeSlot slot = slots[i];

                if (slot == null ||
                    !slot.enabled ||
                    !slot.allowEarlyReplacement)
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

                int wornCount = wornApparel.Count;

                for (int j = 0; j < wornCount; j++)
                {
                    Apparel worn = wornApparel[j];

                    if (worn == null ||
                        worn == candidate ||
                        worn.Destroyed ||
                        worn.def == null)
                    {
                        continue;
                    }

                    if (ApparelUtility.CanWearTogether(
                            candidate.def,
                            worn.def,
                            target.RaceProps.body))
                    {
                        continue;
                    }

                    if (IsApparelLocked(target, worn) &&
                        !slot.allowWorkerRemoveLocked)
                    {
                        continue;
                    }

                    if (slot.useDurabilityThreshold &&
                        GetCondition(worn) >= slot.durabilityThreshold)
                    {
                        WardrobeDisplacedApparelTracker.MarkRecentlyDisplaced(worn);
                    }
                }
            }
        }

        private static Job GetCachedJob(Pawn worker, Thing thing, bool issueJob)
        {
            return GetCachedJob(worker, thing, issueJob, out _);
        }

        private static Job GetCachedJob(
            Pawn worker,
            Thing thing,
            bool issueJob,
            out WardrobeSlot jobSlot)
        {
            jobSlot = null;

            if (worker == null || thing == null)
            {
                return null;
            }

            long cacheKey =
                ((long)worker.thingIDNumber << 32) | (uint)thing.thingIDNumber;
            int currentTick = Find.TickManager?.TicksGame ?? 0;

            if (!issueJob &&
                JobResultCache.TryGetValue(cacheKey, out var cached) &&
                cached != null &&
                cached.Tick == currentTick)
            {
                return cached.Job;
            }

            Job job = FindJobFor(worker, thing, issueJob, out jobSlot);

            if (job == null)
            {
                JobResultCache.Remove(cacheKey);
                return null;
            }

            if (!issueJob)
            {
                if (!JobResultCache.TryGetValue(cacheKey, out var cache) ||
                    cache == null)
                {
                    cache = new WardrobeJobResultCache();
                    JobResultCache[cacheKey] = cache;
                }

                cache.Tick = currentTick;
                cache.Job = job;
            }
            else
            {
                JobResultCache.Remove(cacheKey);
            }

            return job;
        }

        // Called from WardrobeGameComponent's periodic cleanup.
        // Entries older than JobResultCacheMaxAgeTicks belong to pawns that are
        // no longer scanned and would otherwise accumulate for the whole session.
        public static void CleanupStaleJobResults(int now)
        {
            WardrobeDisplacedApparelTracker.CleanupExpiredDisplacedApparel();

            if (JobResultCache.Count == 0)
            {
                return;
            }

            StaleJobResultKeysScratch.Clear();

            foreach (var pair in JobResultCache)
            {
                if (pair.Value == null ||
                    now - pair.Value.Tick > JobResultCacheMaxAgeTicks)
                {
                    StaleJobResultKeysScratch.Add(pair.Key);
                }
            }

            for (int i = 0; i < StaleJobResultKeysScratch.Count; i++)
            {
                JobResultCache.Remove(StaleJobResultKeysScratch[i]);
            }
        }

        // Called from WardrobeGameComponent.Finalize so abandoned maps and
        // finished games are not pinned by static caches.
        public static void ClearStaticCaches()
        {
            ScanCaches.Clear();
            ApparelCaches.Clear();
            JobResultCache.Clear();
            ExactCandidateCache.Clear();
            ExactCandidateCacheTick = -1;
            ExactDefWearabilityCache.Clear();
            ExactDefWearabilityCacheTick = -1;
            ApparelScoreCache.Clear();
            ApparelScoreCacheTick = -1;

            WardrobeDisplacedApparelTracker.ClearRecentlyDisplacedApparel();
        }

        public static void InvalidateExactCandidateCache()
        {
            ExactCandidateCache.Clear();
            ExactCandidateCacheTick = -1;
        }

        private static bool IsExactSlotWearable(Pawn target, WardrobeSlot slot)
        {
            if (target == null ||
                slot == null ||
                slot.mode != WardrobeSlotMode.ExactItem ||
                slot.exactApparelDef == null)
            {
                return false;
            }

            int currentTick = Find.TickManager?.TicksGame ?? 0;

            if (ExactDefWearabilityCacheTick != currentTick)
            {
                ExactDefWearabilityCache.Clear();
                ExactDefWearabilityCacheTick = currentTick;
            }

            var cacheKey = (target.thingIDNumber, slot.exactApparelDef.shortHash);

            if (ExactDefWearabilityCache.TryGetValue(cacheKey, out bool wearable))
            {
                return wearable;
            }

            wearable = slot.IsExactDefWearable(target);
            ExactDefWearabilityCache[cacheKey] = wearable;

            return wearable;
        }

        private static Job FindJobFor(
            Pawn worker,
            Thing thing,
            bool issueJob,
            out WardrobeSlot jobSlot)
        {
            jobSlot = null;
            var target = thing as Pawn;

            if (!IsValidTarget(worker, target))
            {
                return null;
            }

            var component = WardrobeGameComponent.Get;

            if (component == null ||
                !component.TryGetSlots(target, out var slots))
            {
                return null;
            }

            if (WardrobeModSettingsExtensions.DebugLoggingEnabled)
            {
                Log.Message(
                    "[Wardrobe] FindJobFor: " +
                    $"worker={worker?.LabelShortCap ?? "null"}, " +
                    $"target={target?.LabelShortCap ?? "null"}, " +
                    $"samePawn={worker == target}, " +
                    $"slotCount={slots?.Count ?? 0}");
            }

            if (component.IsTargetJobPending(target))
            {
                LogDebug($"{target.LabelShortCap} already has a pending wardrobe job.");
                return null;
            }

            if (IsTargetBlockedByPlayerOrder(target))
            {
                LogDebug(
                    $"{target.LabelShortCap} is skipped because it has a player-forced job.");
                return null;
            }

            if (IsAlreadyBeingDressed(worker, target) ||
                IsReservedByOtherWorker(worker, target))
            {
                return null;
            }

            var mapApparel = GetMapApparel(target.Map);
            var wornApparel = target.apparel.WornApparel;
            var policy = target.outfits?.CurrentApparelPolicy;
            var protectedExactDefs = GetProtectedExactDefs(target);
            var availableExactDefs = GetAvailableExactDefs(
                target,
                worker,
                mapApparel,
                slots,
                policy);

            for (int i = 0; i < slots.Count; i++)
            {
                var slot = slots[i];

                if (!slot.enabled)
                {
                    continue;
                }

                if (WardrobeModSettingsExtensions.DebugLoggingEnabled)
                {
                    Log.Message(
                        "[Wardrobe] Slot scan: " +
                        $"worker={worker?.LabelShortCap ?? "null"}, " +
                        $"target={target?.LabelShortCap ?? "null"}, " +
                        $"slot={slot.slotName}, " +
                        $"mode={slot.mode}, " +
                        $"allowSelfService={slot.allowSelfService}, " +
                        $"selfServiceBlocked={(!slot.allowSelfService && worker == target)}");
                }

                if (!slot.allowSelfService &&
                    worker == target)
                {
                    continue;
                }

                var removeJob = FindRemoveApparelJob(
                    target,
                    slot,
                    worker,
                    component,
                    mapApparel,
                    wornApparel,
                    policy,
                    protectedExactDefs,
                    availableExactDefs,
                    issueJob);

                if (removeJob != null)
                {
                    LogRemoveJob(target, slot, removeJob);
                    return removeJob;
                }
            }

            for (int i = 0; i < slots.Count; i++)
            {
                var slot = slots[i];

                if (!slot.enabled || slot.mode != WardrobeSlotMode.ExactItem)
                {
                    continue;
                }

                if (!IsExactSlotWearable(target, slot))
                {
                    continue;
                }

                if (WardrobeModSettingsExtensions.DebugLoggingEnabled)
                {
                    Log.Message(
                        "[Wardrobe] Slot scan: " +
                        $"worker={worker?.LabelShortCap ?? "null"}, " +
                        $"target={target?.LabelShortCap ?? "null"}, " +
                        $"slot={slot.slotName}, " +
                        $"mode={slot.mode}, " +
                        $"allowSelfService={slot.allowSelfService}, " +
                        $"selfServiceBlocked={(!slot.allowSelfService && worker == target)}");
                }

                if (!slot.allowSelfService &&
                    worker == target)
                {
                    continue;
                }

                var job = FindExactItemJob(target, slot, worker, mapApparel, wornApparel, policy, protectedExactDefs);

                if (job != null)
                {
                    if (worker == target)
                    {
                        job = MakeSelfWearJob(job);
                    }

                    jobSlot = slot;

                    LogWearJob(target, slot, job);
                    return job;
                }
            }

            for (int i = 0; i < slots.Count; i++)
            {
                var slot = slots[i];

                if (!slot.enabled || slot.mode != WardrobeSlotMode.FullOutfit)
                {
                    continue;
                }

                if (WardrobeModSettingsExtensions.DebugLoggingEnabled)
                {
                    Log.Message(
                        "[Wardrobe] Slot scan: " +
                        $"worker={worker?.LabelShortCap ?? "null"}, " +
                        $"target={target?.LabelShortCap ?? "null"}, " +
                        $"slot={slot.slotName}, " +
                        $"mode={slot.mode}, " +
                        $"allowSelfService={slot.allowSelfService}, " +
                        $"selfServiceBlocked={(!slot.allowSelfService && worker == target)}");
                }

                if (!slot.allowSelfService &&
                    worker == target)
                {
                    continue;
                }

                var job = FindFullOutfitJob(target, slot, worker, mapApparel, slots, wornApparel, policy, protectedExactDefs, availableExactDefs);

                if (job != null)
                {
                    if (worker == target)
                    {
                        job = MakeSelfWearJob(job);
                    }

                    jobSlot = slot;

                    LogWearJob(target, slot, job);
                    return job;
                }
            }

            return null;
        }

        private static bool IsTargetBlockedByPlayerOrder(Pawn target)
        {
            return WardrobeModSettingsExtensions.SkipDraftedTargets &&
                   target?.Drafted == true;
        }

        private static bool IsValidTarget(Pawn worker, Pawn target)
        {
            if (target == null ||
                !target.Spawned ||
                target.Dead ||
                target.Downed ||
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

            return target.RaceProps.Animal &&
                   target.Faction == Faction.OfPlayer;
        }

        private static bool IsReservedByOtherWorker(Pawn worker, Pawn target)
        {
            var map = target.Map;

            if (map == null)
            {
                return false;
            }

            var reserver = map.reservationManager.FirstRespectedReserver(target, worker);

            if (reserver == null || reserver == worker)
            {
                return false;
            }

            LogDebug($"{target.LabelShortCap} is reserved by {reserver.LabelShortCap}.");
            return true;
        }

        private static bool IsAlreadyBeingDressed(Pawn worker, Pawn target)
        {
            var cache = GetScanCache(target?.Map);

            if (cache == null)
            {
                return false;
            }

            if (cache.DressingTargetIds.Contains(target.thingIDNumber))
            {
                LogDebug($"{target.LabelShortCap} is already being dressed.");
                return true;
            }

            return false;
        }

        private static bool IsAlreadyRemovingApparel(Pawn worker, Apparel apparel)
        {
            var cache = GetScanCache(apparel?.MapHeld);

            if (cache == null)
            {
                return false;
            }

            if (cache.RemovingApparelIds.Contains(apparel.thingIDNumber))
            {
                LogDebug($"{apparel.def.defName} is already being removed.");
                return true;
            }

            return false;
        }

        private static WardrobeJobScanCache GetScanCache(Map map)
        {
            if (map == null)
            {
                return null;
            }

            int currentTick = Find.TickManager?.TicksGame ?? 0;

            if (!ScanCaches.TryGetValue(map, out var cache) ||
                cache.Tick != currentTick)
            {
                cache = BuildScanCache(map, currentTick);
                ScanCaches[map] = cache;
            }

            return cache;
        }

        private static WardrobeJobScanCache BuildScanCache(Map map, int currentTick)
        {
            var cache = new WardrobeJobScanCache
            {
                Tick = currentTick
            };

            var spawnedPawns = map.mapPawns.AllPawnsSpawned;

            for (int i = 0; i < spawnedPawns.Count; i++)
            {
                var pawn = spawnedPawns[i];

                if (pawn?.jobs?.curJob is not Job currentJob)
                {
                    continue;
                }

                if (currentJob.def == JobDefOf.ForceTargetWear &&
                    currentJob.targetA.Thing is Pawn dressingTarget)
                {
                    cache.DressingTargetIds.Add(dressingTarget.thingIDNumber);
                    continue;
                }

                if (currentJob.def == WardrobeJobDefOf.WardrobeAttendant_SelfWearApparel &&
                    currentJob.targetA.Thing is Pawn selfDressingTarget)
                {
                    cache.DressingTargetIds.Add(selfDressingTarget.thingIDNumber);
                    continue;
                }

                if (currentJob.def == WardrobeJobDefOf.WardrobeAttendant_RemoveApparel)
                {
                    if (currentJob.targetB.Thing is Apparel primaryApparel)
                    {
                        cache.RemovingApparelIds.Add(primaryApparel.thingIDNumber);
                    }

                    if (currentJob.targetQueueB != null)
                    {
                        for (int j = 0; j < currentJob.targetQueueB.Count; j++)
                        {
                            if (currentJob.targetQueueB[j].Thing is Apparel additionalApparel)
                            {
                                cache.RemovingApparelIds.Add(additionalApparel.thingIDNumber);
                            }
                        }
                    }
                }
            }

            return cache;
        }

        private static Job FindRemoveApparelJob(
            Pawn target,
            WardrobeSlot slot,
            Pawn worker,
            WardrobeGameComponent component,
            List<Thing> mapApparel,
            List<Apparel> wornApparel,
            ApparelPolicy policy,
            HashSet<ThingDef> protectedExactDefs,
            List<ThingDef> availableExactDefs,
            bool issueJob)
        {
            if (!slot.useDurabilityThreshold || !slot.removeAtDurabilityThreshold)
            {
                return null;
            }

            if (slot.mode == WardrobeSlotMode.ExactItem &&
                !IsExactSlotWearable(target, slot))
            {
                return null;
            }

            Job job = null;

            for (int i = 0; i < wornApparel.Count; i++)
            {
                var worn = wornApparel[i];

                if (worn == null ||
                    worn.def == null ||
                    worn.MaxHitPoints <= 0 ||
                    !IsRemovalCandidate(worn, slot))
                {
                    continue;
                }

                if (IsApparelLocked(target, worn) &&
                    !slot.allowWorkerRemoveLocked)
                {
                    continue;
                }

                bool isProtectedExact = protectedExactDefs.Contains(worn.def);
                bool isRequiredExactApparel =
                    slot.mode == WardrobeSlotMode.ExactItem &&
                    worn.def == slot.exactApparelDef;

                if (isProtectedExact && !isRequiredExactApparel)
                {
                    continue;
                }

                if (HasSuitableReplacement(
                    target,
                    slot,
                    worker,
                    mapApparel,
                    wornApparel,
                    worn,
                    policy,
                    protectedExactDefs,
                    availableExactDefs))
                {
                    continue;
                }

                if (component.IsRemovalPending(worn))
                {
                    LogDebug($"{worn.def.defName} already has a pending removal job.");
                    continue;
                }

                if (IsAlreadyRemovingApparel(worker, worn))
                {
                    continue;
                }

                if (job == null)
                {
                    job = JobMaker.MakeJob(
                        WardrobeJobDefOf.WardrobeAttendant_RemoveApparel,
                        target,
                        worn);

                    if (!issueJob)
                    {
                        return job;
                    }

                    continue;
                }

                job.targetQueueB ??= new List<LocalTargetInfo>();
                job.targetQueueB.Add(worn);
            }

            return job;
        }

        public static bool IsApparelLocked(Pawn target, Apparel apparel)
        {
            return target?.outfits?.forcedHandler != null &&
                   target.outfits.forcedHandler.IsForced(apparel);
        }

        public static bool TryGetSelfWearSlot(Pawn target, Apparel apparel, out WardrobeSlot slot)
        {
            slot = null;

            if (target == null || apparel == null)
            {
                return false;
            }

            WardrobeGameComponent component = WardrobeGameComponent.Get;

            if (component == null || !component.TryGetSlots(target, out List<WardrobeSlot> slots))
            {
                return false;
            }

            List<Thing> mapApparel = GetMapApparel(target.Map);
            List<Apparel> wornApparel = target.apparel.WornApparel;
            ApparelPolicy policy = target.outfits?.CurrentApparelPolicy;
            HashSet<ThingDef> protectedExactDefs = GetProtectedExactDefs(target);
            List<ThingDef> availableExactDefs = GetAvailableExactDefs(target, target, mapApparel, slots, policy);

            for (int i = 0; i < slots.Count; i++)
            {
                WardrobeSlot candidateSlot = slots[i];

                if (candidateSlot == null || !candidateSlot.enabled)
                {
                    continue;
                }

                if (candidateSlot.mode == WardrobeSlotMode.ExactItem)
                {
                    if (apparel.def == candidateSlot.exactApparelDef &&
                        CanUseExactCandidate(target, candidateSlot, apparel, wornApparel, protectedExactDefs))
                    {
                        slot = candidateSlot;
                        return true;
                    }

                    continue;
                }

                if (candidateSlot.mode == WardrobeSlotMode.FullOutfit &&
                    candidateSlot.AllowsApparel(apparel) &&
                    candidateSlot.AllowsCorpseApparel(apparel) &&
                    CanUseFullOutfitCandidate(
                        target,
                        candidateSlot,
                        apparel,
                        wornApparel,
                        protectedExactDefs,
                        availableExactDefs))
                {
                    slot = candidateSlot;
                    return true;
                }
            }

            return false;
        }

        public static bool IsRemovalStillValid(
            Pawn target,
            Apparel apparel,
            Pawn worker)
        {
            if (target == null ||
                apparel == null ||
                worker == null ||
                apparel.Destroyed ||
                !target.Spawned ||
                target.Dead ||
                target.apparel == null ||
                apparel.Wearer != target)
            {
                return false;
            }

            WardrobeGameComponent component = WardrobeGameComponent.Get;

            if (component == null ||
                !component.TryGetSlots(target, out var slots))
            {
                return false;
            }

            List<Thing> mapApparel = GetMapApparel(target.Map);
            List<Apparel> wornApparel = target.apparel.WornApparel;
            ApparelPolicy policy = target.outfits?.CurrentApparelPolicy;
            HashSet<ThingDef> protectedExactDefs = GetProtectedExactDefs(target);
            List<ThingDef> availableExactDefs = GetAvailableExactDefs(
                target,
                worker,
                mapApparel,
                slots,
                policy);

            for (int i = 0; i < slots.Count; i++)
            {
                WardrobeSlot slot = slots[i];

                if (slot == null ||
                    !slot.enabled ||
                    !slot.removeAtDurabilityThreshold ||
                    (slot.mode == WardrobeSlotMode.ExactItem &&
                     !IsExactSlotWearable(target, slot)) ||
                    !IsRemovalCandidate(apparel, slot))
                {
                    continue;
                }

                bool isProtectedExact = protectedExactDefs.Contains(apparel.def);
                bool isRequiredExactApparel =
                    slot.mode == WardrobeSlotMode.ExactItem &&
                    apparel.def == slot.exactApparelDef;

                if (isProtectedExact && !isRequiredExactApparel)
                {
                    continue;
                }

                if (HasSuitableReplacement(
                    target,
                    slot,
                    worker,
                    mapApparel,
                    wornApparel,
                    apparel,
                    policy,
                    protectedExactDefs,
                    availableExactDefs))
                {
                    continue;
                }

                return true;
            }

            return false;
        }

        private static bool CanUseExactCandidate(
            Pawn target,
            WardrobeSlot slot,
            Apparel candidate,
            List<Apparel> wornApparel,
            HashSet<ThingDef> protectedExactDefs)
        {
            if (target?.RaceProps?.body == null ||
                slot == null ||
                candidate == null ||
                candidate.Destroyed ||
                candidate.def?.apparel == null)
            {
                return false;
            }

            if (candidate.def != slot.exactApparelDef ||
                !slot.AllowsApparel(candidate) ||
                !slot.AllowsCorpseApparel(candidate))
            {
                return false;
            }

            int wornCount = wornApparel?.Count ?? 0;

            for (int i = 0; i < wornCount; i++)
            {
                Apparel worn = wornApparel[i];

                if (worn == null ||
                    worn.Destroyed ||
                    worn.def == null ||
                    worn == candidate)
                {
                    continue;
                }

                if (worn.def == candidate.def)
                {
                    continue;
                }

                if (ApparelUtility.CanWearTogether(
                        candidate.def,
                        worn.def,
                        target.RaceProps.body))
                {
                    continue;
                }

                if (IsApparelLocked(target, worn) &&
                    !slot.allowWorkerRemoveLocked)
                {
                    return false;
                }

                if (protectedExactDefs.Contains(worn.def))
                {
                    return false;
                }
            }

            return true;
        }

        public static bool IsWearStillValid(
            Pawn target,
            Apparel apparel,
            Pawn worker)
        {
            if (target == null ||
                apparel == null ||
                worker == null ||
                apparel.Destroyed ||
                apparel.IsBurning() ||
                apparel.IsForbidden(worker) ||
                !target.Spawned ||
                target.Dead ||
                target.apparel == null ||
                target.apparel.WornApparel.Contains(apparel) ||
                !CanTargetWear(apparel, target))
            {
                LogWearValidationFailure(
                    target,
                    apparel,
                    "basic availability or wearability");
                return false;
            }

            WardrobeGameComponent component = WardrobeGameComponent.Get;

            if (component == null ||
                !component.TryGetSlots(target, out var slots))
            {
                LogWearValidationFailure(
                    target,
                    apparel,
                    "wardrobe component or slots");
                return false;
            }

            List<Thing> mapApparel = GetMapApparel(target.Map);
            List<Apparel> wornApparel = target.apparel.WornApparel;
            ApparelPolicy policy = target.outfits?.CurrentApparelPolicy;
            HashSet<ThingDef> protectedExactDefs = GetProtectedExactDefs(target);

            List<ThingDef> availableExactDefs = GetAvailableExactDefs(
                target,
                worker,
                mapApparel,
                slots,
                policy);

            if (policy != null && !policy.filter.Allows(apparel))
            {
                LogWearValidationFailure(
                    target,
                    apparel,
                    "apparel policy");
                return false;
            }

            for (int i = 0; i < slots.Count; i++)
            {
                WardrobeSlot slot = slots[i];

                if (slot == null || !slot.enabled)
                {
                    continue;
                }

                if (slot.mode == WardrobeSlotMode.ExactItem)
                {
                    if (apparel.def != slot.exactApparelDef ||
                        !slot.AllowsApparel(apparel) ||
                        !slot.AllowsCorpseApparel(apparel))
                    {
                        continue;
                    }

                    if (CanUseExactCandidate(
                            target,
                            slot,
                            apparel,
                            wornApparel,
                            protectedExactDefs))
                    {
                        return true;
                    }

                    LogWearValidationFailure(
                        target,
                        apparel,
                        "exact item candidate",
                        slot);
                    continue;
                }

                if (!slot.AllowsApparel(apparel) ||
                    !slot.AllowsCorpseApparel(apparel))
                {
                    continue;
                }

                if (CanUseFullOutfitCandidate(
                        target,
                        slot,
                        apparel,
                        wornApparel,
                        protectedExactDefs,
                        availableExactDefs))
                {
                    return true;
                }

                LogWearValidationFailure(
                    target,
                    apparel,
                    "full outfit candidate",
                    slot);
            }

            return false;
        }

        public static bool IsWearStillHighestPriority(
            Pawn target,
            Apparel apparel,
            Pawn worker,
            int issuedSlotOrderVersion)
        {
            if (target == null ||
                apparel == null ||
                worker == null ||
                apparel.Destroyed ||
                target.apparel == null)
            {
                return true;
            }

            WardrobeGameComponent component = WardrobeGameComponent.Get;

            if (component == null ||
                !component.TryGetSlots(target, out List<WardrobeSlot> slots))
            {
                return true;
            }

            int currentSlotOrderVersion = component.GetSlotOrderVersion(target);

            if (currentSlotOrderVersion == issuedSlotOrderVersion)
            {
                return true;
            }

            List<Thing> mapApparel = GetMapApparel(target.Map);
            List<Apparel> wornApparel = target.apparel.WornApparel;
            ApparelPolicy policy = target.outfits?.CurrentApparelPolicy;
            HashSet<ThingDef> protectedExactDefs = GetProtectedExactDefs(target);
            List<ThingDef> availableExactDefs = GetAvailableExactDefs(
                target,
                worker,
                mapApparel,
                slots,
                policy);

            for (int i = 0; i < slots.Count; i++)
            {
                WardrobeSlot slot = slots[i];

                if (slot == null ||
                    !slot.enabled ||
                    slot.mode != WardrobeSlotMode.ExactItem ||
                    !IsExactSlotWearable(target, slot))
                {
                    continue;
                }

                Apparel exactCandidate = GetCachedExactCandidate(
                    target,
                    slot,
                    worker,
                    mapApparel,
                    wornApparel,
                    policy,
                    protectedExactDefs);

                if (exactCandidate == null)
                {
                    continue;
                }

                if (apparel.def == slot.exactApparelDef &&
                    CanUseExactCandidate(
                        target,
                        slot,
                        apparel,
                        wornApparel,
                        protectedExactDefs))
                {
                    return true;
                }

                if (!ApparelUtility.CanWearTogether(
                        apparel.def,
                        exactCandidate.def,
                        target.RaceProps.body))
                {
                    return false;
                }

                return true;
            }

            for (int i = 0; i < slots.Count; i++)
            {
                WardrobeSlot slot = slots[i];

                if (slot == null ||
                    !slot.enabled ||
                    slot.mode != WardrobeSlotMode.FullOutfit)
                {
                    continue;
                }

                List<Apparel> candidates = FindAllApparel(
                    target,
                    slot,
                    worker,
                    mapApparel,
                    AllApparelLayers,
                    policy);

                Apparel bestCandidate = null;
                int candidateCount = candidates.Count;

                for (int j = 0; j < candidateCount; j++)
                {
                    Apparel candidate = candidates[j];

                    if (!CanUseFullOutfitCandidate(
                            target,
                            slot,
                            candidate,
                            wornApparel,
                            protectedExactDefs,
                            availableExactDefs))
                    {
                        continue;
                    }

                    if (bestCandidate == null ||
                        CompareApparel(candidate, bestCandidate, slot) > 0)
                    {
                        bestCandidate = candidate;
                    }
                }

                if (bestCandidate == null)
                {
                    continue;
                }

                if (CanUseFullOutfitCandidate(
                        target,
                        slot,
                        apparel,
                        wornApparel,
                        protectedExactDefs,
                        availableExactDefs))
                {
                    return true;
                }

                if (!ApparelUtility.CanWearTogether(
                        apparel.def,
                        bestCandidate.def,
                        target.RaceProps.body))
                {
                    return false;
                }

                return true;
            }

            return true;
        }

        private static void LogWearValidationFailure(
            Pawn target,
            Apparel apparel,
            string reason,
            WardrobeSlot slot = null)
        {
            if (!WardrobeModSettingsExtensions.DebugLoggingEnabled)
            {
                return;
            }

            string slotInfo = slot == null
                ? "slot=null"
                : $"slot={slot.slotName}, " +
                  $"allowCorpse={slot.allowCorpseApparel}, " +
                  $"onlyCorpse={slot.onlyCorpseApparel}";

            Log.Message(
                $"[Wardrobe] Wear validation failed for " +
                $"{target?.LabelShortCap ?? "null"}: " +
                $"{apparel?.def?.defName ?? "null"}, " +
                $"reason={reason}, " +
                $"wornByCorpse={apparel?.WornByCorpse ?? false}, " +
                slotInfo);
        }

        private static bool HasSuitableReplacement(
            Pawn target,
            WardrobeSlot slot,
            Pawn worker,
            List<Thing> mapApparel,
            List<Apparel> wornApparel,
            Apparel worn,
            ApparelPolicy policy,
            HashSet<ThingDef> protectedExactDefs,
            List<ThingDef> availableExactDefs)
        {
            if (target == null ||
                slot == null ||
                worn == null ||
                protectedExactDefs == null ||
                availableExactDefs == null)
            {
                return false;
            }

            if (slot.mode == WardrobeSlotMode.ExactItem)
            {
                Apparel exactCandidate = GetCachedExactCandidate(
                    target,
                    slot,
                    worker,
                    mapApparel,
                    wornApparel,
                    policy,
                    protectedExactDefs);

                return exactCandidate != null &&
                       exactCandidate != worn &&
                       exactCandidate.def == worn.def;
            }

            List<Apparel> candidates = FindAllApparel(
                target,
                slot,
                worker,
                mapApparel,
                AllApparelLayers,
                policy);

            if (candidates.Count == 0)
            {
                return false;
            }

            int count = candidates.Count;

            for (int i = 0; i < count; i++)
            {
                Apparel candidate = candidates[i];

                if (candidate == worn ||
                    ApparelUtility.CanWearTogether(
                        candidate.def,
                        worn.def,
                        target.RaceProps.body))
                {
                    continue;
                }

                if (CanUseFullOutfitReplacementCandidate(
                        target,
                        slot,
                        candidate,
                        worn,
                        wornApparel,
                        protectedExactDefs,
                        availableExactDefs))
                {
                    return true;
                }
            }

            return false;
        }

        private static Job FindExactItemJob(
            Pawn target,
            WardrobeSlot slot,
            Pawn worker,
            List<Thing> mapApparel,
            List<Apparel> wornApparel,
            ApparelPolicy policy,
            HashSet<ThingDef> protectedExactDefs)
        {
            Apparel candidate = GetCachedExactCandidate(
                target, slot, worker, mapApparel, wornApparel, policy,
                protectedExactDefs);

            return candidate != null
                ? JobMaker.MakeJob(JobDefOf.ForceTargetWear, target, candidate)
                : null;
        }

        // Exact candidates are the hot path: the same (worker, target, slot)
        // triple is rescanned by job issuing, replacement checks and both driver
        // revalidation paths. The result is cached for the current tick with the
        // same granularity as GetMapApparel, so behaviour is unchanged.
        private static Apparel GetCachedExactCandidate(
            Pawn target,
            WardrobeSlot slot,
            Pawn worker,
            List<Thing> mapApparel,
            List<Apparel> wornApparel,
            ApparelPolicy policy,
            HashSet<ThingDef> protectedExactDefs)
        {
            int currentTick = Find.TickManager?.TicksGame ?? 0;

            if (ExactCandidateCacheTick != currentTick)
            {
                ExactCandidateCache.Clear();
                ExactCandidateCacheTick = currentTick;
            }

            var key = (worker.thingIDNumber, target.thingIDNumber, slot);

            if (ExactCandidateCache.TryGetValue(key, out Apparel cached))
            {
                return cached;
            }

            Apparel candidate = FindExactCandidate(
                target,
                slot,
                worker,
                mapApparel,
                wornApparel,
                policy,
                protectedExactDefs);

            ExactCandidateCache[key] = candidate;

            return candidate;
        }

        private static Apparel FindExactCandidate(
            Pawn target,
            WardrobeSlot slot,
            Pawn worker,
            List<Thing> mapApparel,
            List<Apparel> wornApparel,
            ApparelPolicy policy,
            HashSet<ThingDef> protectedExactDefs)
        {
            if (target?.RaceProps?.body == null ||
                slot?.exactApparelDef?.apparel == null)
            {
                return null;
            }

            Apparel candidate = FindBestApparel(
                target,
                slot,
                worker,
                mapApparel,
                slot.exactApparelDef.apparel.layers,
                policy);

            if (candidate == null ||
                candidate.def != slot.exactApparelDef ||
                !NeedsExactReplacement(slot, wornApparel, candidate))
            {
                return null;
            }

            int count = wornApparel.Count;

            for (int i = 0; i < count; i++)
            {
                Apparel worn = wornApparel[i];

                if (worn == null || worn.Destroyed || worn.def == null ||
                    worn.def == candidate.def)
                {
                    continue;
                }

                if (protectedExactDefs.Contains(worn.def) &&
                    !ApparelUtility.CanWearTogether(
                        candidate.def, worn.def, target.RaceProps.body))
                {
                    return null;
                }
            }

            return candidate;
        }

        private static Job FindFullOutfitJob(
            Pawn target,
            WardrobeSlot slot,
            Pawn worker,
            List<Thing> mapApparel,
            List<WardrobeSlot> slots,
            List<Apparel> wornApparel,
            ApparelPolicy policy,
            HashSet<ThingDef> protectedExactDefs,
            List<ThingDef> availableExactDefs)
        {
            List<Apparel> candidates = FindAllApparel(
                target,
                slot,
                worker,
                mapApparel,
                AllApparelLayers,
                policy);

            if (candidates.Count == 0)
            {
                return null;
            }

            Apparel best = null;
            int count = candidates.Count;

            for (int i = 0; i < count; i++)
            {
                Apparel candidate = candidates[i];

                if (!CanUseFullOutfitCandidate(
                        target,
                        slot,
                        candidate,
                        wornApparel,
                        protectedExactDefs,
                        availableExactDefs))
                {
                    continue;
                }

                if (best == null ||
                    CompareApparel(candidate, best, slot) > 0)
                {
                    best = candidate;
                }
            }

            return best != null
                ? JobMaker.MakeJob(JobDefOf.ForceTargetWear, target, best)
                : null;
        }

        private static Job MakeSelfWearJob(Job wearJob)
        {
            Pawn target = wearJob.targetA.Thing as Pawn;
            Apparel apparel = wearJob.targetB.Thing as Apparel;

            if (target == null || apparel == null)
            {
                return wearJob;
            }

            return JobMaker.MakeJob(
                WardrobeJobDefOf.WardrobeAttendant_SelfWearApparel,
                target,
                apparel);
        }

        private static bool CanUseFullOutfitCandidate(
            Pawn target,
            WardrobeSlot slot,
            Apparel candidate,
            List<Apparel> wornApparel,
            HashSet<ThingDef> protectedExactDefs,
            List<ThingDef> availableExactDefs)
        {
            if (target?.RaceProps?.body == null ||
                slot == null ||
                candidate == null ||
                candidate.Destroyed ||
                candidate.def?.apparel == null)
            {
                LogFullOutfitRejection(
                    target,
                    candidate,
                    slot,
                    "basic availability or wearability");
                return false;
            }

            if (wornApparel == null)
            {
                LogFullOutfitRejection(
                    target,
                    candidate,
                    slot,
                    "worn apparel list is null");
                return false;
            }

            if (wornApparel.Contains(candidate))
            {
                LogFullOutfitRejection(
                    target,
                    candidate,
                    slot,
                    "candidate is already worn");
                return false;
            }

            if (protectedExactDefs != null &&
                protectedExactDefs.Contains(candidate.def))
            {
                LogFullOutfitRejection(
                    target,
                    candidate,
                    slot,
                    "candidate def is protected by an exact item rule");
                return false;
            }

            int wornCount = wornApparel.Count;

            for (int i = 0; i < wornCount; i++)
            {
                Apparel worn = wornApparel[i];

                if (worn == null ||
                    worn.Destroyed ||
                    worn.def == null)
                {
                    continue;
                }

                if (ApparelUtility.CanWearTogether(
                        candidate.def,
                        worn.def,
                        target.RaceProps.body))
                {
                    continue;
                }

                if (protectedExactDefs != null &&
                    protectedExactDefs.Contains(worn.def))
                {
                    LogFullOutfitRejection(
                        target,
                        candidate,
                        slot,
                        $"conflicts with protected exact apparel {worn.def.defName}");
                    return false;
                }

                bool wornFailsQualityFilter =
                    !slot.MatchesApparelIgnoreDurability(worn);

                if (wornFailsQualityFilter)
                {
                    continue;
                }

                if (IsApparelLocked(target, worn) &&
                    !slot.allowWorkerRemoveLocked)
                {
                    LogFullOutfitRejection(
                        target,
                        candidate,
                        slot,
                        $"would require removing locked apparel {worn.def.defName}");

                    return false;
                }

                if (!ShouldReplaceFullOutfitWornApparel(
                        slot,
                        candidate,
                        worn))
                {
                    LogFullOutfitRejection(
                        target,
                        candidate,
                        slot,
                        $"conflicts with worn {worn.def.defName}, " +
                        $"candidateScore={GetScore(candidate, slot)}, " +
                        $"wornScore={GetScore(worn, slot)}, " +
                        $"candidateCondition={GetCondition(candidate)}, " +
                        $"wornCondition={GetCondition(worn)}, " +
                        $"threshold={(slot.useDurabilityThreshold ? slot.durabilityThreshold.ToString("0.##") : "disabled")}, " +
                        $"allowEarlyReplacement={slot.allowEarlyReplacement}");

                    return false;
                }
            }

            if (availableExactDefs != null)
            {
                int exactCount = availableExactDefs.Count;

                for (int i = 0; i < exactCount; i++)
                {
                    ThingDef exactDef = availableExactDefs[i];

                    if (exactDef == null ||
                        candidate.def == exactDef ||
                        ApparelUtility.CanWearTogether(
                            candidate.def,
                            exactDef,
                            target.RaceProps.body))
                    {
                        continue;
                    }

                    LogFullOutfitRejection(
                        target,
                        candidate,
                        slot,
                        $"conflicts with available exact apparel {exactDef.defName}");
                    return false;
                }
            }

            return true;
        }

        private static void LogFullOutfitRejection(
            Pawn target,
            Apparel candidate,
            WardrobeSlot slot,
            string reason)
        {
            if (!WardrobeModSettingsExtensions.DebugLoggingEnabled)
            {
                return;
            }

            Log.Message(
                $"[Wardrobe] Full outfit candidate rejected for " +
                $"{target?.LabelShortCap ?? "null"}: " +
                $"{candidate?.def?.defName ?? "null"}, " +
                $"slot={slot?.slotName ?? "null"}, " +
                $"reason={reason}");
        }

        private static bool CanUseFullOutfitReplacementCandidate(
            Pawn target,
            WardrobeSlot slot,
            Apparel candidate,
            Apparel worn,
            List<Apparel> wornApparel,
            HashSet<ThingDef> protectedExactDefs,
            List<ThingDef> availableExactDefs)
        {
            if (target?.RaceProps?.body == null ||
                slot == null ||
                candidate == null ||
                candidate.Destroyed ||
                candidate.def?.apparel == null ||
                wornApparel.Contains(candidate) ||
                protectedExactDefs.Contains(candidate.def))
            {
                return false;
            }

            int wornCount = wornApparel.Count;

            for (int i = 0; i < wornCount; i++)
            {
                Apparel otherWorn = wornApparel[i];

                if (otherWorn == null ||
                    otherWorn.Destroyed ||
                    otherWorn.def == null)
                {
                    continue;
                }

                if (ApparelUtility.CanWearTogether(
                        candidate.def,
                        otherWorn.def,
                        target.RaceProps.body))
                {
                    continue;
                }

                if (protectedExactDefs.Contains(otherWorn.def))
                {
                    return false;
                }

                bool otherWornFailsQualityFilter =
                    !slot.MatchesApparelIgnoreDurability(otherWorn);

                if (otherWornFailsQualityFilter)
                {
                    continue;
                }

                if (IsApparelLocked(target, otherWorn) &&
                    !slot.allowWorkerRemoveLocked)
                {
                    LogFullOutfitRejection(
                        target,
                        candidate,
                        slot,
                        "would require removing locked apparel");

                    return false;
                }

                if (!ShouldReplaceFullOutfitWornApparel(
                        slot,
                        candidate,
                        otherWorn))
                {
                    LogFullOutfitRejection(
                        target,
                        candidate,
                        slot,
                        "does not improve the worn apparel enough");

                    return false;
                }
            }

            int exactCount = availableExactDefs.Count;

            for (int i = 0; i < exactCount; i++)
            {
                ThingDef exactDef = availableExactDefs[i];

                if (candidate.def != exactDef &&
                    !ApparelUtility.CanWearTogether(
                        candidate.def,
                        exactDef,
                        target.RaceProps.body))
                {
                    return false;
                }
            }

            return true;
        }

        private static bool ShouldReplaceFullOutfitWornApparel(
    WardrobeSlot slot,
    Apparel candidate,
    Apparel worn)
        {
            if (slot.onlyCorpseApparel &&
                candidate.WornByCorpse &&
                !worn.WornByCorpse)
            {
                return true;
            }

            float candidateScore = GetScore(candidate, slot);
            float wornScore = GetScore(worn, slot);

            if (!slot.useDurabilityThreshold)
            {
                return candidateScore >= wornScore + EarlyReplacementScoreMargin;
            }

            if (GetCondition(worn) < slot.durabilityThreshold)
            {
                return GetCondition(candidate) >= slot.durabilityThreshold;
            }

            if (!slot.allowEarlyReplacement)
            {
                return false;
            }

            return GetCondition(candidate) >= slot.durabilityThreshold &&
                   candidateScore >= wornScore + EarlyReplacementScoreMargin;
        }

        // Availability of exact defs intentionally uses the cached full-map
        // candidate scan (not a phased/stored-only scan): it feeds the
        // full-outfit protection logic, which must keep seeing every
        // reachable exact candidate.
        private static List<ThingDef> GetAvailableExactDefs(
            Pawn target,
            Pawn worker,
            List<Thing> mapApparel,
            List<WardrobeSlot> slots,
            ApparelPolicy policy)
        {
            var result = AvailableExactDefsScratch;
            result.Clear();

            int count = slots.Count;

            if (target?.apparel == null)
            {
                return result;
            }

            List<Apparel> wornApparel = target.apparel.WornApparel;
            HashSet<ThingDef> protectedExactDefs = GetProtectedExactDefs(target);

            for (int i = 0; i < count; i++)
            {
                WardrobeSlot slot = slots[i];

                if (slot == null ||
                    !slot.enabled ||
                    slot.mode != WardrobeSlotMode.ExactItem ||
                    slot.exactApparelDef?.apparel == null ||
                    !IsExactSlotWearable(target, slot) ||
                    result.Contains(slot.exactApparelDef))
                {
                    continue;
                }

                Apparel candidate = GetCachedExactCandidate(
                    target,
                    slot,
                    worker,
                    mapApparel,
                    wornApparel,
                    policy,
                    protectedExactDefs);

                if (candidate != null)
                {
                    result.Add(slot.exactApparelDef);
                }
            }

            return result;
        }

        private static List<Thing> GetMapApparel(Map map)
        {
            if (map == null)
            {
                return EmptyThingList;
            }

            int currentTick = Find.TickManager?.TicksGame ?? 0;

            if (!ApparelCaches.TryGetValue(map, out var cache) ||
                cache == null ||
                cache.Tick != currentTick ||
                cache.Apparel == null)
            {
                cache ??= new WardrobeApparelCache();
                cache.Tick = currentTick;
                cache.Apparel = map.listerThings.ThingsInGroup(
                    ThingRequestGroup.Apparel);

                ApparelCaches[map] = cache;
            }

            return cache.Apparel;
        }

        private static readonly List<Apparel> ScratchApparelCandidates =
            new List<Apparel>();

        private static readonly List<Thing> EmptyThingList =
            new List<Thing>();

        private static List<Apparel> FindAllApparel(
            Pawn target,
            WardrobeSlot slot,
            Pawn worker,
            List<Thing> mapApparel,
            List<ApparelLayerDef> layers,
            ApparelPolicy policy)
        {
            ScratchApparelCandidates.Clear();

            List<Apparel> result = ScratchApparelCandidates;

            if (target == null || worker == null || slot == null || mapApparel == null)
            {
                return result;
            }

            int count = mapApparel.Count;

            for (int i = 0; i < count; i++)
            {
                if (mapApparel[i] is not Apparel apparel ||
                    apparel.Destroyed ||
                    apparel.def?.apparel?.layers == null)
                {
                    continue;
                }

                if (slot.mode == WardrobeSlotMode.ExactItem &&
                    apparel.def != slot.exactApparelDef)
                {
                    continue;
                }

                if (!slot.AllowsCorpseApparel(apparel) ||
                    !MatchesAnyLayer(apparel.def.apparel.layers, layers) ||
                    !slot.AllowsApparel(apparel) ||
                    (policy != null && !policy.filter.Allows(apparel)) ||
                    apparel.IsForbidden(worker) ||
                    apparel.IsBurning() ||
                    !CanTargetWear(apparel, target))
                {
                    continue;
                }

                if (!worker.CanReserveAndReach(
                        apparel, PathEndMode.ClosestTouch, Danger.Deadly))
                {
                    continue;
                }

                result.Add(apparel);
            }

            return result;
        }

        private static Apparel FindBestApparel(
            Pawn target,
            WardrobeSlot slot,
            Pawn worker,
            List<Thing> mapApparel,
            List<ApparelLayerDef> layers,
            ApparelPolicy policy)
        {
            if (target == null || worker == null || slot == null || mapApparel == null)
            {
                return null;
            }

            ThingDef requiredDef = slot.mode == WardrobeSlotMode.ExactItem
                ? slot.exactApparelDef
                : null;

            if (slot.mode == WardrobeSlotMode.ExactItem && requiredDef?.apparel == null)
            {
                return null;
            }

            Apparel best = null;
            float bestScore = float.MinValue;
            int count = mapApparel.Count;

            for (int i = 0; i < count; i++)
            {
                if (mapApparel[i] is not Apparel apparel ||
                    apparel.Destroyed ||
                    apparel.def?.apparel == null ||
                    (requiredDef != null && apparel.def != requiredDef))
                {
                    continue;
                }

                var apparelLayers = apparel.def.apparel.layers;

                if (!slot.AllowsCorpseApparel(apparel) ||
                    apparelLayers == null ||
                    !MatchesAnyLayer(apparelLayers, layers) ||
                    !slot.AllowsApparel(apparel) ||
                    (policy != null && !policy.filter.Allows(apparel)) ||
                    apparel.IsForbidden(worker) ||
                    apparel.IsBurning() ||
                    !CanTargetWear(apparel, target))
                {
                    continue;
                }

                if (!worker.CanReserveAndReach(
                        apparel, PathEndMode.ClosestTouch, Danger.Deadly))
                {
                    continue;
                }

                float score = GetScore(apparel, slot);

                if (score > bestScore)
                {
                    best = apparel;
                    bestScore = score;
                }
            }

            return best;
        }

        private static bool NeedsExactReplacement(
            WardrobeSlot slot,
            List<Apparel> wornApparel,
            Apparel candidate)
        {
            Apparel currentBest = null;
            bool hasCorpseWornItem = false;

            int wornCount = wornApparel?.Count ?? 0;

            for (int i = 0; i < wornCount; i++)
            {
                var apparel = wornApparel[i];

                if (apparel == null ||
                    apparel.Destroyed ||
                    apparel.def != slot.exactApparelDef ||
                    !slot.MatchesApparelIgnoreDurability(apparel))
                {
                    continue;
                }

                if (apparel.WornByCorpse)
                {
                    hasCorpseWornItem = true;
                }

                if (currentBest == null ||
                    GetScore(apparel, slot) > GetScore(currentBest, slot))
                {
                    currentBest = apparel;
                }
            }

            if (currentBest == null)
            {
                return true;
            }

            if (slot.onlyCorpseApparel && !hasCorpseWornItem)
            {
                return true;
            }

            float candidateScore = GetScore(candidate, slot);
            float currentScore = GetScore(currentBest, slot);

            if (!slot.useDurabilityThreshold)
            {
                return candidateScore >= currentScore + EarlyReplacementScoreMargin;
            }

            if (GetCondition(currentBest) < slot.durabilityThreshold)
            {
                return GetCondition(candidate) >= slot.durabilityThreshold;
            }

            if (!slot.allowEarlyReplacement)
            {
                return false;
            }

            return GetCondition(candidate) >= slot.durabilityThreshold &&
                   candidateScore >= currentScore + EarlyReplacementScoreMargin;
        }

        private static HashSet<ThingDef> GetProtectedExactDefs(Pawn target)
        {
            var result = ProtectedExactDefsScratch;
            result.Clear();

            var component = WardrobeGameComponent.Get;

            if (target == null ||
                component == null ||
                !component.TryGetSlots(target, out var slots))
            {
                return result;
            }

            for (int i = 0; i < slots.Count; i++)
            {
                var slot = slots[i];

                if (slot == null ||
                    !slot.enabled ||
                    slot.mode != WardrobeSlotMode.ExactItem ||
                    slot.exactApparelDef == null ||
                    !IsExactSlotWearable(target, slot))
                {
                    continue;
                }

                result.Add(slot.exactApparelDef);
            }

            return result;
        }

        private static bool IsRemovalCandidate(
            Apparel apparel,
            WardrobeSlot slot)
        {
            if (apparel == null || slot == null)
            {
                return false;
            }

            if (slot.mode == WardrobeSlotMode.ExactItem &&
                apparel.def != slot.exactApparelDef)
            {
                return false;
            }

            if (slot.onlyCorpseApparel && !apparel.WornByCorpse)
            {
                return true;
            }

            bool failsQualityFilter = !slot.MatchesApparelIgnoreDurability(apparel);

            bool belowDurabilityThreshold =
                slot.useDurabilityThreshold &&
                GetCondition(apparel) < slot.durabilityThreshold;

            return failsQualityFilter || belowDurabilityThreshold;
        }

        private static bool MatchesSlot(Apparel apparel, WardrobeSlot slot)
        {
            return slot.mode == WardrobeSlotMode.ExactItem
                ? apparel.def == slot.exactApparelDef
                : slot.MatchesApparelIgnoreDurability(apparel);
        }

        private static bool IsBetterThan(
            Apparel candidate,
            Apparel current,
            WardrobeSlot slot)
        {
            return CompareApparel(candidate, current, slot) > 0;
        }

        private static int CompareApparel(
            Apparel a,
            Apparel b,
            WardrobeSlot slot)
        {
            float scoreA = GetScore(a, slot);
            float scoreB = GetScore(b, slot);

            int byScore = scoreA.CompareTo(scoreB);

            if (byScore != 0)
            {
                return byScore;
            }

            int byHitPoints = a.HitPoints.CompareTo(b.HitPoints);

            if (byHitPoints != 0)
            {
                return byHitPoints;
            }

            return a.thingIDNumber.CompareTo(b.thingIDNumber);
        }

        private static float GetScore(Apparel apparel, WardrobeSlot slot)
        {
            if (apparel == null)
            {
                return float.MinValue;
            }

            if (slot == null || !slot.HasProtectionPriorities)
            {
                return GetDefaultScore(apparel);
            }

            var cacheKey = (apparel.thingIDNumber, slot);
            int currentTick = Find.TickManager?.TicksGame ?? 0;

            if (ApparelScoreCacheTick != currentTick)
            {
                ApparelScoreCache.Clear();
                ApparelScoreCacheTick = currentTick;
            }

            if (ApparelScoreCache.TryGetValue(cacheKey, out float cachedScore))
            {
                return cachedScore;
            }

            float score = GetCondition(apparel) * 10f;

            if (slot.prioritizeArmor)
            {
                score += apparel.GetStatValue(StatDefOf.ArmorRating_Sharp) * 100f;
            }

            if (slot.prioritizeCold)
            {
                score += Mathf.Clamp01(
                    -apparel.GetStatValue(StatDefOf.Insulation_Cold) / 100f) * 100f;
            }

            if (slot.prioritizeHeat)
            {
                score += Mathf.Clamp01(
                    apparel.GetStatValue(StatDefOf.Insulation_Heat) / 100f) * 100f;
            }

            if (slot.prioritizeToxicity)
            {
                float toxicityOffset = GetEquippedStatOffset(
                    apparel,
                    StatDefOf.ToxicEnvironmentResistance);

                score += Mathf.Clamp01(toxicityOffset) * 400f;
            }

            if (slot.prioritizeVacuum)
            {
                float vacuumOffset = GetEquippedStatOffset(
                    apparel,
                    StatDefOf.VacuumResistance);

                score += Mathf.Clamp01(vacuumOffset) * 500f;
            }

            ApparelScoreCache[cacheKey] = score;
            return score;
        }

        private static float GetEquippedStatOffset(
            Apparel apparel,
            StatDef statDef)
        {
            if (apparel?.def?.equippedStatOffsets == null ||
                statDef == null)
            {
                return 0f;
            }

            List<StatModifier> offsets = apparel.def.equippedStatOffsets;
            int count = offsets.Count;

            for (int i = 0; i < count; i++)
            {
                StatModifier offset = offsets[i];

                if (offset?.stat == statDef)
                {
                    return offset.value;
                }
            }

            return 0f;
        }

        private static float GetDefaultScore(Apparel apparel)
        {
            if (apparel == null)
            {
                return float.MinValue;
            }

            return GetQualityScore(apparel) +
                   GetCondition(apparel) * 10f +
                   apparel.GetStatValue(StatDefOf.ArmorRating_Sharp) * 100f;
        }

        private static float GetQualityScore(Apparel apparel)
        {
            var quality = apparel?.TryGetComp<CompQuality>()?.Quality;

            return quality == null ? 0f : (int)quality.Value;
        }

        private static float GetProtectionStat(Apparel apparel, string statDefName)
        {
            ResolveProtectionStats();

            StatDef statDef = statDefName switch
            {
                "Insulation_Cold" => InsulationColdStat,
                "Insulation_Heat" => InsulationHeatStat,
                "ToxicEnvironmentResistance" => ToxicityStat,
                "VacuumResistance" => VacuumStat,
                _ => null
            };

            if (statDef == null)
            {
                return 0f;
            }

            return apparel.GetStatValue(statDef);
        }

        private static void ResolveProtectionStats()
        {
            if (ProtectionStatsResolved)
            {
                return;
            }

            InsulationColdStat = DefDatabase<StatDef>.GetNamedSilentFail(
                "Insulation_Cold");

            InsulationHeatStat = DefDatabase<StatDef>.GetNamedSilentFail(
                "Insulation_Heat");

            ToxicityStat = DefDatabase<StatDef>.GetNamedSilentFail(
                "ToxicEnvironmentResistance");

            VacuumStat = DefDatabase<StatDef>.GetNamedSilentFail(
                "VacuumResistance");

            ProtectionStatsResolved = true;
        }

        private static readonly Dictionary<(int, WardrobeSlot), float> ApparelScoreCache =
            new Dictionary<(int, WardrobeSlot), float>();

        private static int ApparelScoreCacheTick = -1;

        private static StatDef InsulationColdStat;
        private static StatDef InsulationHeatStat;
        private static StatDef ToxicityStat;
        private static StatDef VacuumStat;
        private static bool ProtectionStatsResolved;

        private static float GetCondition(Apparel apparel)
        {
            return apparel.HitPoints / (float)Math.Max(1, apparel.MaxHitPoints);
        }

        private static bool CanTargetWear(Apparel apparel, Pawn target)
        {
            return apparel != null &&
                   target != null &&
                   ApparelUtility.HasPartsToWear(target, apparel.def) &&
                   apparel.def.apparel != null &&
                   apparel.def.apparel.PawnCanWear(target);
        }

        private static bool MatchesAnyLayer(
            List<ApparelLayerDef> apparelLayers,
            List<ApparelLayerDef> requiredLayers)
        {
            if (apparelLayers == null)
            {
                return false;
            }

            if (requiredLayers == null || requiredLayers.Count == 0)
            {
                return true;
            }

            for (int i = 0; i < requiredLayers.Count; i++)
            {
                if (apparelLayers.Contains(requiredLayers[i]))
                {
                    return true;
                }
            }

            return false;
        }

        private static void LogRemoveJob(Pawn target, WardrobeSlot slot, Job job)
        {
            if (!WardrobeModSettingsExtensions.DebugLoggingEnabled)
            {
                return;
            }

            var apparel = job.targetB.Thing as Apparel;

            Log.Message(
                $"[Wardrobe] Remove job created for {target.LabelShortCap}: " +
                $"slot={slot.slotName}, " +
                $"apparel={(apparel != null ? apparel.def.defName : "none")}, " +
                $"additional={(job.targetQueueB?.Count ?? 0)}");
        }

        private static void LogWearJob(Pawn target, WardrobeSlot slot, Job job)
        {
            if (!WardrobeModSettingsExtensions.DebugLoggingEnabled)
            {
                return;
            }

            Log.Message(
                $"[Wardrobe] Job created for {target.LabelShortCap}: " +
                $"slot={slot.slotName}, mode={slot.mode}, " +
                $"apparel={(job.targetB.Thing != null ? job.targetB.Thing.def.defName : "none")}");
        }

        private static void LogDebug(string message)
        {
            if (WardrobeModSettingsExtensions.DebugLoggingEnabled)
            {
                Log.Message($"[Wardrobe] {message}");
            }
        }
    }
}