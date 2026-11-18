using System;
using RimWorld;
using UnityEngine;
using Verse;
using System.Collections.Generic;

namespace WardrobeAttendant
{
    public enum WardrobeSlotMode
    {
        FullOutfit,
        ExactItem
    }

    public class WardrobeSlot : IExposable
    {
        public string slotName = "New apparel slot";
        public bool enabled = true;
        public bool lockApparel = true;

        public WardrobeSlotMode mode = WardrobeSlotMode.FullOutfit;
        public ThingDef exactApparelDef;

        public bool useDurabilityThreshold = true;
        public float durabilityThreshold = 0.51f;

        public bool removeAtDurabilityThreshold = true;

        public bool allowEarlyReplacement = true;
        public bool allowSelfService = true;
        public bool allowWorkerRemoveLocked = true;

        public bool useProtectionPriorities = true;
        public bool prioritizeCold;
        public bool prioritizeHeat;
        public bool prioritizeArmor = true;
        public bool prioritizeToxicity;
        public bool prioritizeVacuum;

        public bool allowCorpseApparel;
        public bool onlyCorpseApparel;

        public QualityCategory minQuality = QualityCategory.Awful;
        public QualityCategory maxQuality = QualityCategory.Legendary;

        private int _protectionSummarySignature = int.MinValue;
        private string _protectionSummary;

        private int _corpseSummarySignature = int.MinValue;
        private string _corpseSummary;

        private int _qualitySummarySignature = int.MinValue;
        private string _qualitySummary;

        private int _conditionSummarySignature = int.MinValue;
        private string _conditionSummary;

        public void ExposeData()
        {
            Scribe_Values.Look(ref slotName, "slotName", "New apparel slot");
            Scribe_Values.Look(ref enabled, "enabled", true);
            Scribe_Values.Look(ref mode, "mode", WardrobeSlotMode.FullOutfit);
            Scribe_Defs.Look(ref exactApparelDef, "exactApparelDef");

            Scribe_Values.Look(ref useDurabilityThreshold, "useDurabilityThreshold", true);
            Scribe_Values.Look(ref durabilityThreshold, "durabilityThreshold", 0.51f);
            Scribe_Values.Look(ref removeAtDurabilityThreshold, "removeAtDurabilityThreshold", true);

            Scribe_Values.Look(ref allowEarlyReplacement, "allowEarlyReplacement", true);
            Scribe_Values.Look(ref allowSelfService, "allowSelfService", true);
            Scribe_Values.Look(ref allowWorkerRemoveLocked, "allowWorkerRemoveLocked", true);

            Scribe_Values.Look(ref useProtectionPriorities, "useProtectionPriorities", true);
            Scribe_Values.Look(ref prioritizeCold, "prioritizeCold", false);
            Scribe_Values.Look(ref prioritizeHeat, "prioritizeHeat", false);
            Scribe_Values.Look(ref prioritizeArmor, "prioritizeArmor", true);
            Scribe_Values.Look(ref prioritizeToxicity, "prioritizeToxicity", false);
            Scribe_Values.Look(ref prioritizeVacuum, "prioritizeVacuum", false);

            Scribe_Values.Look(ref allowCorpseApparel, "allowCorpseApparel", false);
            Scribe_Values.Look(ref onlyCorpseApparel, "onlyCorpseApparel", false);

            Scribe_Values.Look(ref minQuality, "minQuality", QualityCategory.Awful);
            Scribe_Values.Look(ref maxQuality, "maxQuality", QualityCategory.Legendary);
            Scribe_Values.Look(ref lockApparel, "lockApparel", true);

            if (Scribe.mode == LoadSaveMode.PostLoadInit &&
                mode == WardrobeSlotMode.ExactItem &&
                (exactApparelDef == null || exactApparelDef.apparel == null))
            {
                exactApparelDef = null;
                enabled = false;
            }
        }

        public bool AllowsApparel(Apparel apparel)
        {
            if (apparel == null)
            {
                return false;
            }

            if (useDurabilityThreshold && GetCondition(apparel) < durabilityThreshold)
            {
                return false;
            }

            var quality = apparel.TryGetComp<CompQuality>()?.Quality;

            if (quality == null)
            {
                return true;
            }

            return quality.Value >= minQuality && quality.Value <= maxQuality;
        }

        public bool MatchesApparelIgnoreDurability(Apparel apparel)
        {
            if (apparel == null)
            {
                return false;
            }

            var quality = apparel.TryGetComp<CompQuality>()?.Quality;

            if (quality == null)
            {
                return true;
            }

            return quality.Value >= minQuality && quality.Value <= maxQuality;
        }

        public bool IsExactLayer(ApparelLayerDef layer)
        {
            return mode == WardrobeSlotMode.ExactItem &&
                   exactApparelDef != null &&
                   exactApparelDef.apparel?.layers?.Contains(layer) == true;
        }

        public bool HasProtectionPriorities =>
            useProtectionPriorities &&
            (prioritizeCold ||
             prioritizeHeat ||
             prioritizeArmor ||
             prioritizeToxicity ||
             prioritizeVacuum);

