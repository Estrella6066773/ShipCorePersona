using System.Reflection;
using HarmonyLib;
using Verse;

namespace ShipCorePersona
{
    /// <summary>
    /// 和 Gravship Voyage 共用同一台电脑核心时，用本模组的人格替换它生成的随机隐藏机械师。
    ///
    /// 联动关系：
    /// Voyage 的 CompAICore.Wake 原本会用 Mechanitor_Basic 随机生成一个不在地图上的人，并把它当成机械师。
    /// 这里在 Wake 之前把 host 设成核心里的人格，于是后续的 Attach 会把人格芯片装到这个人身上。
    /// 存档时把 Voyage 的 host 临时清空，人格只由 <see cref="CompShipCorePersona"/> 的容器保存，避免同一个人被深拷贝两次。
    ///
    /// 注意：本类不引用 GravshipVoyage 程序集。那个模组没启用时，类型找不到，这里什么也不做。
    /// 不要在芯片尚未装上时移除机械链路，否则原版会断开机械族。带宽校正在芯片装上之后由 SyncBandwidth 处理。
    /// </summary>
    public static class VoyageCompat
    {
        private const string CoreTypeName = "GravshipVoyage.CompAICore";

        private static readonly System.Type CoreType = AccessTools.TypeByName(CoreTypeName);
        private static readonly FieldInfo HostField = CoreType == null ? null : AccessTools.Field(CoreType, "host");
        private static readonly FieldInfo ChipField = CoreType == null ? null : AccessTools.Field(CoreType, "chip");

        public static bool IsLoaded => CoreType != null;

        public static void TryPatch(Harmony harmony)
        {
            if (CoreType == null)
                return;

            harmony.Patch(
                AccessTools.Method(CoreType, "Wake"),
                prefix: new HarmonyMethod(typeof(VoyageCompat), nameof(PrefixWake)));
            harmony.Patch(
                AccessTools.Method(CoreType, "Attach"),
                postfix: new HarmonyMethod(typeof(VoyageCompat), nameof(PostfixAttach)));
            harmony.Patch(
                AccessTools.Method(CoreType, "PostExposeData"),
                prefix: new HarmonyMethod(typeof(VoyageCompat), nameof(PrefixExpose)),
                postfix: new HarmonyMethod(typeof(VoyageCompat), nameof(PostfixExpose)));

            Log.Message("[ShipCorePersona] 已接管 Gravship Voyage 的电脑核心人格。");
        }

        public static bool ChipSeated(Thing core)
        {
            if (ChipField == null || core == null)
                return false;

            ThingComp voyage = VoyageComp(core);
            return voyage != null && ChipField.GetValue(voyage) is bool seated && seated;
        }

        public static void ClaimHost(CompShipCorePersona comp)
        {
            if (HostField == null || comp?.parent == null || comp.Persona == null)
                return;

            ThingComp voyage = VoyageComp(comp.parent);
            if (voyage == null)
                return;

            Pawn previous = HostField.GetValue(voyage) as Pawn;
            if (previous != null && previous != comp.Persona)
            {
                CorePersonaUtility.TransferMechs(previous, comp.Persona);
                if (!previous.Destroyed && !previous.Spawned)
                    previous.Destroy(DestroyMode.Vanish);
            }

            HostField.SetValue(voyage, comp.Persona);
        }

        public static bool PrefixWake(ThingComp __instance)
        {
            CompShipCorePersona comp = __instance?.parent?.TryGetComp<CompShipCorePersona>();
            if (comp == null)
                return true;

            comp.EnsurePersona();
            ClaimHost(comp);
            return comp.Persona == null;
        }

        public static void PostfixAttach(ThingComp __instance)
        {
            CompShipCorePersona comp = __instance?.parent?.TryGetComp<CompShipCorePersona>();
            if (comp?.Persona != null)
                CorePersonaUtility.SyncBandwidth(comp.Persona, comp.parent);
        }

        public static void PrefixExpose(ThingComp __instance, ref Pawn __state)
        {
            if (HostField == null || Scribe.mode != LoadSaveMode.Saving)
                return;

            if (HostField.GetValue(__instance) is Pawn host && CorePersonaUtility.IsPersona(host))
            {
                __state = host;
                HostField.SetValue(__instance, null);
            }
        }

        public static void PostfixExpose(ThingComp __instance, Pawn __state)
        {
            if (HostField == null)
                return;

            if (__state != null)
                HostField.SetValue(__instance, __state);

            if (Scribe.mode != LoadSaveMode.PostLoadInit)
                return;

            CompShipCorePersona comp = __instance?.parent?.TryGetComp<CompShipCorePersona>();
            ClaimHost(comp);
        }

        private static ThingComp VoyageComp(Thing thing)
        {
            if (CoreType == null || thing is not ThingWithComps withComps)
                return null;

            for (int i = 0; i < withComps.AllComps.Count; i++)
            {
                ThingComp comp = withComps.AllComps[i];
                if (CoreType.IsInstanceOfType(comp))
                    return comp;
            }

            return null;
        }
    }
}
