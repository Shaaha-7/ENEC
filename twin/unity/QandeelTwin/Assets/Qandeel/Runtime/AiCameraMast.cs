using UnityEngine;

namespace Qandeel
{
    /// <summary>
    /// The AI detector camera on the western breakwater head (drawn 3x real size so it reads from the air, like the boats).
    /// The head pans slowly across the mouth, a status lamp blinks, and the field-of-view wedge on the water shows what
    /// it watches: cyan while watching, red on a jellyfish alarm.
    /// </summary>
    public sealed class AiCameraMast : MonoBehaviour
    {
        public static AiCameraMast I { get; private set; }
        public Transform head;
        public Renderer cone, lamp;
        public Material coneIdle, coneAlarm, lampIdle, lampAlarm;
        public Vector3 seaward;           // horizontal direction the camera faces (out of the mouth)
        public bool Alarm;
        public Vector3 Lens => head != null ? head.position : transform.position;

        void Awake() { I = this; }

        void Update()
        {
            float t = Time.unscaledTime;
            if (head != null)
                head.rotation = Quaternion.LookRotation(Quaternion.Euler(0, 22f * Mathf.Sin(t * 0.35f), 0) * seaward) * Quaternion.Euler(8f, 0, 0);
            if (cone != null) cone.sharedMaterial = Alarm ? coneAlarm : coneIdle;
            if (lamp != null)
            {
                bool on = Alarm ? (t * 4f) % 1f < 0.5f : (t * 0.8f) % 1f < 0.15f;
                lamp.sharedMaterial = Alarm ? lampAlarm : lampIdle;
                lamp.enabled = on;
            }
        }
    }
}