        public bool AllowsCorpseApparel(Apparel apparel)
        {
            if (apparel == null)
            {
                return false;
            }

            bool isCorpseApparel = apparel.WornByCorpse;

            if (!allowCorpseApparel && isCorpseApparel)
            {
                return false;
            }

            if (allowCorpseApparel &&
                onlyCorpseApparel &&
                !isCorpseApparel)
            {
                return false;
            }

            return true;
        }

        public string GetProtectionSummary()
        {
            int signature = GetProtectionSummarySignature();

            if (_protectionSummary == null ||
                _protectionSummarySignature != signature)
            {
                _protectionSummarySignature = signature;
                _protectionSummary = BuildProtectionSummary();
            }

            return _protectionSummary;
        }

        public string GetCorpseApparelSummary()
        {
            int signature = GetCorpseSummarySignature();

            if (_corpseSummary == null ||
                _corpseSummarySignature != signature)
            {
                _corpseSummarySignature = signature;
                _corpseSummary = BuildCorpseSummary();
            }

            return _corpseSummary;
        }

        public string GetQualitySummary()
        {
            int signature = ((int)minQuality << 8) | (int)maxQuality;

            if (_qualitySummary == null ||
                _qualitySummarySignature != signature)
            {
                _qualitySummarySignature = signature;
                _qualitySummary = BuildQualitySummary();
            }

            return _qualitySummary;
        }

        public string GetConditionSummary()
        {
            int signature = GetConditionSummarySignature();

            if (_conditionSummary == null ||
                _conditionSummarySignature != signature)
            {
                _conditionSummarySignature = signature;
                _conditionSummary = BuildConditionSummary();
            }

            return _conditionSummary;
        }

        private int GetProtectionSummarySignature()
        {
            int signature = 0;

            if (HasProtectionPriorities)
            {
                signature |= 1;
            }

            if (prioritizeCold)
            {
                signature |= 2;
            }

            if (prioritizeHeat)
            {
                signature |= 4;
            }

            if (prioritizeArmor)
            {
                signature |= 8;
            }

            if (prioritizeToxicity)
            {
                signature |= 16;
            }

            if (prioritizeVacuum)
            {
                signature |= 32;
            }

            return signature;
        }

        private string BuildProtectionSummary()
        {
            if (!HasProtectionPriorities)
            {
                return "WardrobeAttendant.Summary.ProtectionNone".Translate();
            }

            var enabled = new List<string>(5);

            if (prioritizeCold)
            {
                enabled.Add("WardrobeAttendant.Slot.PrioritizeCold".Translate());
            }

            if (prioritizeHeat)
            {
                enabled.Add("WardrobeAttendant.Slot.PrioritizeHeat".Translate());
            }

            if (prioritizeArmor)
            {
                enabled.Add("WardrobeAttendant.Slot.PrioritizeArmor".Translate());
            }

            if (prioritizeToxicity)
            {
                enabled.Add("WardrobeAttendant.Slot.PrioritizeToxicity".Translate());
            }

            if (prioritizeVacuum)
            {
                enabled.Add("WardrobeAttendant.Slot.PrioritizeVacuum".Translate());
            }

            if (enabled.Count == 0)
            {
                return "WardrobeAttendant.Summary.ProtectionNone".Translate();
            }

            return "WardrobeAttendant.Summary.Protection".Translate(
                string.Join(", ", enabled));
        }

        private int GetCorpseSummarySignature()
        {
            int signature = 0;

            if (allowCorpseApparel)
            {
                signature |= 1;
            }

            if (onlyCorpseApparel)
            {
                signature |= 2;
            }

            return signature;
        }

        private string BuildCorpseSummary()
        {
            if (!allowCorpseApparel)
            {
                return "WardrobeAttendant.Summary.CorpseApparelForbidden".Translate();
            }

            return onlyCorpseApparel
                ? "WardrobeAttendant.Summary.OnlyCorpseApparel".Translate()
                : "WardrobeAttendant.Summary.CorpseApparelAllowed".Translate();
        }

        private string BuildQualitySummary()
        {
            if (minQuality == QualityCategory.Awful &&
                maxQuality == QualityCategory.Legendary)
            {
                return "WardrobeAttendant.Summary.Any".Translate();
            }

            if (minQuality == maxQuality)
            {
                return minQuality.GetLabel().Translate();
            }

            return $"{minQuality.GetLabel().Translate()}–{maxQuality.GetLabel().Translate()}";
        }

        private int GetConditionSummarySignature()
        {
            int signature = useDurabilityThreshold ? 1 : 0;

            return signature |
                   (Mathf.RoundToInt(durabilityThreshold * 100f) << 1);
        }

        private string BuildConditionSummary()
        {
            if (!useDurabilityThreshold)
            {
                return "WardrobeAttendant.Summary.Any".Translate();
            }

            return $">={Mathf.RoundToInt(durabilityThreshold * 100f)}%";
        }

        private static float GetCondition(Apparel apparel)
        {
            return apparel.HitPoints / (float)Math.Max(1, apparel.MaxHitPoints);
        }
    }
}