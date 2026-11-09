using System;
using System.Collections.Generic;
using UnityEngine;
using Random = UnityEngine.Random;

namespace Game.Simple
{
    /// <summary>
    /// Builds the level and gives the player a start size that is guaranteed to be enough with a reserve.
    /// <list type="number">
    /// <item>Picks random obstacle positions in a rectangle centred on this object (on its XZ plane),
    /// away from the player and the door and apart from each other.</item>
    /// <item>Solves the layout: plays it in a deterministic simulation that uses the same rules as the game
    /// (player stop distance, bullet sweep, explosion radius, critical size) with an "optimal" player who
    /// always fires the smallest shot that lets it move on. A binary search finds the smallest start size
    /// that wins.</item>
    /// <item>Spawns the obstacles and sets the player's size to <c>required * sizeReserve</c> (1.2 = 20% reserve).
    /// If a layout cannot be won within <see cref="maxStartSize"/>, a new layout is drawn.</item>
    /// </list>
    /// </summary>
    public sealed class LevelController : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private Player player;
        [SerializeField] private Door door;
        [SerializeField] private Obstacle obstaclePrefab;

        [Tooltip("Source of the critical (lose) size. Without it the player's hard minimum is used.")]
        [SerializeField] private GameController gameController;

        [Header("Generation")]
        [Tooltip("Field size: x = width (world X), y = length (world Z).")]
        [SerializeField] private Vector2 fieldSize = new Vector2(10f, 22f);

        [SerializeField, Min(0)] private int obstacleCount = 40;

        [Tooltip("Minimal distance between obstacle centres.")]
        [SerializeField, Min(0f)] private float minSpacing = 1f;

        [Tooltip("No obstacles closer than this to the player and the door.")]
        [SerializeField, Min(0f)] private float safeRadius = 3f;

        [Tooltip("0 = different level every run, otherwise the same level for the same seed.")]
        [SerializeField] private int seed;

        [Tooltip("Attempts to place one obstacle before giving up on it.")]
        [SerializeField, Min(1)] private int maxAttemptsPerObstacle = 30;

        [Header("Start size (solver)")]
        [Tooltip("Start size = smallest winning size * this. 1.2 = 20% reserve.")]
        [SerializeField, Min(1f)] private float sizeReserve = 1.2f;

        [Tooltip("Layouts that need a bigger start size than this are thrown away and redrawn.")]
        [SerializeField, Min(0.1f)] private float maxStartSize = 6f;

        [Tooltip("Precision of the required start size.")]
        [SerializeField, Min(0.001f)] private float sizePrecision = 0.02f;

        [Tooltip("Shot sizes the optimal player tries, in steps of this.")]
        [SerializeField, Min(0.005f)] private float shotStep = 0.02f;

        [SerializeField, Min(1)] private int maxLayoutAttempts = 10;

        private readonly List<Obstacle> _obstacles = new List<Obstacle>();

        public IReadOnlyList<Obstacle> Obstacles => _obstacles;

        /// <summary>Smallest start size the optimal player wins with.</summary>
        public float RequiredSize { get; private set; }

        /// <summary>Start size given to the player: <see cref="RequiredSize"/> * reserve.</summary>
        public float StartSize { get; private set; }

        private void Start()
        {
            Generate();
        }

        /// <summary>Draws layouts until one is solvable, spawns it and sets the player's start size.</summary>
        public void Generate()
        {
            Clear();
            if (obstaclePrefab == null || player == null || door == null)
            {
                Debug.LogError("LevelController: player, door and obstacle prefab must be assigned.", this);
                return;
            }

            if (seed != 0) Random.InitState(seed);

            List<Vector3> layout = null;
            for (int attempt = 1; attempt <= maxLayoutAttempts; attempt++)
            {
                layout = CreateLayout();
                var solver = new Solver(this, layout);

                if (solver.TryFindRequiredSize(out float required, out int shots))
                {
                    RequiredSize = required;
                    StartSize = required * sizeReserve;
                    Spawn(layout);
                    player.SetSize(StartSize);
                    Debug.Log($"Level: {layout.Count} obstacles, required size {required:0.00}, " +
                              $"start size {StartSize:0.00} (x{sizeReserve:0.##}), optimal run {shots} shots, " +
                              $"layout attempt {attempt}.", this);
                    return;
                }
            }

            // No solvable layout found: keep the last one and give the maximum size so the game still runs.
            Spawn(layout);
            RequiredSize = StartSize = maxStartSize;
            player.SetSize(StartSize);
            Debug.LogWarning($"LevelController: no layout was solvable within size {maxStartSize} " +
                             $"after {maxLayoutAttempts} attempts.", this);
        }

