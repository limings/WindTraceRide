using System;
using System.Collections.Generic;

namespace WindTraceRide.Core
{
    public sealed class TrainerConnectionCoordinator : IDisposable
    {
        private readonly ITrainerTransport transport;
        private readonly ITrainerSettingsStore settings;
        private readonly Dictionary<TrainerProtocol, ITrainerPacketDecoder> decoders;
        private TrainerAdvertisement pendingTrainer;
        private TrainerTelemetry latestTelemetry;
        private bool autoConnectScan;
        private DateTimeOffset connectingSince;
        private static readonly TimeSpan TelemetryTimeout = TimeSpan.FromSeconds(45);

        public event Action<TrainerConnectionState, string> StateChanged;
        public event Action<TrainerAdvertisement> DeviceFound;
        public event Action<TrainerTelemetry> TelemetryReceived;
        public event Action<string> Error;

        public TrainerConnectionState State { get; private set; } = TrainerConnectionState.Idle;
        public SavedTrainer SavedTrainer => settings.Load();

        public TrainerConnectionCoordinator(
            ITrainerTransport transport,
            ITrainerSettingsStore settings,
            params ITrainerPacketDecoder[] decoders)
        {
            this.transport = transport ?? throw new ArgumentNullException(nameof(transport));
            this.settings = settings ?? throw new ArgumentNullException(nameof(settings));
            this.decoders = new Dictionary<TrainerProtocol, ITrainerPacketDecoder>();
            foreach (var decoder in decoders ?? Array.Empty<ITrainerPacketDecoder>())
                this.decoders[decoder.Protocol] = decoder;

            transport.DeviceFound += HandleDeviceFound;
            transport.StateChanged += HandleStateChanged;
            transport.PacketReceived += HandlePacket;
            transport.Error += HandleError;
        }

        public void StartAutoConnect(int timeoutMilliseconds = 12000)
        {
            var saved = settings.Load();
            if (saved == null || string.IsNullOrWhiteSpace(saved.Id))
            {
                SetState(TrainerConnectionState.NeedsPairing, "请在设置中连接动感单车");
                return;
            }

            autoConnectScan = true;
            SetState(TrainerConnectionState.Scanning, $"正在搜索 {saved.Name}");
            transport.StartScan(timeoutMilliseconds);
        }

        public void ScanForPairing(int timeoutMilliseconds = 12000)
        {
            autoConnectScan = false;
            pendingTrainer = null;
            SetState(TrainerConnectionState.Scanning, "正在搜索附近的动感单车");
            transport.StartScan(timeoutMilliseconds);
        }

        public void Connect(TrainerAdvertisement trainer)
        {
            if (trainer == null) throw new ArgumentNullException(nameof(trainer));
            pendingTrainer = trainer;
            latestTelemetry = null;
            autoConnectScan = false;
            transport.StopScan();
            SetState(TrainerConnectionState.Connecting, $"正在连接 {trainer.Name}");
            transport.Connect(trainer.Id);
        }

        public void ForgetTrainer()
        {
            transport.Disconnect();
            settings.Clear();
            pendingTrainer = null;
            latestTelemetry = null;
            SetState(TrainerConnectionState.NeedsPairing, "已忘记设备");
        }

        public void Tick(DateTimeOffset now)
        {
            if (State != TrainerConnectionState.Connecting || pendingTrainer == null ||
                now - connectingSince < TelemetryTimeout) return;
            transport.Disconnect();
            HandleError("蓝牙已连接，但 45 秒内没有收到骑行数据。请踩踏唤醒设备后重新扫描连接。");
        }

        private void HandleDeviceFound(TrainerAdvertisement trainer)
        {
            DeviceFound?.Invoke(trainer);
            if (!autoConnectScan) return;

            var saved = settings.Load();
            if (saved == null) return;
            var idMatches = string.Equals(saved.Id, trainer.Id, StringComparison.OrdinalIgnoreCase);
            var nameMatches = !string.IsNullOrWhiteSpace(saved.Name) &&
                              string.Equals(saved.Name, trainer.Name, StringComparison.OrdinalIgnoreCase);
            if (!idMatches && !nameMatches) return;

            pendingTrainer = trainer;
            autoConnectScan = false;
            transport.StopScan();
            SetState(TrainerConnectionState.Connecting, $"正在连接 {trainer.Name}");
            transport.Connect(trainer.Id);
        }

