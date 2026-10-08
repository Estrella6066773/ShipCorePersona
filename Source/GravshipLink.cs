using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using Verse;

namespace ShipCorePersona
{
    /// <summary>
    /// 把飞船电脑核心登记进各艘逆重引擎的可连接设施。
    /// 原版引擎和会连接逆重设施的模组引擎都会被扫到，不写死引擎 defName。
    ///
    /// 联动关系：由 <see cref="ModEntry"/> 在定义加载完成后调用 <see cref="Register"/>。
    /// 真正能不能连上，还要过 <see cref="Patch_ShipComputerCoreLinksOnlyOnItsFloor"/>。
    /// 若 Gravship Voyage 已经加过电脑核心，这里不会重复添加。
    ///
    /// 注意：必须在全部 XML 补丁跑完之后调用。只改定义上的名单，不改已经生成的存档物体；
    /// 那些物体读的是同一份定义，所以旧档也会跟着生效。
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

            // 不是逆重引擎，也不连接任何逆重设施的建筑，不要塞进电脑核心。
            if (!engineClass && !linksGravFacility)
                return;

            if (affected.linkableFacilities == null)
                affected.linkableFacilities = new List<ThingDef>();
            if (!affected.linkableFacilities.Contains(core))
                affected.linkableFacilities.Add(core);
        }
    }

    /// <summary>
    /// 电脑核心只有整座都站在某一艘逆重飞船的地板上时，才算连上那艘船。
    ///
    /// 联动关系：补丁挂在 <c>CompGravshipFacility.CanBeActive</c> 上。
    /// 本模组或 Gravship Voyage 给电脑核心加上的逆重设施都会走到这里。
    /// 引擎名单来自 <see cref="GravshipEngineWhitelist"/>。
    ///
    /// 注意：原版这个属性碰到第一台距离不够的引擎就会直接失败，多船或模组引擎时会连错、连不上。
    /// 这里改成跳过距离不够的引擎，只接受占地每一格都属于那台引擎的飞船地板。
    /// 松散连接不算。断电或故障（如果有这些组件）时也不算连上。
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
