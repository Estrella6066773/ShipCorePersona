using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;
using Verse.AI;

namespace ShipCorePersona
{
    /// <summary>
    /// 飞船主脑不在地图上时，原版不让她指挥机械族。电脑核心还在，就允许指挥。
    /// 倒地、被俘、精神崩溃仍按原版处理。
    /// </summary>
    [HarmonyPatch(typeof(Pawn_MechanitorTracker), "get_CanControlMechs")]
    public static class Patch_CorePersonaCanControlMechs
    {
        public static void Postfix(ref AcceptanceReport __result, Pawn ___pawn)
        {
            if (__result.Accepted)
                return;

            Pawn pawn = ___pawn;
            if (!CorePersonaUtility.IsPersona(pawn))
                return;
            if (pawn.Downed || pawn.IsPrisoner || pawn.InMentalState)
                return;

            Thing core = CorePersonaUtility.CoreOf(pawn);
            if (core != null && core.Spawned)
                __result = true;
        }
    }

    /// <summary>
    /// 机械族的「选择监督者」在监督者还没生成时是灰的。这里要能重新选中飞船主脑。
    /// </summary>
    [HarmonyPatch(typeof(MechanitorUtility), nameof(MechanitorUtility.GetMechGizmos))]
    public static class Patch_SelectCorePersonaOverseer
    {
        private static readonly AccessTools.FieldRef<Gizmo, bool> DisabledField =
            AccessTools.FieldRefAccess<Gizmo, bool>("disabled");

        public static void Postfix(Pawn mech, ref IEnumerable<Gizmo> __result)
        {
            Pawn overseer = mech.GetOverseer();
            if (!CorePersonaUtility.IsPersona(overseer))
                return;

            string label = "CommandSelectOverseer".Translate();
            IEnumerable<Gizmo> original = __result;
            __result = Enable(original, label);
        }

        private static IEnumerable<Gizmo> Enable(IEnumerable<Gizmo> gizmos, string selectLabel)
        {
            foreach (Gizmo gizmo in gizmos)
            {
                if (gizmo is Command command && command.defaultLabel == selectLabel && DisabledField(command))
                {
                    DisabledField(command) = false;
                    command.disabledReason = null;
                }

                yield return gizmo;
            }
        }
    }

    /// <summary>
    /// 机械师植入体只通过电脑核心存取。角色自己的手术单里不出现这些操作。
    /// </summary>
    [HarmonyPatch(typeof(RecipeWorker), nameof(RecipeWorker.AvailableOnNow))]
    public static class Patch_SurgeryOnlyImplants
    {
        public static void Postfix(Thing thing, ref bool __result)
        {
            if (!__result)
                return;
            if (thing is Pawn pawn && CorePersonaUtility.IsPersona(pawn))
                __result = false;
        }
    }

    /// <summary>
    /// 选中人格角色再点植入体时，原版会给出「给她安装」的菜单。安装和取出只走电脑核心。
    /// </summary>
    [HarmonyPatch(typeof(CompUsable), nameof(CompUsable.CompFloatMenuOptions))]
    public static class Patch_PersonaDoesNotUseImplants
    {
        public static void Postfix(CompUsable __instance, Pawn myPawn, ref IEnumerable<FloatMenuOption> __result)
        {
            if (!CorePersonaUtility.IsPersona(myPawn))
                return;
            if (!ImplantAccess.IsMechanitorImplant(__instance.parent?.def))
                return;

            __result = System.Array.Empty<FloatMenuOption>();
        }
    }

    /// <summary>
    /// 拿着植入体点人格角色时，原版会把她当成安装目标。这个目标不可用。
    /// </summary>
    [HarmonyPatch(typeof(CompTargetable), nameof(CompTargetable.ValidateTarget))]
    public static class Patch_PersonaIsNotImplantTarget
    {
        public static void Postfix(CompTargetable __instance, LocalTargetInfo target, ref bool __result)
        {
            if (!__result)
                return;
            if (target.Thing is not Pawn pawn || !CorePersonaUtility.IsPersona(pawn))
                return;
            if (!ImplantAccess.IsMechanitorImplant(__instance.parent?.def))
                return;

            __result = false;
        }
    }

