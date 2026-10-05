using System;

namespace WindTraceRide.Core
{
    public sealed class YesoulS1Decoder : ITrainerPacketDecoder
    {
        private const float SpeedPerCadence = 0.37497622f;

        public TrainerProtocol Protocol => TrainerProtocol.YesoulLegacy;

        public bool TryDecode(byte[] payload, DateTimeOffset timestamp, out TrainerTelemetry telemetry)
        {
            telemetry = null;
            if (payload == null || payload.Length != 12) return false;

            var distanceRaw = (payload[2] << 8) | payload[3];
            var resistance = payload[4];
            var cadence = payload[6];
            var power = (payload[7] << 8) | payload[8];

            telemetry = new TrainerTelemetry(
                timestamp,
                speedKph: cadence * SpeedPerCadence,
                cadenceRpm: cadence,
                powerWatts: power,
                resistanceLevel: resistance,
                distanceKm: distanceRaw / 100f);
            return true;
        }
    }
}
