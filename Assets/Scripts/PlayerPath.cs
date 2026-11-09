using UnityEngine;

namespace Game.Simple
{
    /// <summary>
    /// Flat strip on the ground from the <see cref="Player"/> to the <see cref="Door"/>.
    /// Its width is the player's diameter, so it narrows while the player shrinks and
    /// shows which obstacles are in the way. Put it on an object with a Quad mesh
    /// (the prefab already has one); this script only places and scales that object.
    /// </summary>
    public sealed class PlayerPath : MonoBehaviour
    {
        [SerializeField] private Player player;
        [SerializeField] private Door door;

        [Tooltip("World height of the strip: just above the ground to avoid z-fighting.")]
        [SerializeField] private float height = 0.01f;

        private Renderer _renderer;

        private void Awake()
        {
            _renderer = GetComponent<Renderer>();
        }

        private void LateUpdate()
        {
            if (player == null || door == null) return;

            Vector3 from = player.transform.position;
            Vector3 to = door.transform.position;
            from.y = to.y = height;

            Vector3 delta = to - from;
            float length = delta.magnitude;

            bool visible = length > 0.01f;
            if (_renderer != null) _renderer.enabled = visible;
            if (!visible) return;

            // A Unity Quad lies in its XY plane facing -Z; tilting it 90° around X lays it flat, facing up,
            // with local X = width and local Y = length along the path.
            transform.SetPositionAndRotation(
                (from + to) * 0.5f,
                Quaternion.LookRotation(delta / length) * Quaternion.Euler(90f, 0f, 0f));
            transform.localScale = new Vector3(player.Size, length, 1f);
        }
    }
}
