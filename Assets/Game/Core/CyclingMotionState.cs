using System;

namespace WindTraceRide.Core
{
    public enum RiderCharacter { Male = 0, Female = 1 }

    /// <summary>Continuous pedal phase and independent distance-driven wheel rotation.</summary>
    public sealed class CyclingMotionState
    {
        public float PedalPhase { get; private set; }
        public float WheelDegrees { get; private set; }

        public void Advance(float seconds, float cadenceRpm, float speedMps, float wheelRadius)
        {
            if (!Finite(seconds) || seconds <= 0) return;
            if (Finite(cadenceRpm) && cadenceRpm > 0)
                PedalPhase = (float)((PedalPhase + (double)seconds * cadenceRpm / 60.0) % 1.0);
            if (Finite(speedMps) && speedMps > 0 && Finite(wheelRadius) && wheelRadius > .001f)
                WheelDegrees = (float)((WheelDegrees + (double)seconds * speedMps / wheelRadius * 180 / Math.PI) % 360.0);
        }

        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    }
}
