using Il2Cpp;
using MelonLoader;
using System.Globalization;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace CustomMapsFramework
{
    public static class MapTriggers
    {
        private static int _scene = -1;
        private static readonly List<Trigger> Active = new();
        private static readonly List<GameObject> Created = new();
        public static event Action<Trigger, PlayerDamage> Entered;
        public static event Action<Trigger, PlayerDamage> Exited;
        public static IReadOnlyList<Trigger> Items => Active.AsReadOnly();

        internal static void Notify(Trigger trigger, PlayerDamage player, bool enter)
        {
            Delegate[] handlers = (enter ? Entered : Exited)?.GetInvocationList();
            foreach (Delegate handler in handlers ?? Array.Empty<Delegate>())
            {
                try
                {
                    ((Action<Trigger, PlayerDamage>)handler)(trigger, player);
                }
                catch (Exception exception)
                {
                    MelonLogger.Error("CustomMaps trigger callback: " + exception.Message);
                }
            }
        }

        private static Transform Resolve(Scene scene, string path)
        {
            string[] parts = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0) return null;

            Transform current = scene.GetRootGameObjects().FirstOrDefault(root => root.name == parts[0])?.transform;
            for (int index = 1; current && index < parts.Length; index++)
            {
                string name = parts[index];
                current = Enumerable.Range(0, current.childCount).Select(current.GetChild).FirstOrDefault(child => child.name == name);
            }

            return current;
        }

        internal static void Setup(Scene scene, MapInfo map)
        {
            if (_scene == scene.handle) return;

            Clear();
            _scene = scene.handle;

            HashSet<IntPtr> configured = new();
            AddConfiguredTriggers(scene, map, configured);
            AddLegacyTriggers(scene, configured);
            MelonDebug.Msg("CustomMaps: Configured " + Active.Count + " trigger volumes.");
        }

        private static void AddConfiguredTriggers(Scene scene, MapInfo map, HashSet<IntPtr> configured)
        {
            foreach (TriggerSettings settings in map.Triggers ?? Array.Empty<TriggerSettings>())
            {
                if (settings.Scene.Length > 0 && settings.Scene != scene.name) continue;

                try
                {
                    GameObject root = TriggerRoot(scene, settings);
                    Collider[] colliders = root.GetComponentsInChildren<Collider>(true).ToArray();
                    if (colliders.Length == 0) throw new InvalidDataException("Trigger object requires a collider: " + settings.Id);

                    foreach (Collider collider in colliders)
                    {
                        Add(collider, settings, configured);
                    }
                }
                catch (Exception exception)
                {
                    MelonLogger.Warning("CustomMaps: Cannot configure trigger '" + settings.Id + "': " + exception.Message);
                }
            }
        }

        private static GameObject TriggerRoot(Scene scene, TriggerSettings settings)
        {
            if (settings.Object.Length > 0)
            {
                Transform target = Resolve(scene, settings.Object);
                if (!target) throw new InvalidDataException("Object not found: " + settings.Object);

                return target.gameObject;
            }

            GameObject root = new("MapTrigger_" + settings.Id);
            SceneManager.MoveGameObjectToScene(root, scene);
            root.transform.position = Vector(settings.Position);
            root.transform.eulerAngles = Vector(settings.Rotation);
            root.AddComponent<BoxCollider>().size = Vector(settings.Size);
            Created.Add(root);
            return root;
        }

        private static void AddLegacyTriggers(Scene scene, HashSet<IntPtr> configured)
        {
            foreach (Transform group in MapEnvironment.Components<Transform>(scene).Where(node =>
                node.name.Equals("Killzones", StringComparison.OrdinalIgnoreCase) ||
                node.name.Equals("Damage zones", StringComparison.OrdinalIgnoreCase) ||
                node.name.Equals("Water zones", StringComparison.OrdinalIgnoreCase)))
            {
                TriggerKind kind = group.name.Equals("Killzones", StringComparison.OrdinalIgnoreCase) ? TriggerKind.Kill :
                    group.name.Equals("Water zones", StringComparison.OrdinalIgnoreCase) ? TriggerKind.Water : TriggerKind.Damage;

                foreach (Collider collider in group.GetComponentsInChildren<Collider>(true))
                {
                    if (configured.Contains(collider.gameObject.Pointer)) continue;

                    try
                    {
                        Add(collider, LegacySettings(collider, kind), configured);
                    }
                    catch (Exception exception)
                    {
                        MelonLogger.Warning("CustomMaps legacy trigger: " + exception.Message);
                    }
                }
            }
        }

        private static TriggerSettings LegacySettings(Collider collider, TriggerKind kind)
        {
            string[] values = collider.name.Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
            TriggerSettings settings = new() { Id = "legacy:" + collider.GetInstanceID(), Type = kind.ToString() };

            if (kind == TriggerKind.Water)
            {
                settings.Gravity = Number(values, 0, 4f, true);
                settings.Damage = Number(values, 1, 5f);
                settings.CoolDown = Number(values, 2, 1f);
            }
            else if (kind == TriggerKind.Damage)
            {
                settings.Damage = Number(values, 0, 5f);
                settings.CoolDown = Number(values, 1, 1f);
            }

            return settings;
        }

        internal static float Number(string[] parts, int index, float fallback, bool zero = false) =>
            index < parts.Length && float.TryParse(parts[index], NumberStyles.Float, CultureInfo.InvariantCulture, out float value) &&
            float.IsFinite(value) && (zero ? value >= 0 : value > 0) ? value : fallback;

        private static Vector3 Vector(float[] value) => new(value[0], value[1], value[2]);

        private static void Add(Collider collider, TriggerSettings settings, HashSet<IntPtr> configured)
        {
            GameObject root = collider.gameObject;
            if (!configured.Add(root.Pointer)) return;

            MeshCollider mesh = collider.TryCast<MeshCollider>();
            if (mesh && !mesh.convex) throw new InvalidDataException("Use a convex MeshCollider or primitive collider for " + settings.Id);
            if (root.GetComponents<Collider>().Length != 1) throw new InvalidDataException("Use one collider per trigger object: " + settings.Id);
            if (root.GetComponent<KillZone>()) return;

            collider.isTrigger = true;
            Trigger trigger = root.GetComponent<Trigger>();
            if (!trigger)
            {
                trigger = settings.Kind switch
                {
                    TriggerKind.Damage => root.AddComponent<DamageZone>(),
                    TriggerKind.Kill => root.AddComponent<KillTrigger>(),
                    TriggerKind.Water => root.AddComponent<WaterZone>(),
                    TriggerKind.Ambient => root.AddComponent<AmbientTriggerEnter>(),
                    _ => root.AddComponent<Trigger>()
                };
            }

            Configure(trigger, settings);
            Active.Add(trigger);
        }

        private static void Configure(Trigger trigger, TriggerSettings settings)
        {
            trigger.Id = settings.Id;
            trigger.Priority = settings.Priority;

            if (trigger is DamageZone damage)
            {
                damage.Damage = settings.Kind == TriggerKind.Kill ? 1000000f : settings.Damage;
                damage.IsDamage = damage.Damage > 0;
                damage.CoolDown = settings.CoolDown;
            }

            if (trigger is WaterZone water)
            {
                water.Gravity = settings.Gravity;
                water.MaxFallSpeed = settings.MaxFallSpeed;
                water.Blur = settings.Blur;
                ColorUtility.TryParseHtmlString(settings.WaterColor, out water.WaterColor);
            }

            if (trigger is AmbientTriggerEnter ambient)
            {
                ColorUtility.TryParseHtmlString(settings.AmbientColor, out ambient.NewAmbientColor);
                ColorUtility.TryParseHtmlString(settings.FogColor, out ambient.NewFogColor);
                ambient.NewFogMode = Enum.Parse<FogMode>(settings.FogMode);
                ambient.NewFogDensity = settings.FogDensity;
                ambient.Fog = settings.Fog;
                ambient.FogStart = settings.FogStart;
                ambient.FogEnd = settings.FogEnd;
                ambient.TransitionSpeed = settings.TransitionSpeed;
            }
        }

        internal static void Update() => TriggerEffects.Update();

        internal static void Clear()
        {
            foreach (Trigger trigger in Active)
            {
                if (trigger)
                {
                    trigger.ClearContacts();
                }
            }

            TriggerEffects.Clear();

            foreach (GameObject root in Created)
            {
                if (root)
                {
                    UnityEngine.Object.Destroy(root);
                }
            }

            Created.Clear();
            Active.Clear();
            _scene = -1;
        }
    }
}
