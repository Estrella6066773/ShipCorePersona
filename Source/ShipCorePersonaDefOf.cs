using RimWorld;
using Verse;

namespace ShipCorePersona
{
    /// <summary>
    /// 本模组自己的 def。只在这里集中取用，避免各处手写字符串。
    /// </summary>
    [DefOf]
    public static class ShipCorePersonaDefOf
    {
        public static PawnKindDef ShipCorePersona;
        public static HediffDef ShipCorePersonaBound;
        public static HediffDef ShipCorePersonaBandwidth;
        public static HediffDef ShipCorePersonaChipOffset;
        public static JobDef OperateShipCorePersona;

        static ShipCorePersonaDefOf()
        {
            DefOfHelper.EnsureInitializedInCtor(typeof(ShipCorePersonaDefOf));
        }
    }
}
