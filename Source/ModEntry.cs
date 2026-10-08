using System.Reflection;
using HarmonyLib;
using Verse;

namespace ShipCorePersona
{
    /// <summary>
    /// 模组入口。启动时挂上本程序集里的 Harmony 补丁，把电脑核心登记进逆重引擎，再按需挂上对 Gravship Voyage 的兼容补丁。
    ///
    /// 联动关系：具体规则在各个补丁和 <see cref="CompShipCorePersona"/> 里。
    /// <see cref="GravshipEngineWhitelist.Register"/> 必须在定义加载完成之后调用，所以放在这里而不是更早的静态构造。
    /// Voyage 的类型不参与编译，只有游戏里真的加载了那个模组时，<see cref="VoyageCompat"/> 才会去反射它。
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
