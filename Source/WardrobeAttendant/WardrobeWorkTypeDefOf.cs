using RimWorld;
using Verse;

namespace WardrobeAttendant
{
    [DefOf]
    public static class WardrobeWorkTypeDefOf
    {
        public static WorkTypeDef WardrobeAttendant_Wardrober;

        static WardrobeWorkTypeDefOf()
        {
            DefOfHelper.EnsureInitializedInCtor(typeof(WardrobeWorkTypeDefOf));
        }
    }
}