        private void HandleStateChanged(TrainerConnectionState state, string message)
        {
            // A GATT link and notification subscription are not proof that an
            // S1 is actually streaming. Only decoded ride data completes pairing.
            if (state == TrainerConnectionState.Connected)
                state = TrainerConnectionState.Connecting;
            SetState(state, message);
        }

        private void HandlePacket(RawTrainerPacket packet)
        {
            if (packet == null || !decoders.TryGetValue(packet.Protocol, out var decoder)) return;
            if (decoder.TryDecode(packet.Payload, DateTimeOffset.UtcNow, out var telemetry))
            {
                // OpenBike restores the S1's FTMS resistance value to the
                // physical 0–100 knob scale. Its firmware encodes whole levels
                // despite the FTMS 0.1-unit convention used by other bikes.
                if (packet.Protocol == TrainerProtocol.Ftms && telemetry.ResistanceLevel.HasValue &&
                    IsYesoulName(pendingTrainer?.Name))
                    telemetry = new TrainerTelemetry(telemetry.Timestamp, telemetry.SpeedKph,
                        telemetry.CadenceRpm, telemetry.PowerWatts,
                        telemetry.ResistanceLevel.Value * 10f, telemetry.DistanceKm, telemetry.HeartRateBpm);

                // FTMS permits Indoor Bike Data to be split across consecutive
                // notifications using the More Data flag. YESOUL sends cadence,
                // power and resistance first, followed by a speed-only frame.
                // Preserve fields omitted by the second frame instead of
                // replacing the complete reading with nulls.
                telemetry = MergeTelemetry(latestTelemetry, telemetry);
                latestTelemetry = telemetry;
                if (State == TrainerConnectionState.Connecting && pendingTrainer != null)
                {
                    settings.Save(new SavedTrainer(pendingTrainer.Id, pendingTrainer.Name, pendingTrainer.Protocol));
                    SetState(TrainerConnectionState.Connected, $"{pendingTrainer.Name} 骑行数据已就绪");
                }
                TelemetryReceived?.Invoke(telemetry);
            }
        }

        private static TrainerTelemetry MergeTelemetry(TrainerTelemetry previous, TrainerTelemetry current)
        {
            if (previous == null) return current;
            return new TrainerTelemetry(
                current.Timestamp,
                current.SpeedKph ?? previous.SpeedKph,
                current.CadenceRpm ?? previous.CadenceRpm,
                current.PowerWatts ?? previous.PowerWatts,
                current.ResistanceLevel ?? previous.ResistanceLevel,
                current.DistanceKm ?? previous.DistanceKm,
                current.HeartRateBpm ?? previous.HeartRateBpm);
        }

        private static bool IsYesoulName(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return false;
            name = name.Trim();
            return name.StartsWith("YESOUL", StringComparison.OrdinalIgnoreCase) ||
                   name.StartsWith("YS_", StringComparison.OrdinalIgnoreCase);
        }

        private void HandleError(string message)
        {
            latestTelemetry = null;
            Error?.Invoke(message);
            if (State != TrainerConnectionState.PermissionRequired)
                SetState(TrainerConnectionState.Failed, message);
        }

        private void SetState(TrainerConnectionState state, string message)
        {
            if (state == TrainerConnectionState.Connecting && State != TrainerConnectionState.Connecting)
                connectingSince = DateTimeOffset.UtcNow;
            State = state;
            StateChanged?.Invoke(state, message ?? string.Empty);
        }

        public void Dispose()
        {
            transport.DeviceFound -= HandleDeviceFound;
            transport.StateChanged -= HandleStateChanged;
            transport.PacketReceived -= HandlePacket;
            transport.Error -= HandleError;
            transport.Dispose();
        }
    }
}
