using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using Verse;

namespace ShipCorePersona
{
    /// <summary>
    /// 把飞船电脑核心登记进逆重引擎可以连接的设施。
    /// 遍历会连接逆重设施的引擎，不把引擎的 defName 写死。定义加载完才能调用这次登记。
    /// </summary>
    public static class GravshipEngineWhitelist
    {
        public static void Register()
        {
            if (!ModsConfig.OdysseyActive)
                return;

            ThingDef core = DefDatabase<ThingDef>.GetNamedSilentFail("Ship_ComputerCore");
            if (core == null)
                return;

            foreach (ThingDef def in DefDatabase<ThingDef>.AllDefs)
            {
                if (def.comps == null)
                    continue;

                bool engineClass = def.thingClass != null && typeof(Building_GravEngine).IsAssignableFrom(def.thingClass);
                foreach (CompProperties comp in def.comps)
                {
                    if (comp is CompProperties_AffectedByFacilities affected)
                        TryAddCore(affected, engineClass, core);
                }
            }
        }

        private static void TryAddCore(CompProperties_AffectedByFacilities affected, bool engineClass, ThingDef core)
        {
            bool linksGravFacility = false;
            if (affected.linkableFacilities != null)
            {
                for (int i = 0; i < affected.linkableFacilities.Count; i++)
                {
                    ThingDef facility = affected.linkableFacilities[i];
                    if (facility != null && facility.GetCompProperties<CompProperties_GravshipFacility>() != null)
                    {
                        linksGravFacility = true;
                        break;
                    }
                }
            }

            // 既不是逆重引擎、也不连接任何逆重设施的建筑，不要把飞船电脑核心加进它的可连接列表。
            if (!engineClass && !linksGravFacility)
                return;

            if (affected.linkableFacilities == null)
                affected.linkableFacilities = new List<ThingDef>();
            if (!affected.linkableFacilities.Contains(core))
                affected.linkableFacilities.Add(core);
        }
    }

    /// <summary>
    /// 整座飞船电脑核心都在那艘船的基架上，才算连上这艘逆重飞船。
    /// 原版碰到第一台距离不够的引擎，就会直接判定这台飞船电脑核心不能连上逆重引擎。这里要跳过那台引擎，继续看下一台。
    /// </summary>
    [HarmonyPatch(typeof(CompGravshipFacility), "get_CanBeActive")]
    public static class Patch_ShipComputerCoreLinksOnlyOnItsFloor
    {
        public static bool Prefix(CompGravshipFacility __instance, ref bool __result)
        {
            if (__instance?.parent?.def?.defName != "Ship_ComputerCore")
                return true;

            __result = false;
            if (!ModsConfig.OdysseyActive)
                return false;

            Thing parent = __instance.parent;
            CompPowerTrader power = parent.TryGetComp<CompPowerTrader>();
            if (power != null && !power.PowerOn)
                return false;

            CompBreakdownable breakdown = parent.TryGetComp<CompBreakdownable>();
            if (breakdown != null && breakdown.BrokenDown)
                return false;

            Map map = parent.Map;
            if (map == null)
                return false;

            float maxDistance = __instance.Props.maxDistance;
            foreach (Building_GravEngine engine in map.listerBuildings.AllBuildingsColonistOfClass<Building_GravEngine>())
            {
                if (!parent.Position.InHorDistOf(engine.Position, maxDistance))
                    continue;
                if (engine.OnValidSubstructure(parent))
                {
                    __result = true;
                    return false;
                }
            }

            return false;
        }
    }
}
