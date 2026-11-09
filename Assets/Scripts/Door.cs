using System;
using UnityEngine;

namespace Game.Simple
{
    /// <summary>
    /// Level exit. Opens (its panel slides down into the ground) once the <see cref="Player"/> is within
    /// <see cref="openDistance"/> metres, and reports when the player enters its trigger;
    /// <see cref="GameController"/> decides what that means (the win).
    /// The trigger sits on the root and stays in place while the panel moves.
    /// </summary>
    [RequireComponent(typeof(Collider))]
    public sealed class Door : MonoBehaviour
    {
        [SerializeField] private Player player;

        [Tooltip("The visible part of the door that moves when it opens (a child object).")]
        [SerializeField] private Transform panel;

        [Tooltip("The door opens when the player is this close (metres, measured on the ground).")]
        [SerializeField, Min(0f)] private float openDistance = 5f;

        [SerializeField, Min(0.01f)] private float openDuration = 0.6f;

        [Tooltip("How far down the panel slides when open.")]
        [SerializeField, Min(0f)] private float openDepth = 3.1f;

        private Vector3 _panelClosedPosition;
        private float _openProgress;

        /// <summary>The door started opening.</summary>
        public event Action Opened;

        /// <summary>The player entered the door's trigger.</summary>
        public event Action<Player> PlayerEntered;

        public bool IsOpen { get; private set; }

        private void Awake()
        {
            // A trigger, so the player's obstacle check does not see the door as something to stop at.
            GetComponent<Collider>().isTrigger = true;

            if (player == null) player = FindAnyObjectByType<Player>();
            if (panel != null) _panelClosedPosition = panel.localPosition;
        }

        private void Update()
        {
            if (!IsOpen)
            {
                if (player != null && FlatDistance(player.transform.position, transform.position) <= openDistance)
                {
                    IsOpen = true;
                    Opened?.Invoke();
                }
                return;
            }

            if (panel == null || _openProgress >= 1f) return;

            _openProgress = Mathf.Min(1f, _openProgress + Time.deltaTime / openDuration);
            float eased = 1f - (1f - _openProgress) * (1f - _openProgress); // fast start, soft stop
            panel.localPosition = _panelClosedPosition + Vector3.down * (openDepth * eased);
        }

        private void OnTriggerEnter(Collider other)
        {
            var enteredPlayer = other.GetComponentInParent<Player>();
            if (enteredPlayer != null) PlayerEntered?.Invoke(enteredPlayer);
        }

        private static float FlatDistance(Vector3 a, Vector3 b)
        {
            float dx = a.x - b.x;
            float dz = a.z - b.z;
            return Mathf.Sqrt(dx * dx + dz * dz);
        }
    }
}
