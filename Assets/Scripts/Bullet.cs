using UnityEngine;

namespace Game.Simple
{
    /// <summary>
    /// Bullet fired by the <see cref="Player"/>. Sits still while being charged, flies straight after
    /// <see cref="Launch"/>, and on hitting an obstacle infects every obstacle within
    /// <c>size * explosionFactor</c> of the impact.
    /// Hits are found with a sphere sweep along each frame's movement (not physics triggers), so the
    /// impact point is exact at any speed and matches what <see cref="LevelController"/>'s solver predicts.
    /// </summary>
    public sealed class Bullet : MonoBehaviour
    {
        [Tooltip("Bullet diameter in world units.")]
        [SerializeField, Min(0.01f)] private float size = 0.2f;

        [SerializeField, Min(0f)] private float speed = 12f;

        [Tooltip("Explosion radius = size * explosionFactor.")]
        [SerializeField, Min(0f)] private float explosionFactor = 1.2f;

        [Tooltip("A bullet that hits nothing is destroyed after this many seconds of flight.")]
        [SerializeField, Min(0.1f)] private float maxLifetime = 5f;

        // Shared, preallocated buffers: no garbage per frame. The level has ~40 obstacles, so 128 is plenty.
        private static readonly RaycastHit[] s_hits = new RaycastHit[64];
        private static readonly Collider[] s_overlaps = new Collider[128];

        private Vector3 _direction;
        private bool _launched;
        private bool _exploded;

        public float Size => size;
        public float ExplosionFactor => explosionFactor;
        public float MaxFlightDistance => speed * maxLifetime;

        private void Awake()
        {
            // The bullet never takes part in physics: hits come from the sweep in Update.
            var body = GetComponent<Rigidbody>();
            if (body != null)
            {
                body.isKinematic = true;
                body.useGravity = false;
            }
            foreach (var bulletCollider in GetComponentsInChildren<Collider>()) bulletCollider.enabled = false;

            ApplySize();
        }

        private void Update()
        {
            if (!_launched || _exploded) return;

            float step = speed * Time.deltaTime;
            if (TryFindHit(step, out float hitDistance))
            {
                transform.position += _direction * hitDistance;
                Explode();
                return;
            }
            transform.position += _direction * step;
        }

        /// <summary>
        /// Distance along the flight direction to the first obstacle the bullet touches within
        /// <paramref name="range"/>. An obstacle it already overlaps counts as distance 0.
        /// </summary>
        private bool TryFindHit(float range, out float hitDistance)
        {
            hitDistance = float.MaxValue;
            int count = Physics.SphereCastNonAlloc(transform.position, size * 0.5f, _direction, s_hits, range,
                Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);

            for (int i = 0; i < count; i++)
            {
                var hit = s_hits[i];
                if (hit.collider.GetComponentInParent<Obstacle>() == null) continue;
                if (hit.distance < hitDistance) hitDistance = hit.distance;
            }
            return hitDistance < float.MaxValue;
        }

        public void SetSize(float newSize)
        {
            size = Mathf.Max(0.01f, newSize);
            ApplySize();
        }

        public void Launch(Vector3 direction)
        {
            _direction = direction.normalized;
            _launched = true;
            Destroy(gameObject, maxLifetime);
        }

        /// <summary>Infects every obstacle within <c>size * explosionFactor</c> and removes the bullet.</summary>
        public void Explode()
        {
            if (_exploded) return;
            _exploded = true;

            float radius = size * explosionFactor;
            int count = Physics.OverlapSphereNonAlloc(transform.position, radius, s_overlaps,
                Physics.DefaultRaycastLayers, QueryTriggerInteraction.Collide);

            for (int i = 0; i < count; i++)
            {
                var obstacle = s_overlaps[i].GetComponentInParent<Obstacle>();
                if (obstacle != null) obstacle.Infect();
            }

            Destroy(gameObject);
        }

        private void ApplySize()
        {
            transform.localScale = Vector3.one * size;
        }

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = Color.red;
            Gizmos.DrawWireSphere(transform.position, size * explosionFactor);
        }
    }
}
