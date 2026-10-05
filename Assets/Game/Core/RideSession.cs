using System;
using System.Collections.Generic;

namespace WindTraceRide.Core
{
    public enum RidePhaseKind
    {
        Warmup,
        Explore,
        CadenceChallenge,
        Recovery,
        Sprint,
        Boss,
        Cooldown,
        Complete
    }

    public sealed class RidePhaseDefinition
    {
        public RidePhaseKind Kind { get; }
        public string Title { get; }
        public float DurationSeconds { get; }
        public float CadenceMin { get; }
        public float CadenceMax { get; }
        public float TargetPowerFtpRatio { get; }

        public RidePhaseDefinition(
            RidePhaseKind kind,
            string title,
            float durationSeconds,
            float cadenceMin,
            float cadenceMax,
            float targetPowerFtpRatio = 0f)
        {
            Kind = kind;
            Title = title;
            DurationSeconds = Math.Max(1f, durationSeconds);
            CadenceMin = Math.Max(0f, cadenceMin);
            CadenceMax = Math.Max(CadenceMin, cadenceMax);
            TargetPowerFtpRatio = Math.Max(0f, targetPowerFtpRatio);
        }
    }

    public sealed class RideSessionConfig
    {
        public int FtpWatts { get; }
        public IReadOnlyList<RidePhaseDefinition> Phases { get; }

        public RideSessionConfig(int ftpWatts, IReadOnlyList<RidePhaseDefinition> phases)
        {
            FtpWatts = Math.Max(50, ftpWatts);
            Phases = phases ?? throw new ArgumentNullException(nameof(phases));
            if (phases.Count == 0) throw new ArgumentException("A ride needs at least one phase.", nameof(phases));
        }

        public static RideSessionConfig FirstRide(int ftpWatts = 150)
        {
            return new RideSessionConfig(ftpWatts, new[]
            {
                new RidePhaseDefinition(RidePhaseKind.Warmup, "Tahoe City · Warm up", 180, 50, 68, .45f),
                new RidePhaseDefinition(RidePhaseKind.Explore, "West Shore Trail · Explore", 180, 62, 78, .58f),
                new RidePhaseDefinition(RidePhaseKind.CadenceChallenge, "Pine Bay · Hold your rhythm", 180, 72, 86, .68f),
                new RidePhaseDefinition(RidePhaseKind.Recovery, "Lakefront Cruise · Recover", 180, 52, 68, .42f),
                new RidePhaseDefinition(RidePhaseKind.Sprint, "Homewood · Sprint", 180, 82, 100, .9f),
                new RidePhaseDefinition(RidePhaseKind.Boss, "West Shore Climb", 180, 70, 92, .82f),
                new RidePhaseDefinition(RidePhaseKind.Cooldown, "Tahoe Twilight · Cool down", 120, 45, 64, .35f)
            });
        }

        public static RideSessionConfig ForLevel(int levelIndex, int ftpWatts = 150)
        {
            var baseRide = FirstRide(ftpWatts);
            levelIndex = Math.Max(0, Math.Min(2, levelIndex));
            if (levelIndex == 0) return baseRide;

            var cadenceLift = levelIndex == 1 ? 5f : 9f;
            var powerLift = levelIndex == 1 ? .08f : .16f;
            var names = levelIndex == 1
                ? new[] { "Wind Canyon · Warm up", "Canyon River · Explore", "Sky Bridge · Hold your rhythm",
                    "Sandstone Bend · Recover", "Wind Pass · Sprint", "Canyon Summit Challenge", "Canyon Sunset · Cool down" }
                : new[] { "Cloud Village · Warm up", "Windmill Lane · Explore", "Flower Meadow · Hold your rhythm",
                    "Brookside · Recover", "Cloud Ridge · Sprint", "Windmill Peak Challenge", "Village Evening · Cool down" };
            var phases = new List<RidePhaseDefinition>();
            var phaseIndex = 0;
            var durations = levelIndex == 1
                ? new[] { 240f, 270f, 270f, 240f, 270f, 270f, 240f }
                : new[] { 600f, 600f, 600f, 600f, 600f, 600f, 600f };
            foreach (var phase in baseRide.Phases)
            {
                phases.Add(new RidePhaseDefinition(phase.Kind, names[phaseIndex], durations[phaseIndex++],
                    phase.CadenceMin + cadenceLift, phase.CadenceMax + cadenceLift,
                    phase.TargetPowerFtpRatio + powerLift));
            }
            return new RideSessionConfig(ftpWatts, phases);
        }
    }

