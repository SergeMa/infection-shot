using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Game.Simple
{
    /// <summary>
    /// Player ball. Everything it does is aimed at <see cref="target"/> (the door).
    /// Tap spawns a bullet in front of it and gives it part of the player's size;
    /// while the finger stays down the bullet keeps growing and the player keeps shrinking.
    /// On release the bullet flies toward the target. When no obstacle is within
    /// <c>bulletSpawnDistance * 2</c> ahead, the player hops toward the target until one appears.
    /// The ball always rests on the ground, so it sinks as it shrinks.
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    public sealed class Player : MonoBehaviour
    {
        [Header("Size")]
        [Tooltip("Player diameter in world units. Applied to the transform scale in Start.")]
        [SerializeField, Min(0.01f)] private float size = 2f;

        [Tooltip("Hard floor: the player physically cannot shrink below this size. " +
                 "The lose threshold lives in GameController and should be above it.")]
        [SerializeField, Min(0.01f)] private float minSize = 0.1f;

        [Tooltip("World Y of the ground the ball rests on.")]
        [SerializeField] private float groundHeight;

        [Header("Target")]
        [Tooltip("Where the player shoots and moves. Usually the door.")]
        [SerializeField] private Transform target;

        [Header("Shooting")]
        [SerializeField] private Bullet bulletPrefab;

        [Tooltip("Gap between the player's surface and the bullet's surface.")]
        [SerializeField, Min(0f)] private float bulletSpawnDistance = 0.5f;

        [Tooltip("Size a bullet gets on a tap. This is also the smallest possible shot.")]
        [SerializeField, Min(0.01f)] private float baseBulletSize = 0.2f;

        [Tooltip("Size moved from the player to the bullet per second while the finger is held.")]
        [SerializeField, Min(0f)] private float chargeRate = 0.6f;

        [Header("Hops")]
        [Tooltip("Horizontal speed of a hop.")]
        [SerializeField, Min(0.01f)] private float moveSpeed = 3f;

        [Tooltip("Longest single hop. Longer free stretches are crossed in several hops.")]
        [SerializeField, Min(0.05f)] private float hopLength = 1.2f;

        [Tooltip("Hop height as a fraction of its length.")]
        [SerializeField, Min(0f)] private float hopHeightFactor = 0.35f;

        [Tooltip("Free stretches shorter than this are not worth a hop.")]
        [SerializeField, Min(0.001f)] private float minHopLength = 0.05f;

        // Shared, preallocated buffers: no garbage per frame.
        private static readonly RaycastHit[] s_hits = new RaycastHit[64];
        private static readonly Predicate<Bullet> s_isDestroyed = bullet => bullet == null;

        private readonly List<Bullet> _bulletsInFlight = new List<Bullet>();
        private Bullet _chargingBullet;
        private bool _stopped;

        private bool _hopping;
        private Vector3 _hopFrom;
        private Vector3 _hopTo;
        private float _hopHeight;
        private float _hopDuration;
        private float _hopTime;

        private bool _blockedByHealthyObstacle;

        /// <summary>A bullet left the player. <see cref="Size"/> is already the size after the shot.</summary>
        public event Action<Bullet> BulletLaunched;

        /// <summary>The player's size changed (start, level bonus, shooting). Passes the new size.</summary>
        public event Action<float> SizeChanged;

        public float Size => size;
        public float MinSize => minSize;
        public float GroundHeight => groundHeight;
        public float BaseBulletSize => baseBulletSize;
        public float BulletSpawnDistance => bulletSpawnDistance;
        public Bullet BulletPrefab => bulletPrefab;
        public bool IsCharging => _chargingBullet != null;
        public bool IsHopping => _hopping;

        public bool HasBulletsInFlight
        {
            get
            {
                _bulletsInFlight.RemoveAll(s_isDestroyed); // cached delegate: a method group would allocate
                return _bulletsInFlight.Count > 0;
            }
        }

        /// <summary>
        /// Nothing will change unless the player shoots: it stands still, no bullet is flying,
        /// and an obstacle that is not infected blocks the way.
        /// </summary>
        public bool IsWaitingForShot =>
            !_stopped && !IsCharging && !_hopping && _blockedByHealthyObstacle && !HasBulletsInFlight;

        /// <summary>Flat direction to the target (or the transform's forward if there is none).</summary>
        public Vector3 Forward
        {
            get
            {
                if (target == null) return transform.forward;

                Vector3 direction = target.position - transform.position;
                direction.y = 0f;
                return direction.sqrMagnitude > 0.0001f ? direction.normalized : transform.forward;
            }
        }

        private float GroundedY => groundHeight + size * 0.5f;

        private void Awake()
        {
            var body = GetComponent<Rigidbody>();
            body.isKinematic = true; // moved by code, but still needs a body to receive door triggers
            body.useGravity = false;
        }

        private void Start()
        {
            ApplySize();
        }

        private void Update()
        {
            if (_stopped) return;

            if (_hopping)
            {
                UpdateHop();
                return;
            }

            Shoot();
            if (!_stopped && !IsCharging) TryStartHop();
        }

        /// <summary>Grows (or shrinks, with a negative amount) the player.</summary>
        public void AddSize(float amount)
        {
            SetSize(size + amount);
        }

        /// <summary>Sets the player's size. Used by the level controller to give the solved start size.</summary>
        public void SetSize(float newSize)
        {
            size = Mathf.Max(minSize, newSize);
            ApplySize();
        }

        /// <summary>Freezes the player (game over). A bullet still being charged is discarded.</summary>
        public void Stop()
        {
            if (IsCharging)
            {
                Destroy(_chargingBullet.gameObject);
                _chargingBullet = null;
            }
            _stopped = true;
        }

        /// <summary>Applies the size to the scale and keeps the ball on the ground (unless mid-hop).</summary>
        private void ApplySize()
        {
            transform.localScale = Vector3.one * size;
            if (!_hopping)
            {
                var position = transform.position;
                position.y = GroundedY;
                transform.position = position;
            }
            SizeChanged?.Invoke(size);
        }

        // ---- Shooting ----------------------------------------------------------------------

        private void Shoot()
        {
            bool pressed = IsPointerPressed();

            if (!IsCharging)
            {
                if (pressed && WasPointerPressedThisFrame()) BeginShot();
                return;
            }

            if (pressed)
            {
                Charge(chargeRate * Time.deltaTime);
            }
            else
            {
                Release();
            }
        }

        private void BeginShot()
        {
            if (bulletPrefab == null || size - baseBulletSize < minSize) return;

            _chargingBullet = Instantiate(bulletPrefab);
            _chargingBullet.SetSize(baseBulletSize);
            size -= baseBulletSize;
            ApplySize(); // listeners may end the game here (and Stop() discards the bullet)
            if (IsCharging) PlaceChargingBullet();
        }

        private void Charge(float amount)
        {
            amount = Mathf.Min(amount, size - minSize);
            if (amount <= 0f) return;

            size -= amount;
            _chargingBullet.SetSize(_chargingBullet.Size + amount);
            ApplySize(); // listeners may end the game here (and Stop() discards the bullet)
            if (IsCharging) PlaceChargingBullet();
        }

        private void Release()
        {
            var bullet = _chargingBullet;
            _chargingBullet = null;
            bullet.Launch(Forward);
            _bulletsInFlight.Add(bullet);
            BulletLaunched?.Invoke(bullet);
        }

        /// <summary>Keeps the growing bullet in front of the player, its surface <c>bulletSpawnDistance</c> away.</summary>
        private void PlaceChargingBullet()
        {
            float offset = size * 0.5f + bulletSpawnDistance + _chargingBullet.Size * 0.5f;
            _chargingBullet.transform.position = transform.position + Forward * offset;
        }

        private static bool IsPointerPressed()
        {
            var touch = Touchscreen.current;
            if (touch != null && touch.primaryTouch.press.isPressed) return true;

            var mouse = Mouse.current;
            return mouse != null && mouse.leftButton.isPressed;
        }

        private static bool WasPointerPressedThisFrame()
        {
            var touch = Touchscreen.current;
            if (touch != null && touch.primaryTouch.press.wasPressedThisFrame) return true;

            var mouse = Mouse.current;
            return mouse != null && mouse.leftButton.wasPressedThisFrame;
        }

        // ---- Hops --------------------------------------------------------------------------

        /// <summary>
        /// Starts a hop toward the target if there is room: the player never lands closer than
        /// <c>bulletSpawnDistance * 2</c> to an obstacle and never hops further than <see cref="hopLength"/>.
        /// </summary>
        private void TryStartHop()
        {
            Vector3 forward = Forward;
            float lookAhead = bulletSpawnDistance * 2f;

            ScanAhead(forward, lookAhead + hopLength, out float nearest, out float nearestHealthy);
            _blockedByHealthyObstacle = nearestHealthy - lookAhead < minHopLength;

            float distance = Mathf.Min(hopLength, nearest - lookAhead);
            if (target != null)
            {
                Vector3 toTarget = target.position - transform.position;
                toTarget.y = 0f;
                distance = Mathf.Min(distance, toTarget.magnitude);
            }
            if (distance < minHopLength) return;

            _hopping = true;
            _hopFrom = transform.position;
            _hopTo = _hopFrom + forward * distance;
            _hopTo.y = GroundedY;
            _hopHeight = distance * hopHeightFactor;
            _hopDuration = distance / moveSpeed;
            _hopTime = 0f;
        }

        private void UpdateHop()
        {
            _hopTime += Time.deltaTime;
            float t = Mathf.Clamp01(_hopTime / _hopDuration);

            Vector3 position = Vector3.Lerp(_hopFrom, _hopTo, t);
            position.y += 4f * _hopHeight * t * (1f - t); // parabola: 0 at both ends, _hopHeight in the middle
            transform.position = position;

            if (t >= 1f) _hopping = false;
        }

        /// <summary>
        /// Distances from the player's surface to the first obstacle ahead and to the first one that is
        /// not infected. Each is <paramref name="range"/> if there is none.
        /// Infected obstacles still block movement until they burst, but they do not need a shot.
        /// </summary>
        private void ScanAhead(Vector3 direction, float range, out float nearest, out float nearestHealthy)
        {
            nearest = range;
            nearestHealthy = range;

            int count = Physics.SphereCastNonAlloc(transform.position, size * 0.5f, direction, s_hits, range,
                Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);

            for (int i = 0; i < count; i++)
            {
                var hit = s_hits[i];
                var obstacle = hit.collider.GetComponentInParent<Obstacle>();
                if (obstacle == null) continue;

                // distance == 0 means the obstacle already overlaps the player: treat it as blocking.
                if (hit.distance < nearest) nearest = hit.distance;
                if (!obstacle.IsInfected && hit.distance < nearestHealthy) nearestHealthy = hit.distance;
            }
        }
    }
}
