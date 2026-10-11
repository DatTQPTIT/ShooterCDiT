using System.Collections;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace LastVaccine
{
    [RequireComponent(typeof(CharacterController))]
    public sealed class FpsSurvivor : MonoBehaviour
    {
        const string DefaultReserveAmmoKey = "LastVaccine.DefaultReserveAmmo";

        [Header("References")]
        public Camera playerCamera;
        public Transform muzzle;
        public Light muzzleLight;
        public AudioSource audioSource;
        public AudioClip gunClip;
        public AudioClip hurtClip;
        public AudioClip deathClip;
        public GameObject hitEffectPrefab;
        [Header("Movement")]
        public float walkSpeed = 4.5f;
        public float sprintSpeed = 7f;
        public float mouseSensitivity = 0.12f;
        [Header("Survival")]
        public int maxHealth = 100;
        public float damageInvulnerability = .45f;
        [Header("Weapon")]
        public int damage = 20;
        public float fireRate = 10f;
        public int magazineCapacity = 30;
        public int startingReserveAmmo = 90;
        public int maxReserveAmmo = 240;
        public float reloadDuration = 2f;
        public float weaponRange = 100f;

        public int Health { get; private set; }
        public int MaxHealth => maxHealth;
        public int Magazine { get; private set; }
        public int ReserveAmmo { get; private set; }

        CharacterController controller;
        float pitch;
        float verticalVelocity;
        float nextShot;
        float invulnerableUntil;
        float muzzleUntil;
        bool reloading;

        void Awake()
        {
            controller = GetComponent<CharacterController>();
            fireRate = Mathf.Max(fireRate, 10f);
            Health = maxHealth;
            Magazine = magazineCapacity;
            startingReserveAmmo = Mathf.Clamp(
                Mathf.Max(startingReserveAmmo, PlayerPrefs.GetInt(DefaultReserveAmmoKey, startingReserveAmmo)),
                0,
                maxReserveAmmo);
            ReserveAmmo = startingReserveAmmo;
            ConfigureLowVisibility();
        }

        void ConfigureLowVisibility()
        {
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(.012f, .008f, .022f);
            RenderSettings.fog = true;
            RenderSettings.fogColor = new Color(.006f, .004f, .012f);
            RenderSettings.fogMode = FogMode.ExponentialSquared;
            RenderSettings.fogDensity = .018f;

            Light directional = GameObject.Find("Directional Light")?.GetComponent<Light>();
            if (directional != null)
            {
                directional.intensity = .035f;
                directional.color = new Color(.35f, .42f, .65f);
                directional.shadows = LightShadows.None;
            }

            if (playerCamera == null) return;
            Transform visibilityTransform = playerCamera.transform.Find("Player Visibility Light (4.5m)");
            if (visibilityTransform == null)
            {
                GameObject visibility = new GameObject("Player Visibility Light (4.5m)");
                visibility.transform.SetParent(playerCamera.transform, false);
                visibilityTransform = visibility.transform;
            }

            Light visibilityLight = visibilityTransform.GetComponent<Light>();
            if (visibilityLight == null) visibilityLight = visibilityTransform.gameObject.AddComponent<Light>();
            visibilityLight.type = LightType.Point;
            visibilityLight.range = 4.5f;
            visibilityLight.intensity = 2.8f;
            visibilityLight.color = new Color(.62f, .76f, 1f);
            visibilityLight.shadows = LightShadows.Soft;
            visibilityLight.enabled = true;
        }

        void Update()
        {
            LastVaccineGame game = LastVaccineGame.Instance;
            if (game == null || game.Phase == GamePhase.Paused || game.Phase == GamePhase.Victory || game.Phase == GamePhase.GameOver) return;
            ReadLook(out Vector2 look);
            transform.Rotate(Vector3.up, look.x * mouseSensitivity);
            pitch = Mathf.Clamp(pitch - look.y * mouseSensitivity, -82f, 82f);
            if (playerCamera != null) playerCamera.transform.localRotation = Quaternion.Euler(pitch, 0f, 0f);
            ReadMove(out Vector2 move, out bool sprint);
            Vector3 direction = (transform.right * move.x + transform.forward * move.y).normalized;
            if (controller.isGrounded && verticalVelocity < 0f) verticalVelocity = -2f;
            verticalVelocity += Physics.gravity.y * Time.deltaTime;
            controller.Move((direction * (sprint ? sprintSpeed : walkSpeed) + Vector3.up * verticalVelocity) * Time.deltaTime);
            if (muzzleLight != null && Time.time >= muzzleUntil) muzzleLight.enabled = false;
            if (ReloadPressed() && !reloading) StartCoroutine(Reload());
            if (FirePressed() && !reloading && Time.time >= nextShot) Fire();
        }

        void Fire()
        {
            if (Magazine <= 0) { if (ReserveAmmo > 0) StartCoroutine(Reload()); return; }
            Magazine--;
            nextShot = Time.time + 1f / Mathf.Max(.1f, fireRate);
            if (audioSource != null && gunClip != null) audioSource.PlayOneShot(gunClip);
            if (muzzleLight != null) { muzzleLight.enabled = true; muzzleUntil = Time.time + .045f; }
            Ray ray = new Ray(playerCamera.transform.position, playerCamera.transform.forward);
            if (Physics.Raycast(ray, out RaycastHit hit, weaponRange, ~0, QueryTriggerInteraction.Ignore))
            {
                ZombieAgent enemy = hit.collider.GetComponentInParent<ZombieAgent>();
                if (enemy != null)
                {
                    float multiplier = hit.point.y > enemy.transform.position.y + 1.2f ? 1.5f : 1f;
                    enemy.TakeDamage(Mathf.RoundToInt(damage * multiplier), hit.point);
                }
                if (hitEffectPrefab != null) Destroy(Instantiate(hitEffectPrefab, hit.point, Quaternion.LookRotation(hit.normal)), 2f);
            }
        }

        IEnumerator Reload()
        {
            if (Magazine >= magazineCapacity || ReserveAmmo <= 0) yield break;
            reloading = true;
            yield return new WaitForSeconds(reloadDuration);
            int amount = Mathf.Min(magazineCapacity - Magazine, ReserveAmmo);
            Magazine += amount;
            ReserveAmmo -= amount;
            reloading = false;
        }

        public void TakeDamage(int amount)
        {
            if (Health <= 0 || Time.time < invulnerableUntil) return;
            invulnerableUntil = Time.time + damageInvulnerability;
            Health = Mathf.Max(0, Health - amount);
            if (audioSource != null && hurtClip != null) audioSource.PlayOneShot(hurtClip);
            if (Health == 0)
            {
                if (audioSource != null && deathClip != null) audioSource.PlayOneShot(deathClip);
                LastVaccineGame.Instance?.NotifyPlayerDied();
            }
        }

        public void Heal(int amount) => Health = Mathf.Min(maxHealth, Health + Mathf.Max(0, amount));
        public void AddReserveAmmo(int amount) => ReserveAmmo = Mathf.Clamp(ReserveAmmo + Mathf.Max(0, amount), 0, maxReserveAmmo);

        public void UpgradeDefaultReserveAmmo(int amount)
        {
            int upgrade = Mathf.Max(0, amount);
            int savedDefault = Mathf.Clamp(PlayerPrefs.GetInt(DefaultReserveAmmoKey, startingReserveAmmo), 0, maxReserveAmmo);
            startingReserveAmmo = Mathf.Clamp(Mathf.Max(startingReserveAmmo, savedDefault) + upgrade, 0, maxReserveAmmo);
            PlayerPrefs.SetInt(DefaultReserveAmmoKey, startingReserveAmmo);
            PlayerPrefs.Save();
            AddReserveAmmo(upgrade);
        }

        static void ReadMove(out Vector2 move, out bool sprint)
        {
#if ENABLE_INPUT_SYSTEM
            Keyboard k = Keyboard.current;
            move = k == null ? Vector2.zero : new Vector2((k.dKey.isPressed ? 1 : 0) - (k.aKey.isPressed ? 1 : 0), (k.wKey.isPressed ? 1 : 0) - (k.sKey.isPressed ? 1 : 0));
            sprint = k != null && k.leftShiftKey.isPressed;
#else
            move = new Vector2(Input.GetAxisRaw("Horizontal"), Input.GetAxisRaw("Vertical")); sprint = Input.GetKey(KeyCode.LeftShift);
#endif
        }
        static void ReadLook(out Vector2 look)
        {
#if ENABLE_INPUT_SYSTEM
            look = Mouse.current == null ? Vector2.zero : Mouse.current.delta.ReadValue();
#else
            look = new Vector2(Input.GetAxis("Mouse X"), Input.GetAxis("Mouse Y"));
#endif
        }
        static bool FirePressed()
        {
#if ENABLE_INPUT_SYSTEM
            return Mouse.current != null && Mouse.current.leftButton.isPressed;
#else
            return Input.GetButton("Fire1");
#endif
        }
        static bool ReloadPressed()
        {
#if ENABLE_INPUT_SYSTEM
            return Keyboard.current != null && Keyboard.current.rKey.wasPressedThisFrame;
#else
            return Input.GetKeyDown(KeyCode.R);
#endif
        }
    }
}
