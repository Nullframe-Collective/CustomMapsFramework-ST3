using Il2Cpp;
using MelonLoader;
using System.Text.Json;
using System.Security.Cryptography;
using System.Text;
using UnityEngine.SceneManagement;

namespace CustomMapsFramework
{
    internal static class MapContent
    {
        internal static readonly JsonSerializerOptions Json = new()
        {
            PropertyNameCaseInsensitive = true,
            IncludeFields = true
        };

        internal static T Read<T>(string path)
        {
            if (new FileInfo(path).Length > 1024 * 1024) throw new InvalidDataException("JSON exceeds 1 MB: " + path);

            return JsonSerializer.Deserialize<T>(File.ReadAllText(path), Json) ?? throw new InvalidDataException("Empty JSON: " + path);
        }

        internal static bool ValidId(string id) => id is { Length: > 2 and <= 128 } && id.Contains(':') &&
            id.All(character => character is >= 'a' and <= 'z' or >= '0' and <= '9' or ':' or '-' or '_' or '.');

        internal static bool Name(string name) => !string.IsNullOrWhiteSpace(name) && name.Length <= 128 && !name.Any(char.IsControl);

        internal static void Validate(MapJson map, List<CustomWave> waves)
        {
            if (map == null || !Name(map.MapName) || map.MapName.IndexOfAny(['/', '\\', ':']) >= 0 ||
                map.MapName.EndsWith(".unity", StringComparison.OrdinalIgnoreCase) || map.MapName.Length + (map.UseDayAndNight ? 8 : 0) > 128)
            {
                throw new InvalidDataException("Invalid map metadata.");
            }

            if (map.Id != null && !ValidId(map.Id) || map.Size < 0 ||
                !Name(map.CollectMonster) || !Name(map.VersusMonster) || !Name(map.SurvivalMonster) ||
                map.Dependencies == null || map.Dependencies.Count > 128 || map.Dependencies.Any(value => !Name(value)))
            {
                throw new InvalidDataException("Invalid map metadata.");
            }

            if (waves == null) return;
            if (waves.Count is < 1 or > 256) throw new InvalidDataException("Use 1 to 256 survival waves.");

            foreach (CustomWave wave in waves)
            {
                if (wave?.WaveInfo == null || wave.MaxNPC is < 1 or > 128 || wave.WaveInfo.TotalCount is < 1 or > 10000 ||
                    !Name(wave.WaveInfo.DefaultNPC) || wave.WaveInfo.NPCs?.Length > 128 ||
                    wave.WaveInfo.NPCs?.Any(npc => npc == null || !Name(npc.NPCName) || npc.SpawnChance is < 0 or > 100) == true)
                {
                    throw new InvalidDataException("Invalid survival wave.");
                }
            }
        }

        internal static string[] ExpectedScenes(MapJson map) => map.UseDayAndNight ?
            [map.MapName + " (Day)", map.MapName + " (Dusk)", map.MapName + " (Night)"] : [map.MapName];

        internal static string Hash(string directory)
        {
            List<string> files = new() { "bundle", "map.json", "waves.json", "triggers.json" };
            string music = Path.Combine(directory, "music");
            if (File.Exists(music))
            {
                files.Add("music");
            }
            else if (Directory.Exists(music))
            {
                files.AddRange(Directory.GetFiles(music, "*.mp3").Select(file => "music/" + Path.GetFileName(file)));
            }

            using IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            foreach (string name in files.Where(name => File.Exists(Path.Combine(directory, name))).OrderBy(name => name, StringComparer.Ordinal))
            {
                byte[] path = Encoding.UTF8.GetBytes(name);
                hash.AppendData(BitConverter.GetBytes(System.Net.IPAddress.HostToNetworkOrder(path.Length)));
                hash.AppendData(path);

                using FileStream stream = File.OpenRead(Path.Combine(directory, name));
                using SHA256 fileHash = SHA256.Create();
                hash.AppendData(fileHash.ComputeHash(stream));
            }

            return Convert.ToHexString(hash.GetHashAndReset());
        }

        internal static void Load()
        {
            Directory.CreateDirectory(Core.CustomMapsDirectory);
            Core.CustomMaps.Clear();
            HashSet<string> ids = new(StringComparer.Ordinal);
            HashSet<string> names = new(StringComparer.OrdinalIgnoreCase);

            for (int index = 0; index < SceneManager.sceneCountInBuildSettings; index++)
            {
                names.Add(Core.SceneName(SceneUtility.GetScenePathByBuildIndex(index)));
            }

            foreach (string directory in Directory.GetDirectories(Core.CustomMapsDirectory).OrderBy(value => value, StringComparer.Ordinal))
            {
                if (!File.Exists(Path.Combine(directory, "map.json"))) continue;

                try
                {
                    MapInfo info = ReadMap(directory, ids, names);
                    Core.CustomMaps.Add(info);
                    ids.Add(info.Id);

                    foreach (string scene in info.Scenes)
                    {
                        names.Add(scene);
                    }

                    MelonLogger.Msg($"CustomMaps: Registered '{info.Map.mapName.ToString()}' ({info.Scenes.Length} scenes).");
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException or ArgumentException or InvalidDataException)
                {
                    Core.UnloadSceneBundle(Path.Combine(directory, "bundle"));
                    MelonLogger.Error($"Cannot load map '{Path.GetFileName(directory)}': {exception.Message}");
                }
            }
        }

        private static MapInfo ReadMap(string directory, HashSet<string> ids, HashSet<string> names)
        {
            MapJson map = Read<MapJson>(Path.Combine(directory, "map.json"));
            string wavesPath = Path.Combine(directory, "waves.json");
            List<CustomWave> waves = File.Exists(wavesPath) ? Read<List<CustomWave>>(wavesPath) : null;
            Validate(map, waves);

            string id = map.Id ?? "map:" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(map.MapName.Normalize().ToLowerInvariant())))[..32].ToLowerInvariant();
            if (ids.Contains(id)) throw new InvalidDataException("Duplicate map ID: " + id);

            string[] expected = ExpectedScenes(map);
            if (expected.Any(names.Contains)) throw new InvalidDataException("Duplicate or native map scene name.");

            string triggersPath = Path.Combine(directory, "triggers.json");
            TriggerSettings[] triggers = File.Exists(triggersPath) ? Read<TriggerSettings[]>(triggersPath) : Array.Empty<TriggerSettings>();
            TriggerSettings.ValidateAll(triggers);

            string bundle = Path.Combine(directory, "bundle");
            Core.LoadSceneBundle(bundle);
            string[] scenes = Core.ScenesFor(bundle);

            if (!expected.OrderBy(value => value, StringComparer.Ordinal).SequenceEqual(scenes.OrderBy(value => value, StringComparer.Ordinal)))
            {
                throw new InvalidDataException("Bundle scenes must match mapName and useDayAndNight exactly.");
            }

            if (scenes.Any(names.Contains)) throw new InvalidDataException("Scene bundle conflicts with another map.");

            return new MapInfo
            {
                Id = id,
                ContentHash = Hash(directory),
                Map = new LobbyMenu.AllMaps { mapName = map.MapName, useDayAndNight = map.UseDayAndNight, size = map.Size },
                Monsters = [map.CollectMonster, map.VersusMonster, map.SurvivalMonster],
                IsOutside = map.IsOutside,
                Version = map.Version,
                Dependencies = map.Dependencies,
                BundlePath = bundle,
                AssetsPath = directory,
                Waves = waves,
                Scenes = expected,
                Triggers = triggers
            };
        }
    }
}
