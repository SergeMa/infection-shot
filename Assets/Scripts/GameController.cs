using System;
using UnityEngine;
using UnityEngine.Events;

namespace Game.Simple
{
    /// <summary>
    /// Decides the outcome of the level:
    /// <list type="bullet">
    /// <item><see cref="Door.PlayerEntered"/> → win.</item>
    /// <item><see cref="Player.SizeChanged"/> below <see cref="minPlayerSize"/> → lose at once
    /// (held the tap too long and pumped the ball into the shot).</item>
    /// <item><see cref="Player.BulletLaunched"/> → size is checked again after the shot.</item>
    /// <item>The player is stuck in front of an obstacle with no bullet flying, and even the smallest
    /// shot would take it below <see cref="minPlayerSize"/> → lose (not enough shots to clear the path).</item>
    /// </list>
    /// </summary>
    public sealed class GameController : MonoBehaviour
    {
        public enum State
        {
            Playing,
            Won,
            Lost,
        }

        [SerializeField] private Player player;
        [SerializeField] private Door door;

        [Tooltip("Critical size: the player must stay at least this big, otherwise the level is lost.")]
        [SerializeField, Min(0f)] private float minPlayerSize = 0.5f;

        [SerializeField] private UnityEvent onWin = new UnityEvent();
        [SerializeField] private UnityEvent onLose = new UnityEvent();

        public State CurrentState { get; private set; } = State.Playing;
        public float MinPlayerSize => minPlayerSize;

        /// <summary>Why the level was lost (for the UI). Empty unless <see cref="CurrentState"/> is Lost.</summary>
        public string LoseReason { get; private set; } = string.Empty;

        public event Action Won;
        public event Action Lost;

        private void OnEnable()
        {
            if (door != null) door.PlayerEntered += HandlePlayerEnteredDoor;
            if (player != null)
            {
                player.BulletLaunched += HandleBulletLaunched;
                player.SizeChanged += HandleSizeChanged;
            }
        }

        private void OnDisable()
        {
            if (door != null) door.PlayerEntered -= HandlePlayerEnteredDoor;
            if (player != null)
            {
                player.BulletLaunched -= HandleBulletLaunched;
                player.SizeChanged -= HandleSizeChanged;
            }
        }

        // LateUpdate: the player has finished its own Update this frame, so its state is current.
        private void LateUpdate()
        {
            if (CurrentState != State.Playing || player == null) return;

            if (player.IsWaitingForShot && player.Size - player.BaseBulletSize < minPlayerSize)
            {
                Lose("Не вистачило пострілів, щоб розчистити шлях");
            }
        }

        private void HandlePlayerEnteredDoor(Player enteredPlayer)
        {
            if (CurrentState != State.Playing) return;

            CurrentState = State.Won;
            enteredPlayer.Stop();
            Debug.Log("Level complete!");
            onWin.Invoke();
            Won?.Invoke();
        }

        private void HandleSizeChanged(float size)
        {
            if (CurrentState != State.Playing || size >= minPlayerSize) return;

            Lose(player.IsCharging
                ? "Куля-гравець перекачалась у постріл"
                : "Куля-гравець занадто мала");
        }

        private void HandleBulletLaunched(Bullet bullet)
        {
            if (CurrentState != State.Playing || player.Size >= minPlayerSize) return;

            Lose("Куля-гравець занадто мала");
        }

        private void Lose(string reason)
        {
            CurrentState = State.Lost;
            LoseReason = reason;
            player.Stop();
            Debug.Log($"Level failed: {reason} (size {player.Size:0.##}, critical {minPlayerSize:0.##}).");
            onLose.Invoke();
            Lost?.Invoke();
        }
    }
}
