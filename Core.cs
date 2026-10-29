using Il2Cpp;
using Il2CppFishNet;
using MelonLoader;
using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

[assembly: MelonInfo(typeof(CustomMapsFramework.Core), "CustomMapsFramework", "1.1.1", "Nullframe Collective")]
[assembly: MelonGame("ZeoWorks", "Slendytubbies 3")]
[assembly: MelonColor(255, 230, 126, 34)]

namespace CustomMapsFramework
{
    public class Core : MelonMod
    {
        public static bool IsCustomMap;
        public static MapInfo CurrentMap;
        public static string CustomMapsDirectory = Path.Combine(MelonLoader.Utils.MelonEnvironment.UserDataDirectory, "CustomMaps");
        public static List<MapInfo> CustomMaps = new();
        public static bool _isLoaded;
        private static readonly Dictionary<string, Il2CppAssetBundle> Bundles = new(StringComparer.OrdinalIgnoreCase);
        private static int _scene = -1;
        private static int _generation;
        private static string _sceneName;
        private static MapInfo _musicLoading;
        public static string Error { get; private set; }
        public static bool IsInitialized { get; private set; }
        public static bool AssetsReady => IsInitialized;

        public override void OnInitializeMelon()
        {
            LoggerInstance.Msg("Initialized.");
            MapContent.Load();
            IsInitialized = true;
        }

        public override void OnSceneWasLoaded(int buildIndex, string sceneName) => CheckForCustomMap(sceneName);

        public override void OnSceneWasInitialized(int buildIndex, string sceneName)
        {
            if (!IsCustomMap || FindMap(sceneName) != CurrentMap) return;

            Scene scene = SceneManager.GetSceneByName(sceneName);
            if (!scene.IsValid() || !scene.isLoaded || _scene == scene.handle) return;

            _scene = scene.handle;
            MapTriggers.Setup(scene, CurrentMap);
            MelonCoroutines.Start(SpawnRoom());
        }

        public override void OnUpdate() => MapTriggers.Update();

        public override void OnDeinitializeMelon()
        {
            Reset();

            foreach (Il2CppAssetBundle bundle in Bundles.Values)
            {
                bundle?.Unload(false);
            }

            Bundles.Clear();
        }

        internal static void Report(string error)
        {
            Error = error;
            MelonLogger.Error("CustomMaps: " + error);
        }

        internal static string SceneName(string name)
        {
            string file = Path.GetFileName((name ?? "").Replace('\\', '/'));

            return file.EndsWith(".unity", StringComparison.OrdinalIgnoreCase) ? file[..^6] : file;
        }

        internal static MapInfo FindMap(string scene) => CustomMaps.FirstOrDefault(map =>
            map.Scenes?.Any(name => string.Equals(name, SceneName(scene), StringComparison.Ordinal)) == true);

        internal static bool PrepareScene(string scene)
        {
            MapInfo map = FindMap(scene);
            if (!MapCatalog.Allows(map))
            {
                if (IsCustomMap && !string.IsNullOrWhiteSpace(scene))
                {
                    Reset();
                }

                return true;
            }

            CurrentMap = map;
            IsCustomMap = true;
            _isLoaded = true;
            Error = null;

            if (!map.MusicLoaded && _musicLoading != map)
            {
                MelonCoroutines.Start(SetupMusic(map));
            }

            return true;
        }

        private static void Reset()
        {
            MapTriggers.Clear();
            MapEnvironment.Reset();
            _generation++;
            _scene = -1;
            _sceneName = null;
            _isLoaded = false;
            IsCustomMap = false;
            CurrentMap = null;
        }

        public static void CheckForCustomMap(string mapName)
        {
            if (_sceneName == mapName && IsCustomMap && FindMap(mapName) == CurrentMap) return;

            Reset();
            _sceneName = mapName;
            PrepareScene(mapName);
        }

        public static void LoadMap(MapInfo mapInfo, string sceneSuffix = "")
        {
            if (mapInfo == null) return;

            LoadSceneBundle(mapInfo.BundlePath);
            PrepareScene(mapInfo.Map.mapName + sceneSuffix);
        }

        public static void LoadSceneBundle(string bundleFilePath)
        {
            string path = Path.GetFullPath(bundleFilePath);
            if (Bundles.ContainsKey(path)) return;

            Il2CppAssetBundle bundle = Il2CppAssetBundleManager.LoadFromFile(path);
            if (bundle == null) throw new InvalidDataException("Cannot load scene bundle: " + path);

            if (!bundle.isStreamedSceneAssetBundle)
            {
                bundle.Unload(false);
                throw new InvalidDataException("A map bundle must contain scenes: " + path);
            }

            Bundles[path] = bundle;
        }

