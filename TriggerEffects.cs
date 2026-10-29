using Il2Cpp;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace CustomMapsFramework
{
    internal static class TriggerEffects
    {
        private sealed record Water(WaterZone Zone, long Order);

        private sealed record Ambient(AmbientTriggerEnter Zone, long Order);

        private sealed class Atmosphere
        {
            internal readonly int Scene;
            internal readonly Color Ambient, FogColor;
            internal readonly bool Fog;
            internal readonly FogMode Mode;
            internal readonly float Density, Start, End;

            internal Atmosphere(int scene, Color ambient, Color fogColor, bool fog, FogMode mode, float density, float start, float end)
            {
                Scene = scene;
                Ambient = ambient;
                FogColor = fogColor;
                Fog = fog;
                Mode = mode;
                Density = density;
                Start = start;
                End = end;
            }

            internal static Atmosphere Read() => new(SceneManager.GetActiveScene().handle, RenderSettings.ambientLight,
                RenderSettings.fogColor, RenderSettings.fog, RenderSettings.fogMode, RenderSettings.fogDensity,
                RenderSettings.fogStartDistance, RenderSettings.fogEndDistance);

            internal void Apply(float blend)
            {
                RenderSettings.ambientLight = Color.Lerp(RenderSettings.ambientLight, Ambient, blend);
                RenderSettings.fogColor = Color.Lerp(RenderSettings.fogColor, FogColor, blend);
                RenderSettings.fogDensity = Mathf.Lerp(RenderSettings.fogDensity, Density, blend);
                RenderSettings.fogStartDistance = Mathf.Lerp(RenderSettings.fogStartDistance, Start, blend);
                RenderSettings.fogEndDistance = Mathf.Lerp(RenderSettings.fogEndDistance, End, blend);
                RenderSettings.fog = Fog;
                RenderSettings.fogMode = Mode;
            }
        }

        private static readonly Dictionary<IntPtr, Water> Waters = new();
        private static readonly Dictionary<IntPtr, Ambient> Ambients = new();
        private static long _order;
        private static FPScontroller _player;
        private static float _gravity, _fallSpeed, _fallLimit;
        private static Camera _camera;
        private static CameraFilterPack_Blur_Blurry _blur;
        private static CameraFilterPack_Color_RGB _color;
        private static Atmosphere _default;
        private static float _ambientSpeed = 4f;
        private static float _restoreUntil;

        internal static void EnterWater(WaterZone zone, PlayerDamage player)
        {
            FPScontroller controller = player.GetComponentInParent<FPScontroller>();
            if (!controller)
            {
                controller = player.GetComponentInChildren<FPScontroller>(true);
            }

            if (!controller || controller.HKCDMBALAAK == null || controller.LGIGJCDJMNO == null) return;

            if (_player && _player.Pointer != controller.Pointer)
            {
                ClearWater();
            }

            if (Waters.Count == 0)
            {
                _player = controller;
                _gravity = controller.HKCDMBALAAK.gravity;
                _fallSpeed = controller.HKCDMBALAAK.maxFallSpeed;
                _fallLimit = controller.LGIGJCDJMNO.fallLimit;
            }

            Waters[zone.Pointer] = new(zone, ++_order);
            UpdateWater();
        }

        internal static void LeaveWater(WaterZone zone)
        {
            Waters.Remove(zone.Pointer);
            UpdateWater();
        }

        private static void ClearCamera()
        {
            if (_blur) UnityEngine.Object.Destroy(_blur);
            if (_color) UnityEngine.Object.Destroy(_color);

            _blur = null;
            _color = null;
            _camera = null;
        }

        private static void ClearWater()
        {
            if (_player && _player.HKCDMBALAAK != null && _player.LGIGJCDJMNO != null)
            {
                _player.HKCDMBALAAK.gravity = _gravity;
                _player.HKCDMBALAAK.maxFallSpeed = _fallSpeed;
                _player.LGIGJCDJMNO.fallLimit = _fallLimit;
            }

            Waters.Clear();
            _player = null;
            ClearCamera();
        }

        private static void UpdateWater()
        {
            foreach (var pair in Waters.ToArray())
            {
                if (!pair.Value.Zone || !pair.Value.Zone.isActiveAndEnabled)
                {
                    Waters.Remove(pair.Key);
                }
            }

            Water water = Waters.Values.OrderByDescending(value => value.Zone.Priority).ThenByDescending(value => value.Order).FirstOrDefault();
            if (water == null || !_player)
            {
                ClearWater();
                return;
            }

            _player.HKCDMBALAAK.gravity = water.Zone.Gravity;
            _player.HKCDMBALAAK.maxFallSpeed = water.Zone.MaxFallSpeed;
            _player.LGIGJCDJMNO.fallLimit = float.MaxValue;
            ApplyWaterCamera(water.Zone);
        }

        private static void ApplyWaterCamera(WaterZone zone)
        {
            Camera camera = Camera.main;
            if (camera != _camera)
            {
                ClearCamera();
                _camera = camera;
            }

            if (!_camera) return;

            if (zone.Blur && !_blur)
            {
                _blur = _camera.gameObject.AddComponent<CameraFilterPack_Blur_Blurry>();
            }
            else if (!zone.Blur && _blur)
            {
                UnityEngine.Object.Destroy(_blur);
                _blur = null;
            }

            if (!_color)
            {
                _color = _camera.gameObject.AddComponent<CameraFilterPack_Color_RGB>();
            }

            _color.EDGGOPGCNLE = zone.WaterColor;
        }

        internal static void EnterAmbient(AmbientTriggerEnter zone)
        {
            _default ??= Atmosphere.Read();
            Ambients[zone.Pointer] = new(zone, ++_order);
        }

        internal static void LeaveAmbient(AmbientTriggerEnter zone)
        {
            Ambients.Remove(zone.Pointer);
            if (Ambients.Count == 0)
            {
                _restoreUntil = Time.unscaledTime + 3f;
            }
        }

        private static void UpdateAmbient()
        {
            if (_default == null) return;

            foreach (var pair in Ambients.ToArray())
            {
                if (!pair.Value.Zone || !pair.Value.Zone.isActiveAndEnabled)
                {
                    Ambients.Remove(pair.Key);
                }
            }

            Ambient active = Ambients.Values.OrderByDescending(value => value.Zone.Priority).ThenByDescending(value => value.Order).FirstOrDefault();
            Atmosphere target = _default;

            if (active != null)
            {
                AmbientTriggerEnter zone = active.Zone;
                _ambientSpeed = zone.TransitionSpeed;
                target = new(_default.Scene, zone.NewAmbientColor, zone.NewFogColor, zone.Fog, zone.NewFogMode, zone.NewFogDensity, zone.FogStart, zone.FogEnd);
                _restoreUntil = Time.unscaledTime + 3f;
            }

            float blend = 1f - Mathf.Exp(-Math.Max(0.01f, _ambientSpeed) * Time.deltaTime);
            target.Apply(blend);

            if (active == null && Time.unscaledTime >= _restoreUntil)
            {
                _default.Apply(1f);
                _default = null;
            }
        }

        internal static void Update()
        {
            UpdateWater();
            UpdateAmbient();
        }

        internal static void Clear()
        {
            ClearWater();
            Ambients.Clear();

            if (_default?.Scene == SceneManager.GetActiveScene().handle)
            {
                _default.Apply(1f);
            }

            _default = null;
        }
    }
}