    /// <summary>
    /// 飞船主脑不能离开电脑核心。
    /// 释放能力和原地等待这两类工作要放行，否则能力放不出来，征召也会报错。
    /// </summary>
    [HarmonyPatch(typeof(Pawn_JobTracker), nameof(Pawn_JobTracker.StartJob))]
    public static class Patch_PersonaCannotAct
    {
        private static readonly HashSet<string> Allowed =
            new HashSet<string> { "RepairMechRemote", "Wait", "Wait_Combat", "Wait_MaintainPosture" };

        public static bool Prefix(Pawn ___pawn, Job newJob)
        {
            if (!CorePersonaUtility.IsPersona(___pawn))
                return true;
            if (newJob?.ability != null)
                return true;

            string defName = newJob?.def?.defName;
            return defName != null && Allowed.Contains(defName);
        }
    }

    /// <summary>
    /// 飞船主脑不在地图上画出来。她仍生成在核心所占的格子上，能力用那个格子计算距离。
    /// </summary>
    [HarmonyPatch(typeof(Pawn), nameof(Pawn.DynamicDrawPhaseAt))]
    public static class Patch_PersonaHidden
    {
        public static bool Prefix(Pawn __instance)
        {
            return !CorePersonaUtility.IsPersona(__instance);
        }
    }

    [HarmonyPatch(typeof(Pawn), nameof(Pawn.DrawGUIOverlay))]
    public static class Patch_PersonaHiddenLabel
    {
        public static bool Prefix(Pawn __instance)
        {
            return !CorePersonaUtility.IsPersona(__instance);
        }
    }

    /// <summary>
    /// 飞船主脑放能力时不检查视线，距离仍要在能力范围内。
    /// 她在建筑里，建筑本身会挡住视线，所以这里直接放过视线检查。
    /// 开始施放时，原版另外调用 TryFindShootLineFromTo，不走 CanHitTargetFrom，所以两处都要放过。
    /// </summary>
    [HarmonyPatch(typeof(Verb), nameof(Verb.CanHitTargetFrom))]
    public static class Patch_PersonaIgnoresSight
    {
        public static bool Prefix(Verb __instance, IntVec3 root, LocalTargetInfo targ, ref bool __result)
        {
            if (__instance.caster is not Pawn pawn || !CorePersonaUtility.IsPersona(pawn))
                return true;

            __result = InRange(pawn, __instance, root, targ);
            return false;
        }

        internal static bool InRange(Pawn pawn, Verb verb, IntVec3 root, LocalTargetInfo targ)
        {
            Map map = pawn.MapHeld;
            if (map == null || !targ.IsValid || (targ.HasThing && targ.Thing.Map != map))
                return false;

            float range = verb.EffectiveRange;
            return root.DistanceToSquared(targ.Cell) <= range * range;
        }

        internal static bool NearCore(Pawn pawn, LocalTargetInfo targ)
        {
            Thing core = CorePersonaUtility.CoreOf(pawn);
            Map map = pawn.MapHeld;
            if (core == null || !core.Spawned || map == null || !targ.IsValid || (targ.HasThing && targ.Thing.Map != map))
                return false;

            IntVec3 closest = core.OccupiedRect().ClosestCellTo(targ.Cell);
            return closest.DistanceToSquared(targ.Cell) <= 2;
        }
    }

    [HarmonyPatch(typeof(Verb), nameof(Verb.TryFindShootLineFromTo))]
    public static class Patch_PersonaShootLineIgnoresSight
    {
        public static bool Prefix(Verb __instance, IntVec3 root, LocalTargetInfo targ, ref ShootLine resultingLine, bool ignoreRange, ref bool __result)
        {
            if (__instance.caster is not Pawn pawn || !CorePersonaUtility.IsPersona(pawn))
                return true;

            if (!ignoreRange && !CanUse(__instance, pawn, root, targ))
            {
                resultingLine = new ShootLine(root, targ.Cell);
                __result = false;
                return false;
            }

            resultingLine = new ShootLine(root, targ.Cell);
            __result = true;
            return false;
        }

