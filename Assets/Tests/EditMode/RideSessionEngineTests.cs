using System;
using NUnit.Framework;
using WindTraceRide.Core;

namespace WindTraceRide.Tests
{
    public sealed class RideSessionEngineTests
    {
        [Test]
        public void Session_DoesNotAdvanceWhilePlayerIsNotPedaling()
        {
            var engine = CreateShortSession();
            var stopped = new TrainerTelemetry(DateTimeOffset.UtcNow, cadenceRpm: 0, powerWatts: 0);

            var snapshot = engine.Tick(10, stopped);

            Assert.That(snapshot.PhaseElapsedSeconds, Is.Zero);
            Assert.That(snapshot.TotalActiveSeconds, Is.Zero);
        }

        [Test]
        public void BriefCadenceDropout_KeepsRideAndVisualCadenceMoving()
        {
            var engine = CreateShortSession();
            var now = DateTimeOffset.UtcNow;
            engine.Tick(1f, new TrainerTelemetry(now, cadenceRpm: 70, powerWatts: 120));

            var dropout = engine.Tick(1f, new TrainerTelemetry(now.AddSeconds(1),
                speedKph: 0, cadenceRpm: 0, powerWatts: 0));

            Assert.That(dropout.IsPedaling, Is.True);
            Assert.That(dropout.PedalingCadenceRpm, Is.EqualTo(70f));
            Assert.That(dropout.TotalActiveSeconds, Is.EqualTo(2f));
        }

        [Test]
        public void SustainedStop_PausesAfterDropoutGrace()
        {
            var engine = CreateShortSession();
            var now = DateTimeOffset.UtcNow;
            engine.Tick(1f, new TrainerTelemetry(now, cadenceRpm: 70, powerWatts: 120));
            engine.Tick(1f, new TrainerTelemetry(now.AddSeconds(1), cadenceRpm: 0, powerWatts: 0));

            var stopped = engine.Tick(1.1f, new TrainerTelemetry(now.AddSeconds(2.1),
                speedKph: 0, cadenceRpm: 0, powerWatts: 0));

            Assert.That(stopped.IsPedaling, Is.False);
            Assert.That(stopped.PedalingCadenceRpm, Is.Zero);
            Assert.That(stopped.TotalActiveSeconds, Is.EqualTo(2f));
        }

        [Test]
        public void SpeedOrPowerEvidence_BridgesMissingCadenceAfterRideStarts()
        {
            var engine = CreateShortSession();
            var now = DateTimeOffset.UtcNow;
            engine.Tick(1f, new TrainerTelemetry(now, cadenceRpm: 70, powerWatts: 120));

            var speedBacked = engine.Tick(3f, new TrainerTelemetry(now.AddSeconds(3),
                speedKph: 18, cadenceRpm: 0, powerWatts: 0));
            var powerBacked = engine.Tick(.5f, new TrainerTelemetry(now.AddSeconds(3.5),
                speedKph: 0, cadenceRpm: 0, powerWatts: 80));

            Assert.That(speedBacked.IsPedaling, Is.True);
            Assert.That(powerBacked.IsPedaling, Is.True);
            Assert.That(powerBacked.PedalingCadenceRpm, Is.EqualTo(70f));
        }

        [Test]
        public void CadenceHysteresis_PreventsChatterAroundStartThreshold()
        {
            var now = DateTimeOffset.UtcNow;
            var engine = CreateShortSession();
            Assert.That(engine.Tick(1f, new TrainerTelemetry(now, cadenceRpm: 7)).IsPedaling, Is.False);

            engine.Tick(1f, new TrainerTelemetry(now.AddSeconds(1), cadenceRpm: 12));
            var belowStartButAboveStop = engine.Tick(3f,
                new TrainerTelemetry(now.AddSeconds(4), cadenceRpm: 7));

            Assert.That(belowStartButAboveStop.IsPedaling, Is.True);
            Assert.That(belowStartButAboveStop.PedalingCadenceRpm, Is.EqualTo(7f));
        }