        public void Clear()
        {
            foreach (var obstacle in _obstacles)
            {
                if (obstacle != null) Destroy(obstacle.gameObject);
            }
            _obstacles.Clear();
        }

        // ---- Layout ------------------------------------------------------------------------

        private List<Vector3> CreateLayout()
        {
            var placed = new List<Vector3>(obstacleCount);
            for (int i = 0; i < obstacleCount; i++)
            {
                if (TryFindPosition(placed, out var position)) placed.Add(position);
            }
            return placed;
        }

        private void Spawn(List<Vector3> layout)
        {
            foreach (var position in layout)
            {
                _obstacles.Add(Instantiate(obstaclePrefab, position, Quaternion.identity, transform));
            }
        }

        private bool TryFindPosition(List<Vector3> placed, out Vector3 position)
        {
            Vector3 center = transform.position;
            for (int attempt = 0; attempt < maxAttemptsPerObstacle; attempt++)
            {
                position = new Vector3(
                    center.x + Random.Range(-fieldSize.x, fieldSize.x) * 0.5f,
                    center.y,
                    center.z + Random.Range(-fieldSize.y, fieldSize.y) * 0.5f);

                if (IsFree(position, placed)) return true;
            }

            position = default;
            return false;
        }

        private bool IsFree(Vector3 position, List<Vector3> placed)
        {
            if (FlatDistance(position, player.transform.position) < safeRadius) return false;
            if (FlatDistance(position, door.transform.position) < safeRadius) return false;

            foreach (var other in placed)
            {
                if (FlatDistance(position, other) < minSpacing) return false;
            }
            return true;
        }

        private static float FlatDistance(Vector3 a, Vector3 b)
        {
            float dx = a.x - b.x;
            float dz = a.z - b.z;
            return Mathf.Sqrt(dx * dx + dz * dz);
        }

        private void OnDrawGizmos()
        {
            Gizmos.color = Color.yellow;
            Gizmos.DrawWireCube(transform.position, new Vector3(fieldSize.x, 0.1f, fieldSize.y));
        }

        // ---- Solver ------------------------------------------------------------------------

        /// <summary>
        /// Plays one layout without physics. Everything happens on the straight line from the player to the
        /// door: <c>t</c> is the distance along it, <c>h</c> the sideways distance of an obstacle from it.
        /// Obstacles are vertical capsules; a sphere at height <c>y</c> touches one when
        /// <c>h² + gap(y)² &lt; (sphereRadius + obstacleRadius)²</c>, where <c>gap</c> is how far <c>y</c> is
        /// outside the capsule's straight part. That is the same test the game's sphere casts and
        /// overlap sphere do, so the simulated run matches the real one.
        /// </summary>
        private sealed class Solver
        {
            private const float MinProgress = 0.01f;
            private const int MaxShots = 500;

            private readonly LevelController _owner;
            private readonly Vector2 _start;
            private readonly Vector2 _direction;
            private readonly float _pathLength;
            private readonly Vector2[] _positions;
            private readonly float[] _t;
            private readonly float[] _h;
            private readonly bool[] _alive;
            private readonly bool[] _trial;

            private readonly float _groundY;
            private readonly float _obstacleRadius;
            private readonly float _axisMinY;
            private readonly float _axisMaxY;

            private readonly float _criticalSize;
            private readonly float _baseShot;
            private readonly float _spawnDistance;
            private readonly float _lookAhead;
            private readonly float _explosionFactor;
            private readonly float _maxFlight;
            private const float MinHop = 0.05f; // matches Player.minHopLength's default