    public sealed class RideSessionSnapshot
    {
        public RidePhaseKind PhaseKind { get; internal set; }
        public string PhaseTitle { get; internal set; }
        public int PhaseIndex { get; internal set; }
        public int PhaseCount { get; internal set; }
        public float PhaseElapsedSeconds { get; internal set; }
        public float PhaseDurationSeconds { get; internal set; }
        public float CadenceMin { get; internal set; }
        public float CadenceMax { get; internal set; }
        public int TargetPowerWatts { get; internal set; }
        public float TotalActiveSeconds { get; internal set; }
        public float RouteProgressSeconds { get; internal set; }
        public float TotalPlannedSeconds { get; internal set; }
        public int Score { get; internal set; }
        public int Combo { get; internal set; }
        public float UltimateCharge { get; internal set; }
        public float WindBoostSeconds { get; internal set; }
        public bool WindBoostActive { get; internal set; }
        public float BossHealth01 { get; internal set; }
        public bool CadenceOnTarget { get; internal set; }
        public bool IsPedaling { get; internal set; }
        public float PedalingCadenceRpm { get; internal set; }
        public float RideSpeedMultiplier { get; internal set; }
        public bool IsComplete { get; internal set; }
    }

    public sealed class RideSessionEngine
    {
        public const float WindBoostCost = 25f;
        public const float WindBoostDurationSeconds = 12f;
        public const float PedalingStartCadenceRpm = 10f;
        public const float PedalingStopCadenceRpm = 5f;
        public const float PedalingDropoutGraceSeconds = 2f;
        public const float ReferenceCadenceRpm = 70f;
        private const float MovingSpeedKph = 2f;
        private const int MovingPowerWatts = 10;
        private readonly RideSessionConfig config;
        private int phaseIndex;
        private float phaseElapsed;
        private float totalActive;
        private float routeProgress;
        private float score;
        private float comboProgress;
        private float ultimateCharge;
        private float windBoostRemaining;
        private float bossHealth = 1f;
        private float lowMotionSeconds;
        private float lastPedalingCadence;
        private bool isPedaling;
        private bool completed;

        public RideSessionEngine(RideSessionConfig config)
        {
            this.config = config ?? throw new ArgumentNullException(nameof(config));
        }

        public RideSessionSnapshot Current => BuildSnapshot(false, !completed && isPedaling,
            !completed ? lastPedalingCadence : 0f,
            !completed && isPedaling ? CadenceSpeed(lastPedalingCadence) : 0f);

        public void CollectWindEnergy(float charge, int scoreBonus)
        {
            if (completed) return;
            ultimateCharge = Math.Min(100f, ultimateCharge + Math.Max(0f, charge));
            score += Math.Max(0, scoreBonus);
        }

        public bool ActivateWindBoost()
        {
            if (completed || windBoostRemaining > 0f || ultimateCharge < WindBoostCost) return false;
            ultimateCharge -= WindBoostCost;
            windBoostRemaining = WindBoostDurationSeconds;
            return true;
        }

        public void CompleteRoute()
        {
            completed = true;
            ResetPedalingState();
        }

        public RideSessionSnapshot Tick(float deltaSeconds, TrainerTelemetry telemetry)
        {
            if (completed || deltaSeconds <= 0f)
                return BuildSnapshot(false, false);

            if (telemetry == null)
            {
                ResetPedalingState();
                return BuildSnapshot(false, false);
            }

            var cadence = Math.Max(0f, telemetry.CadenceRpm ?? 0f);
            var power = Math.Max(0, telemetry.PowerWatts ?? 0);
            var speed = Math.Max(0f, telemetry.SpeedKph ?? 0f);
            UpdatePedalingState(deltaSeconds, cadence, speed, power);
            var phase = config.Phases[phaseIndex];
            var cadenceOnTarget = cadence >= phase.CadenceMin && cadence <= phase.CadenceMax;

            if (!isPedaling) return BuildSnapshot(false, false);

            phaseElapsed += deltaSeconds;
            totalActive += deltaSeconds;
            var boostActive = windBoostRemaining > 0f;
            var rideSpeedMultiplier = CadenceSpeed(lastPedalingCadence);
            routeProgress += deltaSeconds * rideSpeedMultiplier * (boostActive ? 2f : 1f);
            windBoostRemaining = Math.Max(0f, windBoostRemaining - deltaSeconds);
            var scoreMultiplier = boostActive ? 2f : 1f;

            if (cadenceOnTarget)
            {
                comboProgress += deltaSeconds;
                ultimateCharge = Math.Min(100f, ultimateCharge + deltaSeconds * .04f);
                score += deltaSeconds * (12f + Math.Min(20f, comboProgress * .35f)) * scoreMultiplier;
            }
            else
            {
                comboProgress = Math.Max(0f, comboProgress - deltaSeconds * 1.8f);
                score += deltaSeconds * 5f * scoreMultiplier;
            }

            var targetPower = config.FtpWatts * phase.TargetPowerFtpRatio;
            if (targetPower > 0f && power >= targetPower)
            {
                var effort = Math.Min(1.6f, power / targetPower);
                score += deltaSeconds * 8f * effort * scoreMultiplier;
                ultimateCharge = Math.Min(100f, ultimateCharge + deltaSeconds * .02f * effort);
            }

            if (phase.Kind == RidePhaseKind.Boss)
            {
                var effectivePower = Math.Max(0f, power - targetPower * .55f);
                bossHealth = Math.Max(0f, bossHealth - deltaSeconds * effectivePower /
                    (config.FtpWatts * 95f) * (boostActive ? 2f : 1f));
            }

            if (phaseElapsed >= phase.DurationSeconds)
            {
                if (phaseIndex < config.Phases.Count - 1)
                    AdvancePhase();
                else
                {
                    phaseElapsed = phase.DurationSeconds;
                    completed = routeProgress >= TotalDurationSeconds();
                }
            }

            return BuildSnapshot(cadenceOnTarget, !completed, completed ? 0f : lastPedalingCadence,
                completed ? 0f : rideSpeedMultiplier);
        }

