using System;

namespace WindTraceRide.Core
{
    public sealed class FtmsIndoorBikeDataDecoder : ITrainerPacketDecoder
    {
        public TrainerProtocol Protocol => TrainerProtocol.Ftms;

        public bool TryDecode(byte[] payload, DateTimeOffset timestamp, out TrainerTelemetry telemetry)
        {
            telemetry = null;
            if (payload == null || payload.Length < 2) return false;

            var flags = ReadUInt16(payload, 0);
            var offset = 2;
            float? speed = null;
            float? cadence = null;
            float? distance = null;
            float? resistance = null;
            int? power = null;
            int? heartRate = null;

            // FTMS bit 0 means "More Data"; instantaneous speed is omitted when set.
            if ((flags & (1 << 0)) == 0)
            {
                if (!TryReadUInt16(payload, ref offset, out var speedRaw)) return false;
                speed = speedRaw / 100f;
            }

            if ((flags & (1 << 1)) != 0 && !Skip(payload, ref offset, 2)) return false;
            if ((flags & (1 << 2)) != 0)
            {
                if (!TryReadUInt16(payload, ref offset, out var cadenceRaw)) return false;
                cadence = cadenceRaw / 2f;
            }
            if ((flags & (1 << 3)) != 0 && !Skip(payload, ref offset, 2)) return false;
            if ((flags & (1 << 4)) != 0)
            {
                if (!TryReadUInt24(payload, ref offset, out var distanceRaw)) return false;
                distance = distanceRaw / 1000f;
            }
            if ((flags & (1 << 5)) != 0)
            {
                if (!TryReadInt16(payload, ref offset, out var resistanceRaw)) return false;
                resistance = resistanceRaw / 10f;
            }
            if ((flags & (1 << 6)) != 0)
            {
                if (!TryReadInt16(payload, ref offset, out var powerRaw)) return false;
                power = powerRaw;
            }
            if ((flags & (1 << 7)) != 0 && !Skip(payload, ref offset, 2)) return false;
            if ((flags & (1 << 8)) != 0 && !Skip(payload, ref offset, 5)) return false;
            if ((flags & (1 << 9)) != 0)
            {
                if (!TryReadByte(payload, ref offset, out var heartRateRaw)) return false;
                heartRate = heartRateRaw;
            }
            if ((flags & (1 << 10)) != 0 && !Skip(payload, ref offset, 1)) return false;
            if ((flags & (1 << 11)) != 0 && !Skip(payload, ref offset, 2)) return false;
            if ((flags & (1 << 12)) != 0 && !Skip(payload, ref offset, 2)) return false;

            telemetry = new TrainerTelemetry(timestamp, speed, cadence, power, resistance, distance, heartRate);
            return speed.HasValue || cadence.HasValue || power.HasValue || resistance.HasValue || heartRate.HasValue;
        }

        private static ushort ReadUInt16(byte[] data, int offset) =>
            (ushort)(data[offset] | (data[offset + 1] << 8));

        private static bool TryReadUInt16(byte[] data, ref int offset, out ushort value)
        {
            value = 0;
            if (offset + 2 > data.Length) return false;
            value = ReadUInt16(data, offset);
            offset += 2;
            return true;
        }

        private static bool TryReadInt16(byte[] data, ref int offset, out short value)
        {
            value = 0;
            if (!TryReadUInt16(data, ref offset, out var raw)) return false;
            value = unchecked((short)raw);
            return true;
        }

        private static bool TryReadUInt24(byte[] data, ref int offset, out int value)
        {
            value = 0;
            if (offset + 3 > data.Length) return false;
            value = data[offset] | (data[offset + 1] << 8) | (data[offset + 2] << 16);
            offset += 3;
            return true;
        }

        private static bool TryReadByte(byte[] data, ref int offset, out byte value)
        {
            value = 0;
            if (offset >= data.Length) return false;
            value = data[offset++];
            return true;
        }

        private static bool Skip(byte[] data, ref int offset, int count)
        {
            if (offset + count > data.Length) return false;
            offset += count;
            return true;
        }
    }
}