            public Solver(LevelController owner, List<Vector3> layout)
            {
                _owner = owner;
                var player = owner.player;

                Vector3 start3 = player.transform.position;
                Vector3 door3 = owner.door.transform.position;
                _start = new Vector2(start3.x, start3.z);
                Vector2 toDoor = new Vector2(door3.x, door3.z) - _start;
                _pathLength = toDoor.magnitude;
                _direction = _pathLength > 0f ? toDoor / _pathLength : Vector2.up;

                int count = layout.Count;
                _positions = new Vector2[count];
                _t = new float[count];
                _h = new float[count];
                _alive = new bool[count];
                _trial = new bool[count];
                for (int i = 0; i < count; i++)
                {
                    var p = new Vector2(layout[i].x, layout[i].z);
                    Vector2 offset = p - _start;
                    _positions[i] = p;
                    _t[i] = Vector2.Dot(offset, _direction);
                    _h[i] = Mathf.Abs(offset.x * _direction.y - offset.y * _direction.x);
                }

                _groundY = player.GroundHeight;
                ReadObstacleShape(owner.obstaclePrefab, owner.transform.position.y,
                    out _obstacleRadius, out _axisMinY, out _axisMaxY);

                _criticalSize = owner.gameController != null ? owner.gameController.MinPlayerSize : player.MinSize;
                _baseShot = player.BaseBulletSize;
                _spawnDistance = player.BulletSpawnDistance;
                _lookAhead = _spawnDistance * 2f;

                var bullet = player.BulletPrefab;
                _explosionFactor = bullet != null ? bullet.ExplosionFactor : 1.2f;
                _maxFlight = bullet != null ? bullet.MaxFlightDistance : 60f;
            }

            /// <summary>Binary search for the smallest winning start size, then makes sure the reserve size wins too.</summary>
            public bool TryFindRequiredSize(out float required, out int shots)
            {
                required = 0f;
                shots = 0;

                float low = _criticalSize;
                float high = _owner.maxStartSize;
                if (!Simulate(high, out _)) return false;

                while (high - low > _owner.sizePrecision)
                {
                    float mid = (low + high) * 0.5f;
                    if (Simulate(mid, out _)) high = mid;
                    else low = mid;
                }

                // A bigger player has a wider corridor and meets more obstacles, so winning is not strictly
                // monotonic in size. Walk up until both the required size and the reserve size win.
                for (float size = high; size <= _owner.maxStartSize; size += _owner.sizePrecision)
                {
                    if (Simulate(size, out shots) && Simulate(size * _owner.sizeReserve, out _))
                    {
                        required = size;
                        return true;
                    }
                }
                return false;
            }

            /// <summary>Plays the level with the "smallest clearing shot" player. True if the door is reached.</summary>
            private bool Simulate(float startSize, out int shots)
            {
                shots = 0;
                if (startSize < _criticalSize) return false;

                for (int i = 0; i < _alive.Length; i++) _alive[i] = true;
                float size = startSize;
                float s = Advance(0f, size, _alive, out bool won);

                while (!won && shots < MaxShots)
                {
                    bool cleared = false;
                    for (float shot = _baseShot; size - shot >= _criticalSize; shot += _owner.shotStep)
                    {
                        Array.Copy(_alive, _trial, _alive.Length);
                        float sizeAfter = size - shot;
                        Shoot(s, sizeAfter, shot, _trial);

                        float next = Advance(s, sizeAfter, _trial, out won);
                        if (won || next > s + MinProgress)
                        {
                            Array.Copy(_trial, _alive, _alive.Length);
                            size = sizeAfter;
                            s = next;
                            cleared = true;
                            shots++;
                            break;
                        }
                    }

                    if (!cleared) return false; // even the biggest allowed shot does not open the way
                }
                return won;
            }

