using System;
using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace ShipCorePersona
{
    /// <summary>
    /// 飞船电脑核心连上逆重飞船之后，指挥范围改成每台逆重引擎和子锚点的生效范围。
    /// 植入物和装备提供的遥控距离加在每台的半径上。没连上飞船时仍用原版范围。
    /// </summary>
    public static class PersonaCommand
    {
        private struct Circle
        {
            public IntVec3 Center;
            public float Radius;
        }

        private static readonly List<Circle> Circles = new List<Circle>();
        private static readonly HashSet<Thing> Seen = new HashSet<Thing>();
        private static List<StatDef> rangeStats;
        private static List<ThingDef> footprintDefs;

        public static bool DrawShipRange(Pawn persona)
        {
            if (!Collect(persona))
                return false;
            if (persona.mechanitor != null && persona.mechanitor.AnySelectedDraftedMechs)
            {
                for (int i = 0; i < Circles.Count; i++)
                {
                    Circle circle = Circles[i];
                    GenDraw.DrawRadiusRing(circle.Center, circle.Radius, Color.white, cell => Inside(cell, circle));
                }
            }

            return true;
        }

        public static bool CanCommand(Pawn persona, IntVec3 cell)
        {
            if (!Collect(persona))
                return false;

            for (int i = 0; i < Circles.Count; i++)
            {
                if (Inside(cell, Circles[i]))
                    return true;
            }

            return false;
        }

        public static bool BoundToShip(Pawn persona)
        {
            return Collect(persona);
        }

        private static bool Collect(Pawn persona)
        {
            Circles.Clear();
            if (!ModsConfig.OdysseyActive || !CorePersonaUtility.IsPersona(persona))
                return false;

            Thing core = CorePersonaUtility.CoreOf(persona);
            Map map = core?.Map;
            if (core == null || map == null || !core.Spawned)
                return false;

            EnsureDefs();
            float bonus = RadiusBonus(persona);
            Seen.Clear();
            foreach (Building_GravEngine engine in map.listerBuildings.AllBuildingsColonistOfClass<Building_GravEngine>())
            {
                if (engine == null || !engine.OnValidSubstructure(core))
                    continue;

                Add(engine, bonus);
                for (int d = 0; d < footprintDefs.Count; d++)
                {
                    List<Thing> things = map.listerThings.ThingsOfDef(footprintDefs[d]);
                    for (int t = 0; t < things.Count; t++)
                    {
                        Thing thing = things[t];
                        if (thing == null || thing == engine || !engine.OnValidSubstructure(thing))
                            continue;
                        Add(thing, bonus);
                    }
                }
            }

            Seen.Clear();
            return Circles.Count > 0;
        }

        private static void Add(Thing thing, float bonus)
        {
            if (thing == null || !Seen.Add(thing))
                return;

            CompProperties_SubstructureFootprint footprint = thing.def.GetCompProperties<CompProperties_SubstructureFootprint>();
            if (footprint == null || footprint.radius <= 0f)
                return;

            Circles.Add(new Circle
            {
                Center = thing.OccupiedRect().CenterCell,
                Radius = footprint.radius + bonus
            });
        }

        private static float RadiusBonus(Pawn pawn)
        {
            EnsureDefs();
            float bonus = 0f;
            for (int i = 0; i < rangeStats.Count; i++)
                bonus += pawn.GetStatValue(rangeStats[i]);
            return bonus;
        }

        private static bool Inside(IntVec3 cell, Circle circle)
        {
            return cell.DistanceToSquared(circle.Center) <= circle.Radius * circle.Radius;
        }

        private static void EnsureDefs()
        {
            if (rangeStats != null)
                return;

            rangeStats = new List<StatDef>();
            footprintDefs = new List<ThingDef>();
            foreach (StatDef stat in DefDatabase<StatDef>.AllDefsListForReading)
            {
                if (stat?.defName != null && stat.defName.IndexOf("RemoteControlDistance", StringComparison.OrdinalIgnoreCase) >= 0)
                    rangeStats.Add(stat);
            }

            foreach (ThingDef def in DefDatabase<ThingDef>.AllDefsListForReading)
            {
                if (def.GetCompProperties<CompProperties_SubstructureFootprint>() != null)
                    footprintDefs.Add(def);
            }
        }
    }

    [HarmonyPatch(typeof(Pawn_MechanitorTracker), nameof(Pawn_MechanitorTracker.CanCommandTo))]
    [HarmonyPriority(Priority.Last)]
    public static class Patch_PersonaCommandRange
    {
        public static void Postfix(Pawn ___pawn, LocalTargetInfo target, ref bool __result)
        {
            if (!PersonaCommand.BoundToShip(___pawn))
                return;

            Map map = ___pawn.MapHeld;
            __result = map != null && target.Cell.InBounds(map) && PersonaCommand.CanCommand(___pawn, target.Cell);
        }
    }
}
