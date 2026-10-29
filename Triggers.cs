using Il2Cpp;
using Il2CppInterop.Runtime.Attributes;
using MelonLoader;
using UnityEngine;

namespace CustomMapsFramework
{
    [RegisterTypeInIl2Cpp]
    public class Trigger : MonoBehaviour
    {
        public Trigger(IntPtr pointer) : base(pointer) { }

        private sealed class Contact
        {
            internal PlayerDamage Player;
            internal readonly Dictionary<IntPtr, Collider> Colliders = new();
        }

        private readonly Dictionary<IntPtr, Contact> _contacts = new();
        public string Id = "";
        public int Priority;

        private static PlayerDamage Actor(Collider collider)
        {
            if (!collider) return null;

            PlayerDamage player = collider.GetComponentInParent<PlayerDamage>();
            return player && player.IsClientInitialized && player.IsOwner && player.gameObject.activeInHierarchy ? player : null;
        }

        public virtual void OnTriggerEnter(Collider collider) => Touch(collider, false);
        public virtual void OnTriggerStay(Collider collider) => Touch(collider, true);

        private void Touch(Collider collider, bool stay)
        {
            if (!isActiveAndEnabled) return;

            PlayerDamage player = Actor(collider);
            if (!player) return;

            if (!_contacts.TryGetValue(player.Pointer, out Contact contact))
            {
                _contacts[player.Pointer] = contact = new Contact { Player = player };
                contact.Colliders[collider.Pointer] = collider;
                Enter(player);
                MapTriggers.Notify(this, player, true);
            }
            else
            {
                contact.Colliders[collider.Pointer] = collider;
            }

            if (stay)
            {
                Stay(player);
            }
        }

        public virtual void OnTriggerExit(Collider collider)
        {
            if (!collider) return;

            foreach (var pair in _contacts.ToArray())
            {
                Contact contact = pair.Value;
                if (!contact.Colliders.Remove(collider.Pointer) || contact.Colliders.Count > 0) continue;

                _contacts.Remove(pair.Key);
                Leave(contact.Player);
                MapTriggers.Notify(this, contact.Player, false);
            }
        }

        public void Update()
        {
            foreach (var pair in _contacts.ToArray())
            {
                Contact contact = pair.Value;
                foreach (var item in contact.Colliders.ToArray())
                {
                    Collider collider = item.Value;
                    if (!collider || !collider.enabled || !collider.gameObject.activeInHierarchy)
                    {
                        contact.Colliders.Remove(item.Key);
                    }
                }

                if (contact.Player && contact.Player.IsClientInitialized && contact.Player.IsOwner &&
                    contact.Player.gameObject.activeInHierarchy && contact.Colliders.Count > 0) continue;

                _contacts.Remove(pair.Key);
                Leave(contact.Player);
                MapTriggers.Notify(this, contact.Player, false);
            }
        }

        public void OnDisable() => ClearContacts();
        public void OnDestroy() => ClearContacts();

        [HideFromIl2Cpp]
        internal void ClearContacts()
        {
            foreach (Contact contact in _contacts.Values.ToArray())
            {
                Leave(contact.Player);
                MapTriggers.Notify(this, contact.Player, false);
            }

            _contacts.Clear();
        }

        protected virtual void Enter(PlayerDamage player) { }
        protected virtual void Stay(PlayerDamage player) { }
        protected virtual void Leave(PlayerDamage player) { }
    }

    [RegisterTypeInIl2Cpp]
    public class DamageZone : Trigger
    {
        public DamageZone(IntPtr pointer) : base(pointer) { }

        private readonly Dictionary<IntPtr, float> _next = new();
        public bool IsDamage = true;
        public float Damage = 5f;
        public float CoolDown = 1f;

        protected override void Enter(PlayerDamage player) => Hit(player);
        protected override void Stay(PlayerDamage player) => Hit(player);
        protected override void Leave(PlayerDamage player)
        {
            if (player is not null)
            {
                _next.Remove(player.Pointer);
            }
        }

        private void Hit(PlayerDamage player)
        {
            if (!IsDamage || !player || !float.IsFinite(Damage) || Damage <= 0f ||
                _next.TryGetValue(player.Pointer, out float next) && Time.time < next) return;

            _next[player.Pointer] = Time.time + (float.IsFinite(CoolDown) ? Math.Max(0.02f, CoolDown) : 1f);
            player.E10030(Damage);
        }
    }

    [RegisterTypeInIl2Cpp]
    public class KillTrigger : DamageZone
    {
        public KillTrigger(IntPtr pointer) : base(pointer) { }
    }

    [RegisterTypeInIl2Cpp]
    public class WaterZone : DamageZone
    {
        public WaterZone(IntPtr pointer) : base(pointer) { }

        public float Gravity = 4f;
        public float MaxFallSpeed = 10f;
        public bool Blur = true;
        public Color WaterColor = new(0.2096f, 0.8476f, 1f, 1f);

        protected override void Enter(PlayerDamage player)
        {
            base.Enter(player);
            TriggerEffects.EnterWater(this, player);
        }

        protected override void Leave(PlayerDamage player)
        {
            base.Leave(player);
            TriggerEffects.LeaveWater(this);
        }
    }

    [RegisterTypeInIl2Cpp]
    public class AmbientTriggerEnter : Trigger
    {
        public AmbientTriggerEnter(IntPtr pointer) : base(pointer) { }

        public Color DefaultAmbientColor, DefaultFogColor, NewAmbientColor, NewFogColor;
        public FogMode DefaultFogMode, NewFogMode = FogMode.ExponentialSquared;
        public float DefaultFogDensity, NewFogDensity;
        public bool Fog = true;
        public float FogStart, FogEnd = 300f, TransitionSpeed = 4f;

        protected override void Enter(PlayerDamage player) => TriggerEffects.EnterAmbient(this);
        protected override void Leave(PlayerDamage player) => TriggerEffects.LeaveAmbient(this);
    }

    [RegisterTypeInIl2Cpp]
    public class AmbientController : MonoBehaviour
    {
        public AmbientController(IntPtr pointer) : base(pointer) { }

        public static AmbientController Instance { get; set; }
        public Color AmbientColor, FogColor;
        public float FogDensity;
        public float TransitionSpeed = 4f;
        public FogMode FogMode { set => RenderSettings.fogMode = value; }

        public void Start()
        {
            Instance = this;
            FogColor = RenderSettings.fogColor;
            AmbientColor = RenderSettings.ambientLight;
            FogDensity = RenderSettings.fogDensity;
        }

        public void Update()
        {
            float blend = 1f - Mathf.Exp(-Math.Max(0.01f, TransitionSpeed) * Time.deltaTime);
            RenderSettings.fogColor = Color.Lerp(RenderSettings.fogColor, FogColor, blend);
            RenderSettings.ambientLight = Color.Lerp(RenderSettings.ambientLight, AmbientColor, blend);
            RenderSettings.fogDensity = Mathf.Lerp(RenderSettings.fogDensity, FogDensity, blend);
        }

        public void OnDestroy()
        {
            if (Instance == this)
            {
                Instance = null;
            }
        }
    }
}
