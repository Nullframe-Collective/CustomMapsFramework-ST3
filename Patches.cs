using HarmonyLib;
using Il2Cpp;
using DefaultSceneProcessor = Il2CppFishNet.Managing.Scened.DefaultSceneProcessor;
using UnityEngine.SceneManagement;

namespace CustomMapsFramework
{
    [HarmonyPatch(typeof(DefaultSceneProcessor), nameof(DefaultSceneProcessor.BeginLoadAsync))]
    public static class LobbyMenuLoadScenePatch
    {
        public static void Prefix(string sceneName) => Core.PrepareScene(sceneName);
    }

    [HarmonyPatch(typeof(SceneManager), "LoadSceneAsyncNameIndexInternal")]
    internal static class LocalSceneLoadPatch
    {
        private static void Prefix(string sceneName) => Core.PrepareScene(sceneName);
    }

    [HarmonyPatch(typeof(ClassicMechanics), nameof(ClassicMechanics.Start))]
    public static class ClassicMechanicsAwakePatch
    {
        public static void Prefix(ClassicMechanics __instance)
        {
            if (!Core.IsCustomMap || Core.CurrentMap == null) return;

            if (__instance.BCCHIGOBOFJ is { Length: > 0 } && __instance.BCCHIGOBOFJ[0])
            {
                __instance.BCCHIGOBOFJ[0].name = Core.CurrentMap.Monsters[0];
            }

            __instance.FKEIPFJBJHP = Core.CurrentMap.Monsters[1];
        }
    }

    [HarmonyPatch(typeof(SurvivalMechanics), nameof(SurvivalMechanics.Awake))]
    public static class SurvivalMechanicsAwakePatch
    {
        public static void Postfix(SurvivalMechanics __instance) => MapEnvironment.ApplyWaves(__instance);
    }

    [HarmonyPatch(typeof(SurvivalMechanics), nameof(SurvivalMechanics.FixedUpdate))]
    public static class SurvivalMechanicsNextWave
    {
        // The native tick reads the zero-based wave index before spawning the next NPC
        public static bool Prefix(SurvivalMechanics __instance)
        {
            if (!MapEnvironment.CanTick(__instance)) return false;

            MapEnvironment.ApplyWaveLimit(__instance);
            return true;
        }
    }

    [HarmonyPatch(typeof(RoomMultiplayerMenu), nameof(RoomMultiplayerMenu.Awake))]
    public static class RoomMultiplayerMenuAwakePatch
    {
        public static void Prefix(RoomMultiplayerMenu __instance)
        {
            // Native weapon setup resolves this exact scene object name on every peer
            if (Core.IsCustomMap)
            {
                __instance.gameObject.name = "__Room";
            }
        }

        public static void Postfix(RoomMultiplayerMenu __instance) => MapEnvironment.ApplyMusic(__instance);
    }

    [HarmonyPatch(typeof(LobbyMenu), nameof(LobbyMenu.Start))]
    public static class LobbyMenuAwakePatch
    {
        [HarmonyPriority(Priority.First)]
        public static void Prefix(LobbyMenu __instance) => Core.AddCustomMaps(__instance);
    }
}