        private static bool CanUse(Verb verb, Pawn pawn, IntVec3 root, LocalTargetInfo targ)
        {
            if (verb.EffectiveRange <= 0f)
                return Patch_PersonaIgnoresSight.NearCore(pawn, targ);
            return Patch_PersonaIgnoresSight.InRange(pawn, verb, root, targ);
        }
    }

    /// <summary>
    /// 近身能力原版要求人走到目标旁边。人站在建筑里走不出去，于是每个目标都选不中。
    /// 挨着电脑核心的目标算够得着。有射程的能力仍按距离判断，不看视线。
    /// </summary>
    [HarmonyPatch(typeof(Verb_CastAbility), nameof(Verb_CastAbility.ValidateTarget))]
    public static class Patch_PersonaAbilityTarget
    {
        public static bool Prefix(Verb_CastAbility __instance, LocalTargetInfo target, bool showMessages, ref bool __result)
        {
            Pawn pawn = __instance.ability?.pawn;
            if (!CorePersonaUtility.IsPersona(pawn) || __instance.EffectiveRange > 0f)
                return true;

            if (!Patch_PersonaIgnoresSight.NearCore(pawn, target) || !__instance.IsApplicableTo(target, showMessages))
            {
                __result = false;
                return false;
            }

            List<CompAbilityEffect> effects = __instance.ability.EffectComps;
            for (int i = 0; i < effects.Count; i++)
            {
                if (!effects[i].Valid(target, showMessages))
                {
                    __result = false;
                    return false;
                }
            }

            __result = true;
            return false;
        }
    }

    /// <summary>
    /// 原版找施法格子时，会挑一个看得见目标的位置，人就会走出建筑。
    /// 飞船主脑就在当前格子施放。
    /// </summary>
    [HarmonyPatch(typeof(CastPositionFinder), nameof(CastPositionFinder.TryFindCastPosition))]
    public static class Patch_PersonaCastsFromCore
    {
        public static bool Prefix(CastPositionRequest newReq, ref IntVec3 dest, ref bool __result)
        {
            if (newReq.caster is not Pawn pawn || !CorePersonaUtility.IsPersona(pawn))
                return true;
            if (newReq.verb == null || !newReq.verb.CanHitTargetFrom(pawn.Position, newReq.target))
            {
                __result = false;
                return false;
            }

            dest = pawn.Position;
            __result = true;
            return false;
        }
    }

    /// <summary>
    /// 殖民者名单是疾病、社交冲突这些事件的抽人来源。飞船主脑不进这些名单。
    /// 上方栏另外把她加回去，所以设施里仍能看见她。
    /// </summary>
    [HarmonyPatch(typeof(MapPawns), nameof(MapPawns.FreeHumanlikesOfFaction))]
    public static class Patch_PersonaNotInColonistList
    {
        public static void Postfix(ref List<Pawn> __result)
        {
            StripPersonas(__result);
        }

        internal static void StripPersonas(List<Pawn> pawns)
        {
            if (pawns == null)
                return;

            for (int i = pawns.Count - 1; i >= 0; i--)
            {
                if (CorePersonaUtility.IsPersona(pawns[i]))
                    pawns.RemoveAt(i);
            }
        }
    }

    [HarmonyPatch(typeof(MapPawns), nameof(MapPawns.FreeHumanlikesSpawnedOfFaction))]
    public static class Patch_PersonaNotInSpawnedColonistList
    {
        public static void Postfix(ref List<Pawn> __result)
        {
            Patch_PersonaNotInColonistList.StripPersonas(__result);
        }
    }

    /// <summary>
    /// 离开地图时原版会把人放进世界人物池。飞船主脑只留在专属人物池里。
    /// </summary>
    [HarmonyPatch(typeof(WorldPawns), nameof(WorldPawns.PassToWorld))]
    public static class Patch_PersonaSkipsWorldPawnPool
    {
        public static void Postfix(Pawn pawn)
        {
            if (!CorePersonaUtility.IsPersona(pawn))
                return;
            if (!pawn.Spawned && !GameComponent_PersonaPool.Contains(pawn))
                return;
            if (Find.WorldPawns.Contains(pawn))
                Find.WorldPawns.RemovePawn(pawn);
        }
    }

