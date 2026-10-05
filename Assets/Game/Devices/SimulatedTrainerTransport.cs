using System;
using WindTraceRide.Core;

namespace WindTraceRide.Devices
{
    public sealed class SimulatedTrainerTransport : ITrainerTransport
    {
        public const string SimulatorId = "simulator://wind-trace-bike";
        private float elapsed;
        private float packetTimer;
        private bool connected;

        public event Action<TrainerAdvertisement> DeviceFound;
        public event Action<TrainerConnectionState, string> StateChanged;
        public event Action<RawTrainerPacket> PacketReceived;
        public event Action<string> Error;

        public void StartScan(int timeoutMilliseconds)
        {
            StateChanged?.Invoke(TrainerConnectionState.Scanning, "Searching for a simulated trainer");
            DeviceFound?.Invoke(new TrainerAdvertisement(
                SimulatorId, "Wind Trace Simulator", -35, TrainerProtocol.Ftms));
        }

        public void StopScan() { }

        public void Connect(string deviceId)
        {
            if (!string.Equals(deviceId, SimulatorId, StringComparison.OrdinalIgnoreCase))
            {
                Error?.Invoke("The simulated trainer was not found.");
                return;
            }
            connected = true;
            StateChanged?.Invoke(TrainerConnectionState.Connected, "Simulated trainer connected");
        }

        public void Disconnect()
        {
            connected = false;
            StateChanged?.Invoke(TrainerConnectionState.Idle, "Trainer disconnected");
        }

        public void Tick(float deltaTime)
        {
            if (!connected) return;
            elapsed += deltaTime;
            packetTimer += deltaTime;
            if (packetTimer < 0.25f) return;
            packetTimer = 0f;

            var cadence = 78f + (float)Math.Sin(elapsed * 0.55f) * 8f;
            var speed = cadence * 0.375f;
            var power = 115 + (int)Math.Round(Math.Sin(elapsed * 0.31f) * 25f);
            var packet = BuildFtmsPacket(speed, cadence, power, 5f);
            PacketReceived?.Invoke(new RawTrainerPacket(TrainerProtocol.Ftms, packet));
        }

        private static byte[] BuildFtmsPacket(float speedKph, float cadenceRpm, int powerWatts, float resistance)
        {
            // Speed is mandatory; bits 2, 5 and 6 add cadence, resistance and power.
            const ushort flags = (1 << 2) | (1 << 5) | (1 << 6);
            var bytes = new byte[10];
            WriteUInt16(bytes, 0, flags);
            WriteUInt16(bytes, 2, (ushort)Math.Round(speedKph * 100f));
            WriteUInt16(bytes, 4, (ushort)Math.Round(cadenceRpm * 2f));
            WriteUInt16(bytes, 6, unchecked((ushort)(short)Math.Round(resistance * 10f)));
            WriteUInt16(bytes, 8, unchecked((ushort)(short)powerWatts));
            return bytes;
        }

        private static void WriteUInt16(byte[] data, int offset, ushort value)
        {
            data[offset] = (byte)(value & 0xff);
            data[offset + 1] = (byte)(value >> 8);
        }

        public void Dispose() => connected = false;
    }
}
