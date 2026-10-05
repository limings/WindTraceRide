using System;

namespace WindTraceRide.Core
{
    public interface ITrainerTransport : IDisposable
    {
        event Action<TrainerAdvertisement> DeviceFound;
        event Action<TrainerConnectionState, string> StateChanged;
        event Action<RawTrainerPacket> PacketReceived;
        event Action<string> Error;

        void StartScan(int timeoutMilliseconds);
        void StopScan();
        void Connect(string deviceId);
        void Disconnect();
    }

    public interface ITrainerSettingsStore
    {
        SavedTrainer Load();
        void Save(SavedTrainer trainer);
        void Clear();
    }

    public interface ITrainerPacketDecoder
    {
        TrainerProtocol Protocol { get; }
        bool TryDecode(byte[] payload, DateTimeOffset timestamp, out TrainerTelemetry telemetry);
    }
}

