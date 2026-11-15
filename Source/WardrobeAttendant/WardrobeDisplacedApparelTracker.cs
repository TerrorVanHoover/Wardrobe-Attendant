using System.Collections.Generic;
using RimWorld;
using Verse;

namespace WardrobeAttendant
{
    public static class WardrobeDisplacedApparelTracker
    {
        private const int RecentlyDisplacedDurationTicks = 2500;

        private static readonly Dictionary<int, int> RecentlyDisplacedApparel =
            new Dictionary<int, int>();

        private static readonly List<int> ExpiredDisplacedApparelIds =
            new List<int>();

        public static void MarkRecentlyDisplaced(Apparel apparel)
        {
            if (apparel == null || apparel.Destroyed)
            {
                return;
            }

            RecentlyDisplacedApparel[apparel.thingIDNumber] =
                (Find.TickManager?.TicksGame ?? 0) + RecentlyDisplacedDurationTicks;
        }

        public static bool IsRecentlyDisplaced(Apparel apparel)
        {
            if (apparel == null)
            {
                return false;
            }

            if (!RecentlyDisplacedApparel.TryGetValue(
                    apparel.thingIDNumber,
                    out int expiresAt))
            {
                return false;
            }

            if (expiresAt <= (Find.TickManager?.TicksGame ?? 0))
            {
                RecentlyDisplacedApparel.Remove(apparel.thingIDNumber);
                return false;
            }

            return true;
        }

        public static void CleanupExpiredDisplacedApparel()
        {
            if (RecentlyDisplacedApparel.Count == 0)
            {
                return;
            }

            int now = Find.TickManager?.TicksGame ?? 0;

            ExpiredDisplacedApparelIds.Clear();

            foreach (var pair in RecentlyDisplacedApparel)
            {
                if (pair.Value <= now)
                {
                    ExpiredDisplacedApparelIds.Add(pair.Key);
                }
            }

            for (int i = 0; i < ExpiredDisplacedApparelIds.Count; i++)
            {
                RecentlyDisplacedApparel.Remove(ExpiredDisplacedApparelIds[i]);
            }
        }

        public static void ClearRecentlyDisplacedApparel()
        {
            RecentlyDisplacedApparel.Clear();
        }
    }
}