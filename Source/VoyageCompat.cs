using System.Reflection;
using HarmonyLib;
using Verse;

namespace ShipCorePersona
{
    /// <summary>
    /// 用本模组的飞船主脑替换 Gravship Voyage 随机生成的机械师。
    /// 不引用那个程序集。存档时先暂时清掉它的 host，避免同一个人被深拷贝两次。
    /// </summary>
    public static class VoyageCompat
    {
        private const string CoreTypeName = "GravshipVoyage.CompAICore";

        private static readonly System.Type CoreType = AccessTools.TypeByName(CoreTypeName);
        private static readonly FieldInfo HostField = CoreType == null ? null : AccessTools.Field(CoreType, "host");
        private static readonly FieldInfo ChipField = CoreType == null ? null : AccessTools.Field(CoreType, "chip");
        private static readonly FieldInfo MindField = CoreType == null ? null : AccessTools.Field(CoreType, "mind");

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
            harmony.Patch(
                AccessTools.Method(CoreType, "GiveTo"),
                postfix: new HarmonyMethod(typeof(VoyageCompat), nameof(PostfixChipLeft)));
            harmony.Patch(
                AccessTools.Method(CoreType, "TryWithdraw"),
                postfix: new HarmonyMethod(typeof(VoyageCompat), nameof(PostfixChipLeft)));

            Log.Message("[ShipCorePersona] 已用飞船主脑替换 Gravship Voyage 随机生成的机械师。");
        }

        public static bool ShowsMechanitorGizmos(Thing core)
        {
            return IsLoaded && ChipSeated(core);
        }

        /// <summary>
        /// 读取人格核心的名字。
        /// 芯片还在核心里、名字还空着时，调用那边的 EnsureNamed，只取一次。
        /// 芯片已经离开核心时，核心上的 mind 是空的，这时只读 AIName，不再另外取名字。
        /// </summary>
        public static string CoreName(Thing core)
        {
            if (core == null || !IsLoaded)
                return null;

            ThingComp voyage = VoyageComp(core);
            if (voyage == null)
                return null;

            object mind = MindField?.GetValue(voyage);
            bool seated = ChipField?.GetValue(voyage) is bool chip && chip;
            if (seated && mind != null)
            {
                string ensured = mind.GetType().GetMethod("EnsureNamed")?.Invoke(mind, null) as string;
                if (!string.IsNullOrEmpty(ensured))
                    return ensured;
            }

            string aiName = voyage.GetType().GetProperty("AIName")?.GetValue(voyage) as string;
            if (string.IsNullOrEmpty(aiName) || aiName == "the persona")
                return null;
            return aiName;
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

        public static void ClearHost(Thing core, Pawn persona)
        {
            if (HostField == null || core == null || persona == null)
                return;

            ThingComp voyage = VoyageComp(core);
            if (voyage == null)
                return;
            if (HostField.GetValue(voyage) == persona)
                HostField.SetValue(voyage, null);
        }

        public static void PostfixChipLeft(ThingComp __instance)
        {
            if (__instance?.parent == null || ChipSeated(__instance.parent))
                return;

            __instance.parent.TryGetComp<CompShipCorePersona>()?.DismissPersona();
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
