using System;

namespace WindTraceRide.Core
{
    public enum TrainerProtocol
    {
        Unknown,
        Ftms,
        YesoulLegacy,
        Simulator
    }

    public enum TrainerConnectionState
    {
        Idle,
        NeedsPairing,
        Scanning,
        Connecting,
        Connected,
        Reconnecting,
        PermissionRequired,
        Failed
    }

    [Serializable]
    public sealed class TrainerAdvertisement
    {
        public string Id;
        public string Name;
        public int Rssi;
        public TrainerProtocol Protocol;

        public TrainerAdvertisement(string id, string name, int rssi, TrainerProtocol protocol)
        {
            Id = id ?? string.Empty;
            Name = string.IsNullOrWhiteSpace(name) ? "Unnamed trainer" : name;
            Rssi = rssi;
            Protocol = protocol;
        }
    }

    [Serializable]
    public sealed class SavedTrainer
    {
        public string Id;
        public string Name;
        public TrainerProtocol Protocol;

        public SavedTrainer(string id, string name, TrainerProtocol protocol)
        {
            Id = id ?? string.Empty;
            Name = name ?? string.Empty;
            Protocol = protocol;
        }
    }

    public sealed class TrainerTelemetry
    {
        public DateTimeOffset Timestamp { get; }
        public float? SpeedKph { get; }
        public float? CadenceRpm { get; }
        public int? PowerWatts { get; }
        public float? ResistanceLevel { get; }
        public float? DistanceKm { get; }
        public int? HeartRateBpm { get; }

        public TrainerTelemetry(
            DateTimeOffset timestamp,
            float? speedKph = null,
            float? cadenceRpm = null,
            int? powerWatts = null,
            float? resistanceLevel = null,
            float? distanceKm = null,
            int? heartRateBpm = null)
        {
            Timestamp = timestamp;
            SpeedKph = speedKph;
            CadenceRpm = cadenceRpm;
            PowerWatts = powerWatts;
            ResistanceLevel = resistanceLevel;
            DistanceKm = distanceKm;
            HeartRateBpm = heartRateBpm;
        }
    }

    public sealed class RawTrainerPacket
    {
        public TrainerProtocol Protocol { get; }
        public byte[] Payload { get; }

        public RawTrainerPacket(TrainerProtocol protocol, byte[] payload)
        {
            Protocol = protocol;
            Payload = payload ?? Array.Empty<byte>();
        }
    }
}

