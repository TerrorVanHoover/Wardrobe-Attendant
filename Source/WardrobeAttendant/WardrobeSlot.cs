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
        public bool lockApparel;

        public WardrobeSlotMode mode = WardrobeSlotMode.FullOutfit;
        public ThingDef exactApparelDef;

        public bool useDurabilityThreshold;
        public float durabilityThreshold = 0.5f;

        public bool removeAtDurabilityThreshold;

        public bool useProtectionPriorities;
        public bool prioritizeCold;
        public bool prioritizeHeat;
        public bool prioritizeArmor = true;
        public bool prioritizeToxicity;
        public bool prioritizeVacuum;

        public bool allowCorpseApparel;
        public bool onlyCorpseApparel;

        public QualityCategory minQuality = QualityCategory.Awful;
        public QualityCategory maxQuality = QualityCategory.Legendary;

        public void ExposeData()
        {
            Scribe_Values.Look(ref slotName, "slotName", "New apparel slot");
            Scribe_Values.Look(ref enabled, "enabled", true);
            Scribe_Values.Look(ref mode, "mode", WardrobeSlotMode.FullOutfit);
            Scribe_Defs.Look(ref exactApparelDef, "exactApparelDef");
            Scribe_Values.Look(ref useDurabilityThreshold, "useDurabilityThreshold");
            Scribe_Values.Look(ref durabilityThreshold, "durabilityThreshold");
            Scribe_Values.Look(ref removeAtDurabilityThreshold, "removeAtDurabilityThreshold", false);
            Scribe_Values.Look(ref useProtectionPriorities, "useProtectionPriorities", false);
            Scribe_Values.Look(ref prioritizeCold, "prioritizeCold", false);
            Scribe_Values.Look(ref prioritizeHeat, "prioritizeHeat", false);
            Scribe_Values.Look(ref prioritizeArmor, "prioritizeArmor", true);
            Scribe_Values.Look(ref prioritizeToxicity, "prioritizeToxicity", false);
            Scribe_Values.Look(ref prioritizeVacuum, "prioritizeVacuum", false);
            Scribe_Values.Look(ref allowCorpseApparel, "allowCorpseApparel", false);
            Scribe_Values.Look(ref onlyCorpseApparel, "onlyCorpseApparel", false);
            Scribe_Values.Look(ref minQuality, "minQuality", QualityCategory.Awful);
            Scribe_Values.Look(ref maxQuality, "maxQuality", QualityCategory.Legendary);
            Scribe_Values.Look(ref lockApparel, "lockApparel", false);

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
            if (!useProtectionPriorities)
            {
                return "WardrobeAttendant.Summary.ProtectionNone".Translate();
            }

            var enabled = new List<string>();

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

        public string GetCorpseApparelSummary()
        {
            if (!allowCorpseApparel)
            {
                return "WardrobeAttendant.Summary.CorpseApparelForbidden".Translate();
            }

            return onlyCorpseApparel
                ? "WardrobeAttendant.Summary.OnlyCorpseApparel".Translate()
                : "WardrobeAttendant.Summary.CorpseApparelAllowed".Translate();
        }

        public string GetQualitySummary()
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

        public string GetConditionSummary()
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