        internal static void UnloadSceneBundle(string path)
        {
            if (Bundles.Remove(Path.GetFullPath(path), out Il2CppAssetBundle bundle))
            {
                bundle?.Unload(false);
            }
        }

        internal static string[] ScenesFor(string path) => Bundles[Path.GetFullPath(path)].GetAllScenePaths().Select(SceneName).ToArray();

        public static void AddCustomMaps(LobbyMenu menu)
        {
            if (!menu || menu.CFJBONPPILK == null) return;

            foreach (MapInfo map in CustomMaps.Where(MapCatalog.Allows))
            {
                if (menu.CFJBONPPILK.ToArray().Any(value => value != null && value.mapName == map.Map.mapName)) continue;

                if (!map.Map.mapPreview)
                {
                    try
                    {
                        map.Map.mapPreview = LoadNewSprite(Path.Combine(map.AssetsPath, "preview.png"));
                    }
                    catch (Exception exception)
                    {
                        MelonLogger.Warning("CustomMaps: Cannot load preview: " + exception.Message);
                    }
                }

                menu.CFJBONPPILK.Add(map.Map);
            }
        }

        public static IEnumerator SetupMusic(MapInfo map)
        {
            if (map == null || map.MusicLoaded || _musicLoading == map) yield break;

            _musicLoading = map;
            try
            {
                yield return MapEnvironment.LoadMusic(map);
                map.MusicLoaded = true;

                RoomMultiplayerMenu room = RoomMultiplayerMenu.GJDGDIGECJN;
                if (CurrentMap == map && IsCustomMap && room)
                {
                    MapEnvironment.ApplyMusic(room);
                }
            }
            finally
            {
                if (_musicLoading == map)
                {
                    _musicLoading = null;
                }
            }
        }

        public static IEnumerator SetupWaves(SurvivalMechanics mechanics)
        {
            MapEnvironment.ApplyWaves(mechanics);
            yield break;
        }

        // Waits up to 10 seconds for the network to start, a newer map load cancels the wait
        public static IEnumerator SpawnRoom()
        {
            int generation = _generation;
            float deadline = Time.realtimeSinceStartup + 10f;
            while (IsCustomMap && generation == _generation && !InstanceFinder.IsServerStarted && !InstanceFinder.IsClientStarted &&
                Time.realtimeSinceStartup < deadline)
            {
                yield return null;
            }

            if (!IsCustomMap || generation != _generation) yield break;

            if (InstanceFinder.IsServerStarted && !RoomMultiplayerMenu.GJDGDIGECJN)
            {
                CreateRoom();
            }

            SetupAudioSources();
        }

        private static void CreateRoom()
        {
            GameObject prefab = Resources.Load<GameObject>("__Room");
            if (!prefab)
            {
                Report("The game does not expose the __Room prefab required by this map.");
                return;
            }

            GameObject room = UnityEngine.Object.Instantiate(prefab, Vector3.zero, Quaternion.identity);
            room.name = "__Room";

            Scene scene = SceneManager.GetSceneByName(_sceneName ?? "");
            if (scene.IsValid() && scene.isLoaded)
            {
                SceneManager.MoveGameObjectToScene(room, scene);
            }

            InstanceFinder.ServerManager.Spawn(room);
        }

        public static void SetupAudioSources() => MapEnvironment.RouteAudio(SceneManager.GetSceneByName(_sceneName ?? ""));

        public static void SetupTriggersOld() => SetupTriggers();

        public static void SetupTriggers()
        {
            Scene scene = SceneManager.GetSceneByName(_sceneName ?? "");
            if (IsCustomMap && scene.IsValid())
            {
                MapTriggers.Setup(scene, CurrentMap);
            }
        }

        public static Sprite LoadNewSprite(string path, float pixelsPerUnit = 100f, SpriteMeshType spriteType = SpriteMeshType.Tight)
        {
            Texture2D texture = LoadTexture(path);
            return texture ? Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height), new Vector2(0.5f, 0.5f), pixelsPerUnit, 0, spriteType) : null;
        }

        public static Texture2D LoadTexture(string path)
        {
            if (!File.Exists(path)) return null;

            byte[] data = File.ReadAllBytes(path);
            if (data.Length > 8 * 1024 * 1024) throw new InvalidDataException("Map preview exceeds 8 MB.");

            Texture2D texture = new(2, 2, TextureFormat.RGBA32, false);
            if (ImageConversion.LoadImage(texture, data, true)) return texture;

            UnityEngine.Object.Destroy(texture);
            return null;
        }
    }
}