        [Test]
        public void CadenceDirectlyControlsRouteSpeed()
        {
            var now = DateTimeOffset.UtcNow;
            var slow = CreateShortSession().Tick(2f,
                new TrainerTelemetry(now, cadenceRpm: 35, powerWatts: 80));
            var baseline = CreateShortSession().Tick(2f,
                new TrainerTelemetry(now, cadenceRpm: 70, powerWatts: 120));
            var fast = CreateShortSession().Tick(2f,
                new TrainerTelemetry(now, cadenceRpm: 105, powerWatts: 180));

            Assert.That(slow.RouteProgressSeconds, Is.EqualTo(1f).Within(.001f));
            Assert.That(baseline.RouteProgressSeconds, Is.EqualTo(2f).Within(.001f));
            Assert.That(fast.RouteProgressSeconds, Is.EqualTo(3f).Within(.001f));
            Assert.That(slow.RideSpeedMultiplier, Is.LessThan(baseline.RideSpeedMultiplier));
            Assert.That(fast.RideSpeedMultiplier, Is.GreaterThan(baseline.RideSpeedMultiplier));
        }

        [Test]
        public void OnTargetCadence_BuildsComboAndScore()
        {
            var engine = CreateShortSession();
            var riding = new TrainerTelemetry(DateTimeOffset.UtcNow, cadenceRpm: 70, powerWatts: 120);

            var snapshot = engine.Tick(4, riding);

            Assert.That(snapshot.CadenceOnTarget, Is.True);
            Assert.That(snapshot.Combo, Is.EqualTo(2));
            Assert.That(snapshot.Score, Is.GreaterThan(0));
        }

        [Test]
        public void Session_AdvancesAndCompletesAllPhases()
        {
            var engine = CreateShortSession();
            var riding = new TrainerTelemetry(DateTimeOffset.UtcNow, cadenceRpm: 70, powerWatts: 200);

            var first = engine.Tick(5, riding);
            var complete = engine.Tick(5, riding);

            Assert.That(first.PhaseIndex, Is.EqualTo(1));
            Assert.That(complete.IsComplete, Is.True);
            Assert.That(complete.PhaseKind, Is.EqualTo(RidePhaseKind.Complete));
        }

        [Test]
        public void WindEnergy_AddsChargeAndScore_AndCapsCharge()
        {
            var engine = new RideSessionEngine(RideSessionConfig.FirstRide());
            engine.CollectWindEnergy(35f, 120);
            engine.CollectWindEnergy(80f, 120);

            Assert.That(engine.Current.UltimateCharge, Is.EqualTo(100f));
            Assert.That(engine.Current.Score, Is.EqualTo(240));
        }

        [Test]
        public void Orbs_AreTheMainSourceOfWindCharge()
        {
            var engine = new RideSessionEngine(RideSessionConfig.FirstRide());
            var riding = new TrainerTelemetry(DateTimeOffset.UtcNow, cadenceRpm: 60, powerWatts: 120);
            var trainingCharge = engine.Tick(60f, riding).UltimateCharge;
            Assert.That(trainingCharge, Is.LessThan(5f));

            engine.CollectWindEnergy(8f, 120);
            Assert.That(engine.Current.UltimateCharge, Is.EqualTo(trainingCharge + 8f).Within(.001f));
            Assert.That(engine.Current.Score, Is.GreaterThan(120));
        }

        [Test]
        public void WindBoost_SpendsCharge_AndAcceleratesRouteProgress()
        {
            var engine = new RideSessionEngine(RideSessionConfig.FirstRide());
            engine.CollectWindEnergy(30f, 0);
            Assert.That(engine.ActivateWindBoost(), Is.True);
            Assert.That(engine.Current.UltimateCharge, Is.EqualTo(5f));

            var riding = new TrainerTelemetry(DateTimeOffset.UtcNow, cadenceRpm: 70, powerWatts: 120);
            var snapshot = engine.Tick(2f, riding);

            Assert.That(snapshot.UltimateCharge, Is.GreaterThan(5f));
            Assert.That(snapshot.WindBoostActive, Is.True);
            Assert.That(snapshot.RouteProgressSeconds, Is.EqualTo(4f).Within(.001f));
            Assert.That(snapshot.WindBoostSeconds, Is.EqualTo(10f).Within(.001f));
        }

        [Test]
        public void WindBoost_RequiresEnoughCharge_AndCannotStack()
        {
            var engine = new RideSessionEngine(RideSessionConfig.FirstRide());
            engine.CollectWindEnergy(24f, 0);
            Assert.That(engine.ActivateWindBoost(), Is.False);
            engine.CollectWindEnergy(1f, 0);
            Assert.That(engine.ActivateWindBoost(), Is.True);
            Assert.That(engine.ActivateWindBoost(), Is.False);
        }

