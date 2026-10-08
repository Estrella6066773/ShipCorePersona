using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;

namespace ShipCorePersona
{
    /// <summary>
    /// 核心人格没有生成在地图上，原版会因此拒绝让他指挥机械族。
    /// 电脑核心还在地图上时，视同这个人在自己的岗位上。
    ///
    /// 注意：倒地、被囚或精神崩溃时不放开这个限制，那些仍按原版处理。
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
    /// 核心人格不在地图上时，原版的「选择监督者」按钮是灰的。
    /// 机械族仍应能点回住在电脑核心里的人格。
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
    /// 健康页默认会给人类列出全部手术。核心人格只保留本模组的机械师植入体手术。
    /// </summary>
    [HarmonyPatch(typeof(RecipeWorker), nameof(RecipeWorker.AvailableOnNow))]
    public static class Patch_SurgeryOnlyImplants
    {
        public static void Postfix(RecipeWorker __instance, Thing thing, ref bool __result)
        {
            if (!__result)
                return;
            if (thing is not Pawn pawn || !CorePersonaUtility.IsPersona(pawn))
                return;
            if (__instance is Recipe_InstallCorePersonaImplant)
                return;

            __result = false;
        }
    }

    /// <summary>
    /// 核心人格可以被征召，但只能把远程修理进行下去。
    /// 移动、射击、普通工作和自安装植入体都在这里停住。
    ///
    /// 注意：Wait 一类原地等待要放行，否则征召状态会因为没有工作而反复报错。
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

            string defName = newJob?.def?.defName;
            return defName != null && Allowed.Contains(defName);
        }
    }

    /// <summary>
    /// 远程修理的命中判断用施放者的 Map。核心人格没有生成出来，Map 是空的，原版会直接判失败。
    /// 这里改用电脑核心所在的地图，以及人格被放到核心旁边的那一格。
    /// </summary>
    [HarmonyPatch(typeof(Verb), nameof(Verb.CanHitTargetFrom))]
    public static class Patch_PersonaVerbUsesCoreMap
    {
        public static bool Prefix(Verb __instance, IntVec3 root, LocalTargetInfo targ, ref bool __result)
        {
            if (__instance.caster is not Pawn pawn || !CorePersonaUtility.IsPersona(pawn))
                return true;

            Map map = pawn.MapHeld;
            if (map == null || !targ.IsValid || (targ.HasThing && targ.Thing.Map != map))
            {
                __result = false;
                return false;
            }

            float range = __instance.EffectiveRange;
            if (root.DistanceToSquared(targ.Cell) > range * range)
            {
                __result = false;
                return false;
            }

            if (__instance.verbProps.requireLineOfSight && !GenSight.LineOfSight(root, targ.Cell, map, true))
            {
                __result = false;
                return false;
            }

            __result = true;
            return false;
        }
    }
}