        private static float CadenceSpeed(float cadence) =>
            Math.Max(.35f, Math.Min(1.75f, cadence / ReferenceCadenceRpm));

        private void UpdatePedalingState(float deltaSeconds, float cadence, float speed, int power)
        {
            if (!isPedaling)
            {
                if (cadence < PedalingStartCadenceRpm) return;
                isPedaling = true;
                lowMotionSeconds = 0f;
                lastPedalingCadence = cadence;
                return;
            }

            var cadenceStillMoving = cadence >= PedalingStopCadenceRpm;
            var corroboratedMotion = speed >= MovingSpeedKph || power >= MovingPowerWatts;
            if (cadenceStillMoving || corroboratedMotion)
            {
                lowMotionSeconds = 0f;
                if (cadenceStillMoving) lastPedalingCadence = cadence;
                return;
            }

            lowMotionSeconds += deltaSeconds;
            if (lowMotionSeconds < PedalingDropoutGraceSeconds) return;
            ResetPedalingState();
        }

        private void ResetPedalingState()
        {
            isPedaling = false;
            lowMotionSeconds = 0f;
            lastPedalingCadence = 0f;
        }

        private void AdvancePhase()
        {
            phaseIndex++;
            phaseElapsed = 0f;
            comboProgress = Math.Max(0f, comboProgress * .5f);
            if (phaseIndex < config.Phases.Count) return;
            phaseIndex = config.Phases.Count - 1;
            completed = true;
        }

        private RideSessionSnapshot BuildSnapshot(bool cadenceOnTarget, bool pedaling, float pedalingCadence = 0f,
            float rideSpeedMultiplier = 0f)
        {
            var phase = config.Phases[phaseIndex];
            return new RideSessionSnapshot
            {
                PhaseKind = completed ? RidePhaseKind.Complete : phase.Kind,
                PhaseTitle = completed ? "Ride complete" : phase.Title,
                PhaseIndex = phaseIndex,
                PhaseCount = config.Phases.Count,
                PhaseElapsedSeconds = completed ? phase.DurationSeconds : phaseElapsed,
                PhaseDurationSeconds = phase.DurationSeconds,
                CadenceMin = phase.CadenceMin,
                CadenceMax = phase.CadenceMax,
                TargetPowerWatts = (int)Math.Round(config.FtpWatts * phase.TargetPowerFtpRatio),
                TotalActiveSeconds = totalActive,
                RouteProgressSeconds = routeProgress,
                TotalPlannedSeconds = TotalDurationSeconds(),
                Score = (int)Math.Round(score),
                Combo = Math.Min(99, (int)(comboProgress / 2f)),
                UltimateCharge = ultimateCharge,
                WindBoostSeconds = windBoostRemaining,
                WindBoostActive = windBoostRemaining > 0f,
                BossHealth01 = bossHealth,
                CadenceOnTarget = cadenceOnTarget,
                IsPedaling = pedaling,
                PedalingCadenceRpm = pedaling ? pedalingCadence : 0f,
                RideSpeedMultiplier = pedaling ? rideSpeedMultiplier : 0f,
                IsComplete = completed
            };
        }

        private float TotalDurationSeconds()
        {
            var seconds = 0f;
            foreach (var phase in config.Phases) seconds += phase.DurationSeconds;
            return seconds;
        }
    }

    public sealed class TelemetryFreshnessMonitor
    {
        private readonly TimeSpan timeout;
        private DateTimeOffset? lastTelemetry;

        public TelemetryFreshnessMonitor(TimeSpan timeout)
        {
            if (timeout <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(timeout));
            this.timeout = timeout;
        }

        public void Observe(TrainerTelemetry telemetry)
        {
            if (telemetry != null) lastTelemetry = telemetry.Timestamp;
        }

        public bool IsFresh(DateTimeOffset now) =>
            lastTelemetry.HasValue && now - lastTelemetry.Value <= timeout;
    }
}