        [Test]
        public void RouteCompletion_EndsTheRide()
        {
            var engine = new RideSessionEngine(RideSessionConfig.FirstRide());
            engine.CompleteRoute();

            Assert.That(engine.Current.IsComplete, Is.True);
            Assert.That(engine.Current.PhaseKind, Is.EqualTo(RidePhaseKind.Complete));
        }

        [Test]
        public void Levels_HaveTwentyThirtyAndSeventyMinutePlans()
        {
            var expectedSeconds = new[] { 1200f, 1800f, 4200f };
            for (var level = 0; level < expectedSeconds.Length; level++)
            {
                var config = RideSessionConfig.ForLevel(level);
                Assert.That(config.Phases.Count, Is.EqualTo(7));
                Assert.That(new RideSessionEngine(config).Current.TotalPlannedSeconds, Is.EqualTo(expectedSeconds[level]));
            }
        }

        [Test]
        public void FirstRide_CompletesOnlyAfterTwentyActiveMinutes()
        {
            var engine = new RideSessionEngine(RideSessionConfig.FirstRide());
            var pedaling = new TrainerTelemetry(DateTimeOffset.UtcNow, cadenceRpm: 80, powerWatts: 130);
            var stopped = new TrainerTelemetry(DateTimeOffset.UtcNow, cadenceRpm: 0, powerWatts: 0);
            for (var stage = 0; stage < 7; stage++)
            {
                var paused = engine.Tick(300, stopped);
                Assert.That(paused.PhaseIndex, Is.EqualTo(stage));
                Assert.That(paused.PhaseElapsedSeconds, Is.EqualTo(0f));
                var duration = RideSessionConfig.FirstRide().Phases[stage].DurationSeconds;
                var beforeEnd = engine.Tick(duration - 1f, pedaling);
                Assert.That(beforeEnd.IsComplete, Is.False);
                Assert.That(beforeEnd.PhaseIndex, Is.EqualTo(stage));
                var afterEnd = engine.Tick(1, pedaling);
                Assert.That(afterEnd.IsComplete, Is.EqualTo(stage == 6));
            }
            Assert.That(engine.Current.TotalActiveSeconds, Is.EqualTo(1200f));
        }

        [Test]
        public void HigherLevels_RaiseCadenceAndPowerTargets()
        {
            var lakeside = RideSessionConfig.ForLevel(0);
            var canyon = RideSessionConfig.ForLevel(1);
            var village = RideSessionConfig.ForLevel(2);

            Assert.That(canyon.Phases[0].CadenceMin, Is.GreaterThan(lakeside.Phases[0].CadenceMin));
            Assert.That(village.Phases[0].CadenceMin, Is.GreaterThan(canyon.Phases[0].CadenceMin));
            Assert.That(village.Phases[0].TargetPowerFtpRatio, Is.GreaterThan(canyon.Phases[0].TargetPowerFtpRatio));
        }

        [Test]
        public void Boss_DoesNotFinishBeforeItsTenMinutes()
        {
            var config = new RideSessionConfig(150, new[]
            {
                new RidePhaseDefinition(RidePhaseKind.Boss, "Boss", 600, 70, 90, .5f)
            });
            var engine = new RideSessionEngine(config);
            var hardEffort = new TrainerTelemetry(DateTimeOffset.UtcNow, cadenceRpm: 80, powerWatts: 2000);
            var snapshot = engine.Tick(300, hardEffort);
            Assert.That(snapshot.IsComplete, Is.False);
            Assert.That(snapshot.PhaseElapsedSeconds, Is.EqualTo(300f));
        }

        [Test]
        public void FreshnessMonitor_ExpiresOldTelemetry()
        {
            var now = DateTimeOffset.UtcNow;
            var monitor = new TelemetryFreshnessMonitor(TimeSpan.FromSeconds(3));
            monitor.Observe(new TrainerTelemetry(now, cadenceRpm: 70));

            Assert.That(monitor.IsFresh(now.AddSeconds(2.9)), Is.True);
            Assert.That(monitor.IsFresh(now.AddSeconds(3.1)), Is.False);
        }

        private static RideSessionEngine CreateShortSession()
        {
            return new RideSessionEngine(new RideSessionConfig(150, new[]
            {
                new RidePhaseDefinition(RidePhaseKind.Warmup, "Warmup", 5, 60, 80, .5f),
                new RidePhaseDefinition(RidePhaseKind.Cooldown, "Cooldown", 5, 55, 75, .4f)
            }));
        }
    }
}
