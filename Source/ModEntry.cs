using System.Reflection;
using HarmonyLib;
using Verse;

namespace ShipCorePersona
{
    /// <summary>
    /// 模组入口。补丁挂上之后再登记逆重引擎，因为那时定义已经加载完。
    /// </summary>
    [StaticConstructorOnStartup]
    internal static class ModEntry
    {
        public const string HarmonyId = "Estrella6066.ShipCorePersona";

        static ModEntry()
        {
            var harmony = new Harmony(HarmonyId);
            harmony.PatchAll(Assembly.GetExecutingAssembly());
            GravshipEngineWhitelist.Register();
            VoyageCompat.TryPatch(harmony);
        }
    }
}
