using System;
using System.Collections.Generic;
using NUnit.Framework;
using WindTraceRide.Core;

namespace WindTraceRide.Tests
{
    public sealed class TrainerConnectionCoordinatorTests
    {
        [Test]
        public void AutoConnect_WithoutSavedTrainer_RequiresPairingAndDoesNotScan()
        {
            var transport = new FakeTransport();
            using var coordinator = new TrainerConnectionCoordinator(transport, new MemoryStore());

            coordinator.StartAutoConnect();

            Assert.That(coordinator.State, Is.EqualTo(TrainerConnectionState.NeedsPairing));
            Assert.That(transport.ScanCount, Is.Zero);
        }

        [Test]
        public void AutoConnect_IgnoresUnknownBikeAndConnectsSavedBike()
        {
            var transport = new FakeTransport();
            var store = new MemoryStore
            {
                Value = new SavedTrainer("AA:BB", "My Bike", TrainerProtocol.Ftms)
            };
            using var coordinator = new TrainerConnectionCoordinator(transport, store);

            coordinator.StartAutoConnect();
            transport.Publish(new TrainerAdvertisement("CC:DD", "Other Bike", -30, TrainerProtocol.Ftms));
            Assert.That(transport.ConnectedIds, Is.Empty);

            transport.Publish(new TrainerAdvertisement("AA:BB", "My Bike", -60, TrainerProtocol.Ftms));
            Assert.That(transport.ConnectedIds, Is.EqualTo(new[] { "AA:BB" }));
        }

        [Test]
        public void ManualConnect_SavesTrainerOnlyAfterValidRideData()
        {
            var transport = new FakeTransport();
            var store = new MemoryStore();
            using var coordinator = new TrainerConnectionCoordinator(transport, store, new YesoulS1Decoder());
            var trainer = new TrainerAdvertisement("11:22", "YESOUL S1", -42, TrainerProtocol.YesoulLegacy);

            coordinator.Connect(trainer);
            Assert.That(store.Value, Is.Null);

            transport.SetState(TrainerConnectionState.Connected, "connected");
            Assert.That(coordinator.State, Is.EqualTo(TrainerConnectionState.Connecting));
            Assert.That(store.Value, Is.Null);

            transport.PublishPacket(new RawTrainerPacket(TrainerProtocol.YesoulLegacy, new byte[] { 0xF5, 0, 0, 0, 8, 0, 80, 0, 100, 0, 0, 0xF6 }));
            Assert.That(coordinator.State, Is.EqualTo(TrainerConnectionState.Connected));
            Assert.That(store.Value.Id, Is.EqualTo("11:22"));
            Assert.That(store.Value.Protocol, Is.EqualTo(TrainerProtocol.YesoulLegacy));
        }

        [Test]
        public void YesoulFtmsPacket_UsesPhysicalResistanceScale()
        {
            var transport = new FakeTransport();
            using var coordinator = new TrainerConnectionCoordinator(transport, new MemoryStore(),
                new FtmsIndoorBikeDataDecoder());
            TrainerTelemetry reading = null;
            coordinator.TelemetryReceived += value => reading = value;
            coordinator.Connect(new TrainerAdvertisement("11:22", "YESOUL S1", -42, TrainerProtocol.Ftms));

            transport.PublishPacket(new RawTrainerPacket(TrainerProtocol.Ftms, new byte[]
            {
                0x64, 0x00, 0xB8, 0x0B, 0xA0, 0x00, 0x1B, 0x00, 0x96, 0x00
            }));

            Assert.That(reading, Is.Not.Null);
            Assert.That(reading.CadenceRpm, Is.EqualTo(80f));
            Assert.That(reading.PowerWatts, Is.EqualTo(150));
            Assert.That(reading.ResistanceLevel, Is.EqualTo(27f));
            Assert.That(coordinator.State, Is.EqualTo(TrainerConnectionState.Connected));
        }

