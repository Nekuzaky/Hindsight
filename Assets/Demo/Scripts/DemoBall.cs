using System.Globalization;
using UnityEngine;

namespace Nekuzaky.Hindsight.Demo
{
    [RequireComponent(typeof(Rigidbody2D))]
    public sealed class DemoBall : MonoBehaviour
    {
        #region Public

        [HindsightTrack] public int Bounces;
        [HindsightTrack("Heat")] public float Heat;

        public int Number { get; set; }
        public bool IsComet { get; set; }

        #endregion


        #region Private and Protected

        private const float KillHeight = -7f;
        private const float HeatPerBounce = 0.35f;
        private const float HeatDecay = 0.6f;

        private Rigidbody2D _body;
        private float _fastestSpeed;

        #endregion


        #region Unity Callbacks

        private void Awake()
        {
            _body = GetComponent<Rigidbody2D>();
        }

        private void Update()
        {
            Heat = Mathf.Max(0f, Heat - HeatDecay * Time.deltaTime);
            _fastestSpeed = Mathf.Max(_fastestSpeed, _body.linearVelocity.magnitude);

            if (transform.position.y >= KillHeight)
                return;

            var kind = IsComet ? "Comet" : "Ball";
            var speed = _fastestSpeed.ToString("0.0", CultureInfo.InvariantCulture);
            Debug.LogError($"{kind} #{Number} fell through the floor at {speed} m/s. Its Rigidbody2D uses Discrete collision detection, so at this speed it skips over the floor between two physics steps.");
            _body.simulated = false;
            enabled = false;
        }

        private void OnCollisionEnter2D(Collision2D collision)
        {
            Bounces++;
            Heat += HeatPerBounce;
        }

        #endregion
    }
}