            /// <summary>
            /// Where the player ends up after hopping from <paramref name="s"/>: it stops <c>lookAhead</c> before
            /// the first obstacle its sphere would touch, or reaches the door.
            /// </summary>
            private float Advance(float s, float size, bool[] alive, out bool won)
            {
                float radius = size * 0.5f;
                float reach2 = Reach2(radius, _groundY + radius);

                float nearest = float.MaxValue;
                for (int i = 0; i < alive.Length; i++)
                {
                    if (!alive[i] || _t[i] <= s) continue;
                    float h2 = _h[i] * _h[i];
                    if (h2 >= reach2) continue;

                    float travel = Mathf.Max(0f, _t[i] - s - Mathf.Sqrt(reach2 - h2));
                    if (travel < nearest) nearest = travel;
                }

                float move = nearest - _lookAhead;
                float toDoor = _pathLength - s - radius; // touching the door counts as reaching it
                won = move >= toDoor;
                if (won) return _pathLength;
                return move >= MinHop ? s + move : s;
            }

            /// <summary>
            /// Fires a bullet of <paramref name="shot"/> size from a player of <paramref name="sizeAfter"/>
            /// standing at <paramref name="s"/>, and clears every obstacle the explosion reaches.
            /// </summary>
            private void Shoot(float s, float sizeAfter, float shot, bool[] alive)
            {
                float bulletRadius = shot * 0.5f;
                float bulletY = _groundY + sizeAfter * 0.5f; // the bullet flies at the player's centre height
                float start = s + sizeAfter * 0.5f + _spawnDistance + bulletRadius;

                float hitReach2 = Reach2(bulletRadius, bulletY);
                float nearest = float.MaxValue;
                for (int i = 0; i < alive.Length; i++)
                {
                    if (!alive[i]) continue;
                    float h2 = _h[i] * _h[i];
                    if (h2 >= hitReach2) continue;

                    float along = _t[i] - start;
                    float travel;
                    if (along * along + h2 < hitReach2) travel = 0f; // already touching at launch
                    else if (along <= 0f) continue;                 // behind the bullet
                    else travel = along - Mathf.Sqrt(hitReach2 - h2);

                    if (travel < nearest) nearest = travel;
                }

                if (nearest > _maxFlight) return; // missed everything: the size is simply spent

                Vector2 impact = _start + _direction * (start + nearest);
                float blastReach2 = Reach2(shot * _explosionFactor, bulletY);
                if (blastReach2 <= 0f) return;

                for (int i = 0; i < alive.Length; i++)
                {
                    if (alive[i] && (_positions[i] - impact).sqrMagnitude < blastReach2) alive[i] = false;
                }
            }

            /// <summary>
            /// Squared horizontal distance within which a sphere of <paramref name="radius"/> at height
            /// <paramref name="y"/> touches an obstacle (≤ 0 if it is entirely above or below it).
            /// </summary>
            private float Reach2(float radius, float y)
            {
                float gap = y < _axisMinY ? _axisMinY - y : (y > _axisMaxY ? y - _axisMaxY : 0f);
                float reach = radius + _obstacleRadius;
                return reach * reach - gap * gap;
            }

            /// <summary>Reads the obstacle's capsule (radius and the height span of its straight part) from the prefab.</summary>
            private static void ReadObstacleShape(Obstacle prefab, float spawnY,
                out float radius, out float axisMinY, out float axisMaxY)
            {
                Vector3 scale = prefab.transform.localScale;
                if (prefab.TryGetComponent(out CapsuleCollider capsule) && capsule.direction == 1)
                {
                    radius = capsule.radius * Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.z));
                    float height = capsule.height * Mathf.Abs(scale.y);
                    float halfAxis = Mathf.Max(0f, height * 0.5f - radius);
                    float centerY = spawnY + capsule.center.y * scale.y;
                    axisMinY = centerY - halfAxis;
                    axisMaxY = centerY + halfAxis;
                    return;
                }

                // Any other collider: treat it as an infinitely tall cylinder as wide as its prefab scale.
                radius = 0.5f * Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.z));
                axisMinY = float.MinValue;
                axisMaxY = float.MaxValue;
            }
        }
    }
}
