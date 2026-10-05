using NUnit.Framework;
using WindTraceRide.Core;

namespace WindTraceRide.Tests
{
    public sealed class CyclingMotionTests
    {
        [Test] public void CadenceChangesPreservePhaseAndStoppingHoldsPose()
        {
            var motion=new CyclingMotionState();
            motion.Advance(.25f,60,0,.35f);
            Assert.That(motion.PedalPhase,Is.EqualTo(.25f).Within(.00001));
            motion.Advance(2,0,0,.35f);
            Assert.That(motion.PedalPhase,Is.EqualTo(.25f));
            motion.Advance(.25f,120,0,.35f);
            Assert.That(motion.PedalPhase,Is.EqualTo(.75f).Within(.00001));
        }
        [Test] public void CoastingSpinsWheelsWithoutMovingPedals()
        {
            var motion=new CyclingMotionState();
            motion.Advance(1,0,(float)System.Math.PI*.35f,.35f);
            Assert.That(motion.PedalPhase,Is.Zero);
            Assert.That(motion.WheelDegrees,Is.EqualTo(180).Within(.001));
        }
        [Test] public void InvalidTelemetryDoesNotPoisonFutureAnimation()
        {
            var motion=new CyclingMotionState();
            motion.Advance(float.NaN,60,4,.35f);
            motion.Advance(1,float.PositiveInfinity,float.NaN,.35f);
            motion.Advance(.5f,60,0,.35f);
            Assert.That(motion.PedalPhase,Is.EqualTo(.5f));
            Assert.That(motion.WheelDegrees,Is.Zero);
        }
    }
}
