using System;
using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.AI;

namespace WardrobeAttendant
{
    public class WorkGiver_WardrobeAttendant : WorkGiver_Scanner
    {
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

        private static readonly Dictionary<int, WardrobeJobResultCache> JobResultCache =
            new Dictionary<int, WardrobeJobResultCache>();

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
            var job = GetCachedJob(worker, thing, issueJob: true);

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
                component.TryMarkLockPending(apparelToWear, job);
            }

            return job;
        }

        private static Job GetCachedJob(Pawn worker, Thing thing, bool issueJob)
        {
            if (worker == null || thing == null)
            {
                return null;
            }

            int cacheKey = worker.thingIDNumber * 397 ^ thing.thingIDNumber;
            int currentTick = Find.TickManager?.TicksGame ?? 0;

            if (!issueJob &&
                JobResultCache.TryGetValue(cacheKey, out var cached) &&
                cached != null &&
                cached.Tick == currentTick)
            {
                return cached.Job;
            }

            Job job = FindJobFor(worker, thing, issueJob);

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

        private static Job FindJobFor(Pawn worker, Thing thing, bool issueJob)
        {
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

                var job = FindExactItemJob(
                    target, slot, worker, mapApparel, wornApparel, policy, protectedExactDefs);

                if (job != null)
                {
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

                var job = FindFullOutfitJob(
                    target,
                    slot,
                    worker,
                    mapApparel,
                    slots,
                    wornApparel,
                    policy,
                    protectedExactDefs,
                    availableExactDefs);

                if (job != null)
                {
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
                target == worker ||
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
                Apparel exactCandidate = FindExactCandidate(
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
            Apparel candidate = FindExactCandidate(
                target, slot, worker, mapApparel, wornApparel, policy,
                protectedExactDefs);

            return candidate != null
                ? JobMaker.MakeJob(JobDefOf.ForceTargetWear, target, candidate)
                : null;
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

                bool candidateIsBetter =
                    IsBetterThan(candidate, worn, slot);

                bool wornIsAboveThreshold =
                    slot.useDurabilityThreshold &&
                    GetCondition(worn) > slot.durabilityThreshold;

                if (!candidateIsBetter ||
                    wornIsAboveThreshold)
                {
                    LogFullOutfitRejection(
                        target,
                        candidate,
                        slot,
                        $"conflicts with worn {worn.def.defName}, " +
                        $"candidateIsBetter={candidateIsBetter}, " +
                        $"wornIsAboveThreshold={wornIsAboveThreshold}, " +
                        $"candidateScore={GetScore(candidate, slot)}, " +
                        $"wornScore={GetScore(worn, slot)}");
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

                if (!IsBetterThan(candidate, otherWorn, slot) ||
                    (slot.useDurabilityThreshold &&
                    GetCondition(otherWorn) >= slot.durabilityThreshold))
                {
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

            for (int i = 0; i < count; i++)
            {
                WardrobeSlot slot = slots[i];

                if (slot == null ||
                    !slot.enabled ||
                    slot.mode != WardrobeSlotMode.ExactItem ||
                    slot.exactApparelDef?.apparel == null ||
                    result.Contains(slot.exactApparelDef))
                {
                    continue;
                }

                Apparel candidate = FindBestApparel(
                    target,
                    slot,
                    worker,
                    mapApparel,
                    slot.exactApparelDef.apparel.layers,
                    policy);

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

            for (int i = 0; i < wornApparel.Count; i++)
            {
                var apparel = wornApparel[i];

                if (apparel == null ||
                    apparel.def != slot.exactApparelDef ||
                    !slot.AllowsApparel(apparel))
                {
                    continue;
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

            // Replace only after the currently worn exact apparel
            // falls below the slot durability threshold.
            if (slot.useDurabilityThreshold &&
                GetCondition(currentBest) >= slot.durabilityThreshold)
            {
                return false;
            }

            return IsBetterThan(candidate, currentBest, slot);
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

                if (slot.enabled &&
                    slot.mode == WardrobeSlotMode.ExactItem &&
                    slot.exactApparelDef != null)
                {
                    result.Add(slot.exactApparelDef);
                }
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

            bool failsQualityFilter = !slot.MatchesApparelIgnoreDurability(apparel);

            bool belowDurabilityThreshold =
                slot.useDurabilityThreshold &&
                GetCondition(apparel) < slot.durabilityThreshold;

            if (slot.mode == WardrobeSlotMode.ExactItem)
            {
                return apparel.def == slot.exactApparelDef &&
                       (failsQualityFilter || belowDurabilityThreshold);
            }

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
                float quality = GetQualityScore(apparel);
                float condition = GetCondition(apparel);

                return quality + condition * 10f + apparel.MarketValue;
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

            float score = 0f;

            if (slot.prioritizeArmor)
            {
                score += apparel.GetStatValue(StatDefOf.ArmorRating_Sharp);
            }

            if (slot.prioritizeCold)
            {
                score += GetProtectionStat(apparel, "Insulation_Cold") * 10f;
            }

            if (slot.prioritizeHeat)
            {
                score += GetProtectionStat(apparel, "Insulation_Heat") * 10f;
            }

            if (slot.prioritizeToxicity)
            {
                score += GetProtectionStat(apparel, "ToxicEnvironmentResistance") * 10f;
            }

            if (slot.prioritizeVacuum)
            {
                score += GetProtectionStat(apparel, "VacuumResistance") * 100f;
            }

            ApparelScoreCache[cacheKey] = score;

            return score;
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