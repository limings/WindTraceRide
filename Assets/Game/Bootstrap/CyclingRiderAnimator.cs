using UnityEngine;
using WindTraceRide.Core;

namespace WindTraceRide.Bootstrap
{
    public sealed class CyclingRiderAnimator : MonoBehaviour
    {
        public AnimationClip pedalClip;
        private readonly CyclingMotionState motion = new CyclingMotionState();
        private Transform front, rear;
        private Quaternion frontRest, rearRest;
        private float radius;
        private bool ready;
        public float PedalPhase => motion.PedalPhase;
        public float WheelRadius => radius;

        public static Transform Find(Transform root, string name)
        {
            foreach (var child in root.GetComponentsInChildren<Transform>(true))
                if (child.name == name) return child;
            return null;
        }

        public void Initialize()
        {
            if (ready) return;
            if (pedalClip == null) throw new MissingReferenceException("Cyclist pedal clip is missing.");
            var legacy = GetComponent<Animation>();
            if (legacy != null) { legacy.playAutomatically = false; legacy.enabled = false; }
            pedalClip.SampleAnimation(gameObject, 0);
            front = Find(transform, "FrontWheelPivot_ROTATE_X");
            rear = Find(transform, "RearWheelPivot_ROTATE_X");
            if (front == null || rear == null) throw new MissingReferenceException("Cyclist wheel pivots are missing.");
            frontRest = front.localRotation;
            rearRest = rear.localRotation;
            radius = front.GetComponentInChildren<Renderer>().bounds.extents.y;
            if (radius <= .01f) throw new MissingReferenceException("Cyclist wheel radius is invalid.");
            ready = true;
        }

        public void Tick(float cadenceRpm, float speedMps, float seconds)
        {
            Initialize();
            motion.Advance(seconds, cadenceRpm, speedMps, radius);
            pedalClip.SampleAnimation(gameObject, motion.PedalPhase * pedalClip.length);
            var forward = (front.position - rear.position).normalized;
            Spin(front, frontRest, forward);
            Spin(rear, rearRest, forward);
        }

        private void Spin(Transform wheel, Quaternion rest, Vector3 forward)
        {
            var sign = Vector3.Dot(Vector3.Cross(wheel.TransformDirection(Vector3.right), Vector3.down), -forward) >= 0 ? 1f : -1f;
            wheel.localRotation = rest * Quaternion.AngleAxis(motion.WheelDegrees * sign, Vector3.right);
        }
    }
}
