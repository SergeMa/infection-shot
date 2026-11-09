using UnityEngine;

namespace Game.Simple
{
    /// <summary>
    /// Obstacle on the level. Once infected by a <see cref="Bullet"/> explosion it changes colour,
    /// swells for <see cref="infectionDelay"/> seconds and then bursts into particles.
    /// It keeps blocking the way until it bursts.
    /// <para>
    /// Allocation-free: the colour change uses a shared <see cref="MaterialPropertyBlock"/> (no material copies),
    /// and every obstacle emits into one shared <see cref="ParticleSystem"/> that is created on the first burst.
    /// </para>
    /// </summary>
    [RequireComponent(typeof(Collider))]
    public sealed class Obstacle : MonoBehaviour
    {
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor"); // URP Lit
        private static readonly int ColorId = Shader.PropertyToID("_Color");         // built-in shaders

        private static MaterialPropertyBlock s_infectedBlock;
        private static ParticleSystem s_burstParticles; // shared by all obstacles, recreated after a scene reload

        [Header("Infection")]
        [SerializeField, Min(0f)] private float infectionDelay = 1f;
        [SerializeField] private Color infectedColor = new Color(1f, 0.4f, 0.7f);

        [Tooltip("Scale multiplier the obstacle swells to right before it bursts.")]
        [SerializeField, Min(1f)] private float swellScale = 1.35f;

        [Header("Burst particles")]
        [SerializeField, Min(0)] private int burstCount = 10;
        [SerializeField, Min(0.01f)] private float particleSize = 0.22f;
        [SerializeField, Min(0f)] private float particleSpeed = 5f;
        [SerializeField, Min(0.05f)] private float particleLifetime = 0.7f;

        private Renderer _renderer;
        private Vector3 _baseScale;
        private float _infectedTime;

        public bool IsInfected { get; private set; }

        private void Awake()
        {
            _renderer = GetComponentInChildren<Renderer>();
            _baseScale = transform.localScale;
            enabled = false; // Update only runs while infected
        }

        public void Infect()
        {
            if (IsInfected) return;
            IsInfected = true;

            if (_renderer != null)
            {
                if (s_infectedBlock == null) s_infectedBlock = new MaterialPropertyBlock();
                s_infectedBlock.SetColor(BaseColorId, infectedColor);
                s_infectedBlock.SetColor(ColorId, infectedColor);
                _renderer.SetPropertyBlock(s_infectedBlock);
            }

            _infectedTime = 0f;
            enabled = true;
        }

        private void Update()
        {
            _infectedTime += Time.deltaTime;
            float t = infectionDelay > 0f ? Mathf.Clamp01(_infectedTime / infectionDelay) : 1f;

            // Ease in: barely moves at first, balloons right before the burst.
            transform.localScale = _baseScale * Mathf.Lerp(1f, swellScale, t * t);

            if (t >= 1f) Burst();
        }

        private void Burst()
        {
            if (burstCount > 0)
            {
                var particles = GetBurstParticles();
                var emit = new ParticleSystem.EmitParams
                {
                    position = _renderer != null ? _renderer.bounds.center : transform.position,
                    applyShapeToPosition = true,
                };
                particles.Emit(emit, burstCount);
            }

            Destroy(gameObject);
        }

        /// <summary>
        /// Lazily builds the one particle system all obstacles share: small spheres thrown out of a hemisphere,
        /// falling under gravity and shrinking away. Lit with the obstacle's own material in the infected colour.
        /// </summary>
        private ParticleSystem GetBurstParticles()
        {
            if (s_burstParticles != null) return s_burstParticles;

            var go = new GameObject("ObstacleBurstParticles");
            var particles = go.AddComponent<ParticleSystem>();
            particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear); // settings below need a stopped system

            var main = particles.main;
            main.playOnAwake = false;
            main.loop = true; // stays "playing" forever so emitted particles always simulate; emission is off anyway
            main.duration = 1f;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.startLifetime = new ParticleSystem.MinMaxCurve(particleLifetime * 0.7f, particleLifetime);
            main.startSpeed = new ParticleSystem.MinMaxCurve(particleSpeed * 0.6f, particleSpeed);
            main.startSize = new ParticleSystem.MinMaxCurve(particleSize * 0.6f, particleSize);
            main.gravityModifier = 1.5f;
            main.maxParticles = 512;

            var emission = particles.emission;
            emission.enabled = false; // only explicit Emit() calls

            var shape = particles.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Hemisphere;
            shape.radius = 0.3f;
            shape.rotation = new Vector3(-90f, 0f, 0f); // hemisphere opens upward

            var sizeOverLifetime = particles.sizeOverLifetime;
            sizeOverLifetime.enabled = true;
            sizeOverLifetime.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 1f, 1f, 0f));

            var particleRenderer = go.GetComponent<ParticleSystemRenderer>();
            particleRenderer.renderMode = ParticleSystemRenderMode.Mesh;
            particleRenderer.mesh = GetSphereMesh();
            particleRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            if (_renderer != null)
            {
                // One material for the whole system, created once: the obstacle material tinted infected.
                var material = new Material(_renderer.sharedMaterial) { name = "ObstacleBurst" };
                if (material.HasProperty(BaseColorId)) material.SetColor(BaseColorId, infectedColor);
                if (material.HasProperty(ColorId)) material.SetColor(ColorId, infectedColor);
                particleRenderer.sharedMaterial = material;
            }

            particles.Play();
            s_burstParticles = particles;
            return particles;
        }

        /// <summary>Unity's built-in sphere mesh, borrowed from a temporary primitive.</summary>
        private static Mesh GetSphereMesh()
        {
            var temp = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            var mesh = temp.GetComponent<MeshFilter>().sharedMesh;
            Destroy(temp);
            return mesh;
        }
    }
}