        [Test]
        public void SplitFtmsPackets_PreserveCadenceAndPowerWhenSpeedArrivesSeparately()
        {
            var transport = new FakeTransport();
            using var coordinator = new TrainerConnectionCoordinator(transport, new MemoryStore(),
                new FtmsIndoorBikeDataDecoder());
            var readings = new List<TrainerTelemetry>();
            coordinator.TelemetryReceived += readings.Add;
            coordinator.Connect(new TrainerAdvertisement("11:22", "YESOUL S1", -42, TrainerProtocol.Ftms));

            // Real YESOUL split frame: More Data is set, so speed is omitted;
            // cadence, resistance and power are present in this first packet.
            transport.PublishPacket(new RawTrainerPacket(TrainerProtocol.Ftms, new byte[]
            {
                0xF5, 0x01, 0x30, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
                0x07, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00
            }));
            // Follow-up frame carries only instantaneous speed (9.11 km/h).
            transport.PublishPacket(new RawTrainerPacket(TrainerProtocol.Ftms, new byte[]
            {
                0x00, 0x08, 0x8F, 0x03, 0x00, 0x00
            }));

            Assert.That(readings, Has.Count.EqualTo(2));
            Assert.That(readings[1].SpeedKph, Is.EqualTo(9.11f).Within(0.001f));
            Assert.That(readings[1].CadenceRpm, Is.EqualTo(24f));
            Assert.That(readings[1].PowerWatts, Is.EqualTo(7));
            Assert.That(readings[1].ResistanceLevel, Is.EqualTo(0f));
        }

        [Test]
        public void InvalidPacket_DoesNotMarkTrainerConnected()
        {
            var transport = new FakeTransport();
            using var coordinator = new TrainerConnectionCoordinator(transport, new MemoryStore(), new YesoulS1Decoder());
            coordinator.Connect(new TrainerAdvertisement("11:22", "YESOUL S1", -42, TrainerProtocol.YesoulLegacy));
            transport.SetState(TrainerConnectionState.Connected, "GATT ready");
            transport.PublishPacket(new RawTrainerPacket(TrainerProtocol.YesoulLegacy, new byte[] { 0xF5, 0x00 }));

            Assert.That(coordinator.State, Is.EqualTo(TrainerConnectionState.Connecting));
        }

        [Test]
        public void SilentConnection_TimesOutWithActionableFailure()
        {
            var transport = new FakeTransport();
            using var coordinator = new TrainerConnectionCoordinator(transport, new MemoryStore(), new YesoulS1Decoder());
            coordinator.Connect(new TrainerAdvertisement("11:22", "YESOUL S1", -42, TrainerProtocol.YesoulLegacy));

            coordinator.Tick(DateTimeOffset.UtcNow.AddSeconds(46));

            Assert.That(coordinator.State, Is.EqualTo(TrainerConnectionState.Failed));
            Assert.That(transport.DisconnectCount, Is.EqualTo(1));
        }

        private sealed class MemoryStore : ITrainerSettingsStore
        {
            public SavedTrainer Value;
            public SavedTrainer Load() => Value;
            public void Save(SavedTrainer trainer) => Value = trainer;
            public void Clear() => Value = null;
        }

        private sealed class FakeTransport : ITrainerTransport
        {
            public event Action<TrainerAdvertisement> DeviceFound;
            public event Action<TrainerConnectionState, string> StateChanged;
            public event Action<RawTrainerPacket> PacketReceived;
            public event Action<string> Error;

            public int ScanCount { get; private set; }
            public int DisconnectCount { get; private set; }
            public List<string> ConnectedIds { get; } = new List<string>();

            public void StartScan(int timeoutMilliseconds) => ScanCount++;
            public void StopScan() { }
            public void Connect(string deviceId) => ConnectedIds.Add(deviceId);
            public void Disconnect() => DisconnectCount++;
            public void Publish(TrainerAdvertisement advertisement) => DeviceFound?.Invoke(advertisement);
            public void PublishPacket(RawTrainerPacket packet) => PacketReceived?.Invoke(packet);
            public void SetState(TrainerConnectionState state, string message) => StateChanged?.Invoke(state, message);
            public void Dispose() { }
        }
    }
}
