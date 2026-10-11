using System.Collections;
using UnityEngine;
using UnityEngine.AI;

namespace LastVaccine
{
    [RequireComponent(typeof(CapsuleCollider), typeof(AudioSource))]
    public sealed class ZombieAgent : MonoBehaviour
    {
        public ZombieKind kind;
        public int maxHealth = 50;
        public float moveSpeed = 5.5f;
        public int attackDamage = 10;
        public float attackCooldown = 1f;
        public float attackRange = 1.65f;
        public AudioClip hurtClip;
        public AudioClip deathClip;
        int health;
        float nextAttack;
        float wobbleSeed;
        bool dead;
        FpsSurvivor target;
        LastVaccineGame game;
        NavMeshAgent agent;
        AudioSource source;

        void Awake()
        {
            health = maxHealth;
            wobbleSeed = Random.value * 100f;
            agent = GetComponent<NavMeshAgent>();
            source = GetComponent<AudioSource>();
            source.spatialBlend = 1f;
            source.minDistance = 2f;
            source.maxDistance = 28f;
            source.rolloffMode = AudioRolloffMode.Logarithmic;
            if (agent != null) { agent.speed = moveSpeed; agent.stoppingDistance = attackRange * .8f; agent.angularSpeed = 360f; }
        }

        public void Initialize(LastVaccineGame owner, FpsSurvivor player)
        {
            game = owner; target = player; health = maxHealth;
            if (agent != null && agent.isActiveAndEnabled && NavMesh.SamplePosition(transform.position, out NavMeshHit hit, 5f, NavMesh.AllAreas)) agent.Warp(hit.position);
        }

        void Update()
        {
            if (dead || target == null || game == null || !game.EnemiesCanAttack) return;
            Vector3 delta = target.transform.position - transform.position; delta.y = 0f;
            float distance = delta.magnitude;
            bool navigating = agent != null && agent.isActiveAndEnabled && agent.isOnNavMesh;
            if (distance > attackRange)
            {
                if (navigating) agent.SetDestination(target.transform.position);
                else
                {
                    Vector3 direction = delta.normalized;
                    if (!Physics.SphereCast(transform.position + Vector3.up, .35f, direction, out _, moveSpeed * Time.deltaTime + .2f, ~0, QueryTriggerInteraction.Ignore)) transform.position += direction * moveSpeed * Time.deltaTime;
                    if (direction.sqrMagnitude > .01f) transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(direction), Time.deltaTime * 8f);
                }
                float bob = Mathf.Sin((Time.time + wobbleSeed) * (kind == ZombieKind.Bunny ? 10f : 6f)) * .03f;
                transform.localScale = Vector3.one * (1f + bob);
            }
            else if (Time.time >= nextAttack)
            {
                nextAttack = Time.time + attackCooldown;
                target.TakeDamage(attackDamage);
            }
        }

        public void TakeDamage(int amount, Vector3 point)
        {
            if (dead) return;
            health -= amount;
            if (source != null && hurtClip != null) source.PlayOneShot(hurtClip);
            StartCoroutine(HitFlash());
            if (health <= 0) Die();
        }

        IEnumerator HitFlash()
        {
            Renderer[] renderers = GetComponentsInChildren<Renderer>();
            foreach (Renderer r in renderers) r.material.color *= 1.6f;
            yield return new WaitForSeconds(.08f);
            foreach (Renderer r in renderers) if (r != null) r.material.color /= 1.6f;
        }

        void Die()
        {
            dead = true;
            if (agent != null) agent.enabled = false;
            foreach (Collider c in GetComponents<Collider>()) c.enabled = false;
            if (source != null && deathClip != null) source.PlayOneShot(deathClip);
            StartCoroutine(DeathRoutine());
        }

        IEnumerator DeathRoutine()
        {
            float t = 0f;
            Quaternion start = transform.rotation;
            Quaternion end = start * Quaternion.Euler(0f, 0f, 90f);
            while (t < 1f) { t += Time.deltaTime * 3f; transform.rotation = Quaternion.Slerp(start, end, t); yield return null; }
            yield return new WaitForSeconds(.7f);
            Destroy(gameObject);
        }
    }
}
