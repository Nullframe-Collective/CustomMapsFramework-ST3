using Il2Cpp;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using MelonLoader;
using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Audio;

namespace CustomMapsFramework
{
    internal static class MapEnvironment
    {
        private static bool _playersReady;
        private static int _playersScene = -1;

        internal static void Reset()
        {
            _playersReady = false;
            _playersScene = -1;
        }

        // The native survival tick ends the round immediately when team1 is empty
        // A scene transition temporarily has no avatars while FishNet holds spawns, so the tick waits for the first player
        internal static bool CanTick(SurvivalMechanics mechanics)
        {
            if (!Core.IsCustomMap || !mechanics) return true;

            int scene = mechanics.gameObject.scene.handle;
            if (_playersScene != scene)
            {
                _playersScene = scene;
                _playersReady = false;
            }

            if (_playersReady) return true;

            var scenes = Il2CppFishNet.InstanceFinder.SceneManager;
            if (scenes && scenes.IteratingQueue || GameObject.FindGameObjectsWithTag("team1").Length == 0) return false;

            _playersReady = true;
            return true;
        }

        internal static IEnumerable<T> Components<T>(Scene scene) where T : Component =>
            scene.IsValid() && scene.isLoaded ? scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<T>(true)) : Array.Empty<T>();

        internal static IEnumerator LoadMusic(MapInfo map)
        {
            string path = Path.Combine(map.AssetsPath, "music");
            if (File.Exists(path))
            {
                LoadMusicBundle(map, path);
            }
            else if (Directory.Exists(path))
            {
                yield return LoadMusicFolder(map, path);
            }
        }

        private static void LoadMusicBundle(MapInfo map, string path)
        {
            Il2CppAssetBundle bundle = Il2CppAssetBundleManager.LoadFromFile(path);
            if (bundle == null)
            {
                MelonLogger.Warning("CustomMaps: Cannot load music bundle for " + map.Map.mapName);
                return;
            }

            try
            {
                map.Ambient = bundle.Load<AudioClip>("ambient");

                for (int index = 0; index < (map.Waves?.Count ?? 0); index++)
                {
                    map.Waves[index].WaveInfo.Music = bundle.Load<AudioClip>("wave" + (index + 1));
                }
            }
            finally
            {
                bundle.Unload(false);
            }
        }

        private static IEnumerator LoadMusicFolder(MapInfo map, string path)
        {
            yield return LoadClip(Path.Combine(path, "ambient.mp3"), clip => map.Ambient = clip);

            for (int index = 0; index < (map.Waves?.Count ?? 0); index++)
            {
                CustomWave wave = map.Waves[index];
                yield return LoadClip(Path.Combine(path, "wave" + (index + 1) + ".mp3"), clip => wave.WaveInfo.Music = clip);
            }
        }

        private static IEnumerator LoadClip(string path, Action<AudioClip> ready)
        {
            if (!File.Exists(path)) yield break;

            WWW request = new(new Uri(Path.GetFullPath(path)).AbsoluteUri);
            try
            {
                yield return request;

                if (string.IsNullOrEmpty(request.error))
                {
                    ready(request.GetAudioClip());
                }
                else
                {
                    MelonLogger.Warning("CustomMaps: Cannot load music: " + request.error);
                }
            }
            finally
            {
                request.Dispose();
            }
        }

        internal static void ApplyMusic(RoomMultiplayerMenu room)
        {
            if (!Core.IsCustomMap || Core.CurrentMap == null || !room) return;

            AudioSource source = room.GetComponent<AudioSource>();
            if (source && Core.CurrentMap.Ambient && source.clip != Core.CurrentMap.Ambient)
            {
                source.clip = Core.CurrentMap.Ambient;
                source.Play();
            }

            foreach (SurvivalMechanics mechanics in room.GetComponentsInChildren<SurvivalMechanics>(true))
            {
                if (Core.CurrentMap.Waves == null || mechanics.LKKBNLDMNDL == null) continue;

                for (int index = 0; index < Math.Min(Core.CurrentMap.Waves.Count, mechanics.LKKBNLDMNDL.Count); index++)
                {
                    AudioClip music = Core.CurrentMap.Waves[index].WaveInfo.Music;
                    if (music)
                    {
                        mechanics.LKKBNLDMNDL[index].music = music;
                    }
                }
            }
        }

        internal static void ApplyWaves(SurvivalMechanics mechanics)
        {
            if (!Core.IsCustomMap || Core.CurrentMap == null || !mechanics || mechanics.LKKBNLDMNDL == null) return;

            MapInfo map = Core.CurrentMap;
            mechanics.GFHEICDOFEP = map.IsOutside;

            if (map.Waves is { Count: > 0 })
            {
                ReplaceWaves(mechanics, map);
            }
            else
            {
                UseMapMonster(mechanics, map.Monsters[2]);
            }

            ApplyWaveLimit(mechanics);
        }

        private static void ReplaceWaves(SurvivalMechanics mechanics, MapInfo map)
        {
            mechanics.LKKBNLDMNDL.Clear();

            foreach (CustomWave wave in map.Waves)
            {
                SurvivalMechanics.specialNPC[] special = (wave.WaveInfo.NPCs ?? Array.Empty<CustomWave.SpecialNPC>()).Select(npc =>
                    new SurvivalMechanics.specialNPC { npcName = npc.NPCName, spawnChance = npc.SpawnChance, isBoss = npc.IsBoss }).ToArray();

                mechanics.LKKBNLDMNDL.Add(new SurvivalMechanics.waveInfo
                {
                    defaultNPC = wave.WaveInfo.DefaultNPC,
                    totalCount = wave.WaveInfo.TotalCount,
                    music = wave.WaveInfo.Music ? wave.WaveInfo.Music : map.Ambient,
                    NPC = new Il2CppReferenceArray<SurvivalMechanics.specialNPC>(special)
                });
            }
        }

        private static void UseMapMonster(SurvivalMechanics mechanics, string monster)
        {
            foreach (SurvivalMechanics.waveInfo wave in mechanics.LKKBNLDMNDL)
            {
                if (wave != null)
                {
                    wave.defaultNPC = monster;
                }
            }

            if (mechanics.FNKMHJJNLJF != null)
            {
                mechanics.FNKMHJJNLJF.defaultNPC = monster;
            }
        }

        internal static void ApplyWaveLimit(SurvivalMechanics mechanics)
        {
            if (!Core.IsCustomMap || Core.CurrentMap?.Waves is not { Count: > 0 } waves || !mechanics) return;

            int wave = Math.Clamp(mechanics.PKONLONPGNP, 0, waves.Count - 1);
            mechanics.POJLLLGLPKL = waves[wave].MaxNPC;
        }

        internal static void RouteAudio(Scene scene)
        {
            AudioMixerManager manager = UnityEngine.Object.FindObjectOfType<AudioMixerManager>();
            if (!manager || !manager.EEJANKGKOBO) return;

            AudioMixerGroup[] groups = Resources.FindObjectsOfTypeAll<AudioMixerGroup>().Where(group => group && group.audioMixer == manager.EEJANKGKOBO).ToArray();
            var music = groups.FirstOrDefault(group => group.name.Equals("Music", StringComparison.OrdinalIgnoreCase));
            var effects = groups.FirstOrDefault(group => group.name.Equals("SFX", StringComparison.OrdinalIgnoreCase));

            foreach (AudioSource source in Components<AudioSource>(scene))
            {
                if (source.outputAudioMixerGroup || !source.clip) continue;

                var group = source.clip.length >= 30f ? music : effects;
                if (group)
                {
                    source.outputAudioMixerGroup = group;
                }
            }
        }
    }
}