    [HarmonyPatch(typeof(MentalBreaker), "get_CanDoRandomMentalBreaks")]
    public static class Patch_PersonaNoMentalBreak
    {
        public static void Postfix(Pawn ___pawn, ref bool __result)
        {
            if (CorePersonaUtility.IsPersona(___pawn))
                __result = false;
        }
    }

    [HarmonyPatch(typeof(InspirationHandler), nameof(InspirationHandler.TryStartInspiration))]
    public static class Patch_PersonaNoInspiration
    {
        public static bool Prefix(InspirationHandler __instance)
        {
            return !CorePersonaUtility.IsPersona(__instance.pawn);
        }
    }

    /// <summary>
    /// 殖民者名单里没有飞船主脑，这里把她加回上方栏。从设施卸下后她不在地图上，栏里也就没有她。
    /// </summary>
    [HarmonyPatch(typeof(ColonistBar), "CheckRecacheEntries")]
    public static class Patch_PersonaOnColonistBar
    {
        private static bool adding;

        public static void Postfix(ColonistBar __instance)
        {
            if (adding || Find.PlaySettings == null || !Find.PlaySettings.showColonistBar)
                return;

            List<ColonistBar.Entry> entries = AccessTools.Field(typeof(ColonistBar), "cachedEntries").GetValue(__instance) as List<ColonistBar.Entry>;
            if (entries == null)
                return;

            bool added = false;
            for (int mapIndex = 0; mapIndex < Find.Maps.Count; mapIndex++)
            {
                Map map = Find.Maps[mapIndex];
                int group = -1;
                for (int i = 0; i < entries.Count; i++)
                {
                    if (entries[i].map == map)
                        group = entries[i].group;
                }

                if (group < 0)
                    continue;

                List<Pawn> spawned = map.mapPawns.SpawnedPawnsInFaction(Faction.OfPlayer);
                for (int i = 0; i < spawned.Count; i++)
                {
                    Pawn pawn = spawned[i];
                    if (!CorePersonaUtility.IsPersona(pawn) || pawn.Dead)
                        continue;
                    if (Listed(entries, pawn))
                        continue;

                    for (int entryIndex = entries.Count - 1; entryIndex >= 0; entryIndex--)
                    {
                        if (entries[entryIndex].map == map && entries[entryIndex].pawn == null)
                            entries.RemoveAt(entryIndex);
                    }

                    entries.Add(new ColonistBar.Entry(pawn, map, group));
                    added = true;
                }
            }

            if (!added)
                return;

            if (AccessTools.Field(typeof(ColonistBar), "cachedReorderableGroups").GetValue(__instance) is List<int> reorder)
            {
                reorder.Clear();
                for (int i = 0; i < entries.Count; i++)
                    reorder.Add(-1);
            }

            int groups = 0;
            for (int i = 0; i < entries.Count; i++)
            {
                if (entries[i].group + 1 > groups)
                    groups = entries[i].group + 1;
            }

            adding = true;
            try
            {
                List<Vector2> locs = AccessTools.Field(typeof(ColonistBar), "cachedDrawLocs").GetValue(__instance) as List<Vector2>;
                object finder = AccessTools.Field(typeof(ColonistBar), "drawLocsFinder").GetValue(__instance);
                object[] args = { locs, 1f, groups };
                AccessTools.Method(typeof(ColonistBarDrawLocsFinder), "CalculateDrawLocs").Invoke(finder, args);
                AccessTools.Field(typeof(ColonistBar), "cachedScale").SetValue(__instance, args[1]);
                __instance.drawer.Notify_RecachedEntries();
            }
            finally
            {
                adding = false;
            }
        }

        private static bool Listed(List<ColonistBar.Entry> entries, Pawn pawn)
        {
            for (int i = 0; i < entries.Count; i++)
            {
                if (entries[i].pawn == pawn)
                    return true;
            }

            return false;
        }
    }
}
