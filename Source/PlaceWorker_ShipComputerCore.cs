using System.Collections.Generic;
using RimWorld;
using Verse;

namespace ShipCorePersona
{
    /// <summary>
    /// 电脑核心的放置规则：能承重的地面，或逆重飞船地板（含铺在飞船地板上的地表）。
    /// 屋顶限制不在这里处理，由补丁去掉 <c>PlaceWorker_NotUnderRoof</c>。
    ///
    /// 联动关系：是否连上某一艘飞船由 <see cref="Patch_ShipComputerCoreLinksOnlyOnItsFloor"/> 决定，
    /// 放置成功不等于已经连上飞船。原版 <c>terrainAffordanceNeeded</c> 只能填一种地面，
    /// 承重和飞船地板对不上，所以改由这个 PlaceWorker 同时接受两种。
    ///
    /// 注意：逆重飞船地板看 <c>TerrainDef.IsSubstructure</c>，其他模组的飞船地板只要标了这个标记也算。
    /// 占地里每一格都要合格，不能一半悬在不合格的地上。
    /// </summary>
    public class PlaceWorker_ShipComputerCore : PlaceWorker
    {
        public override AcceptanceReport AllowsPlacing(BuildableDef checkingDef, IntVec3 loc, Rot4 rot, Map map, Thing thingToIgnore = null, Thing thing = null)
        {
            foreach (IntVec3 cell in GenAdj.OccupiedRect(loc, rot, checkingDef.Size))
            {
                if (!cell.InBounds(map))
                    return "ShipCorePersona_NeedsHeavyOrSubstructure".Translate();

                if (!CellAcceptsCore(cell, map))
                    return "ShipCorePersona_NeedsHeavyOrSubstructure".Translate();
            }

            return true;
        }

        /// <summary>
        /// 建造菜单里同时显示承重和飞船地板两种要求。
        /// 奥德赛没加载、没有 Substructure 这个承重要求定义时，只显示承重。
        /// </summary>
        public override IEnumerable<TerrainAffordanceDef> DisplayAffordances()
        {
            yield return TerrainAffordanceDefOf.Heavy;

            TerrainAffordanceDef substructure = DefDatabase<TerrainAffordanceDef>.GetNamedSilentFail("Substructure");
            if (substructure != null)
                yield return substructure;
        }

        private static bool CellAcceptsCore(IntVec3 cell, Map map)
        {
            if (cell.GetAffordances(map).Contains(TerrainAffordanceDefOf.Heavy))
                return true;

            TerrainDef surface = map.terrainGrid.TerrainAt(cell);
            if (surface != null && surface.IsSubstructure)
                return true;

            // 飞船上再铺普通地板时，地表本身不是飞船地板，地基仍是。
            TerrainDef foundation = map.terrainGrid.FoundationAt(cell);
            return foundation != null && foundation.IsSubstructure;
        }
    }
}
