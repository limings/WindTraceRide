using System;
using NUnit.Framework;
using WindTraceRide.Core;

namespace WindTraceRide.Tests
{
    public sealed class TrainerDecoderTests
    {
        [Test]
        public void FtmsDecoder_ReadsSpeedCadenceResistanceAndPower()
        {
            // Flags: instantaneous cadence, resistance and instantaneous power.
            var packet = new byte[]
            {
                0x64, 0x00,
                0xB8, 0x0B, // 30.00 km/h
                0xA0, 0x00, // 80 rpm
                0x32, 0x00, // resistance 5.0
                0x96, 0x00  // 150 W
            };

            var parsed = new FtmsIndoorBikeDataDecoder().TryDecode(
                packet, DateTimeOffset.UnixEpoch, out var telemetry);

            Assert.That(parsed, Is.True);
            Assert.That(telemetry.SpeedKph, Is.EqualTo(30f).Within(0.001f));
            Assert.That(telemetry.CadenceRpm, Is.EqualTo(80f).Within(0.001f));
            Assert.That(telemetry.ResistanceLevel, Is.EqualTo(5f).Within(0.001f));
            Assert.That(telemetry.PowerWatts, Is.EqualTo(150));
        }

        [Test]
        public void FtmsDecoder_UsesCorrectFieldOrderThroughHeartRate()
        {
            // Speed + cadence + power + 5-byte energy block + HR.
            var packet = new byte[]
            {
                0x44, 0x03,
                0xC4, 0x09,
                0x96, 0x00,
                0xB4, 0x00,
                0x0A, 0x00, 0x64, 0x00, 0x14,
                0x8E
            };

            var parsed = new FtmsIndoorBikeDataDecoder().TryDecode(
                packet, DateTimeOffset.UnixEpoch, out var telemetry);

            Assert.That(parsed, Is.True);
            Assert.That(telemetry.CadenceRpm, Is.EqualTo(75f));
            Assert.That(telemetry.PowerWatts, Is.EqualTo(180));
            Assert.That(telemetry.HeartRateBpm, Is.EqualTo(142));
        }

        [Test]
        public void FtmsDecoder_RejectsTruncatedOptionalField()
        {
            var packet = new byte[] { 0x40, 0x00, 0xB8, 0x0B, 0x01 };

            var parsed = new FtmsIndoorBikeDataDecoder().TryDecode(
                packet, DateTimeOffset.UnixEpoch, out _);

            Assert.That(parsed, Is.False);
        }

        [Test]
        public void YesoulDecoder_ReadsKnownOffsets()
        {
            var packet = new byte[]
            {
                0xF5, 0x00,
                0x04, 0xD2, // 12.34 km
                0x08,
                0x00,
                0x50,       // 80 rpm
                0x00, 0xAF, // 175 W
                0x00, 0x00, 0xF6
            };

            var parsed = new YesoulS1Decoder().TryDecode(
                packet, DateTimeOffset.UnixEpoch, out var telemetry);

            Assert.That(parsed, Is.True);
            Assert.That(telemetry.DistanceKm, Is.EqualTo(12.34f).Within(0.001f));
            Assert.That(telemetry.ResistanceLevel, Is.EqualTo(8f));
            Assert.That(telemetry.CadenceRpm, Is.EqualTo(80f));
            Assert.That(telemetry.PowerWatts, Is.EqualTo(175));
        }

        [Test]
        public void YesoulDecoder_RejectsUnexpectedPacketLength()
        {
            var parsed = new YesoulS1Decoder().TryDecode(
                new byte[] { 0xF5, 0, 0, 0, 8, 0, 80, 0, 100 },
                DateTimeOffset.UnixEpoch, out var telemetry);

            Assert.That(parsed, Is.False);
            Assert.That(telemetry, Is.Null);
        }
    }
}
