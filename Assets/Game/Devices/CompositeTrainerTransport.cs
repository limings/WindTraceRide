using System;
using System.Collections.Generic;
using WindTraceRide.Core;

namespace WindTraceRide.Devices
{
    public sealed class CompositeTrainerTransport : ITrainerTransport
    {
        private readonly IReadOnlyList<ITrainerTransport> transports;
        private readonly Dictionary<ITrainerTransport, Action<TrainerAdvertisement>> deviceHandlers = new Dictionary<ITrainerTransport, Action<TrainerAdvertisement>>();
        private readonly Dictionary<ITrainerTransport, Action<TrainerConnectionState, string>> stateHandlers = new Dictionary<ITrainerTransport, Action<TrainerConnectionState, string>>();
        private readonly Dictionary<ITrainerTransport, Action<RawTrainerPacket>> packetHandlers = new Dictionary<ITrainerTransport, Action<RawTrainerPacket>>();
        private readonly Dictionary<ITrainerTransport, Action<string>> errorHandlers = new Dictionary<ITrainerTransport, Action<string>>();
        private ITrainerTransport activeTransport;

        public event Action<TrainerAdvertisement> DeviceFound;
        public event Action<TrainerConnectionState, string> StateChanged;
        public event Action<RawTrainerPacket> PacketReceived;
        public event Action<string> Error;

        public CompositeTrainerTransport(params ITrainerTransport[] transports)
        {
            this.transports = transports ?? throw new ArgumentNullException(nameof(transports));
            foreach (var transport in transports)
            {
                Action<TrainerAdvertisement> deviceHandler = value => DeviceFound?.Invoke(value);
                Action<TrainerConnectionState, string> stateHandler = (state, message) => ForwardState(transport, state, message);
                Action<RawTrainerPacket> packetHandler = packet => ForwardPacket(transport, packet);
                Action<string> errorHandler = message => ForwardError(transport, message);
                deviceHandlers[transport] = deviceHandler;
                stateHandlers[transport] = stateHandler;
                packetHandlers[transport] = packetHandler;
                errorHandlers[transport] = errorHandler;
                transport.DeviceFound += deviceHandler;
                transport.StateChanged += stateHandler;
                transport.PacketReceived += packetHandler;
                transport.Error += errorHandler;
            }
        }

        public void StartScan(int timeoutMilliseconds)
        {
            activeTransport = null;
            foreach (var transport in transports) transport.StartScan(timeoutMilliseconds);
        }

        public void StopScan()
        {
            foreach (var transport in transports) transport.StopScan();
        }

        public void Connect(string deviceId)
        {
            var wantsSimulator = deviceId != null && deviceId.StartsWith("simulator://", StringComparison.OrdinalIgnoreCase);
            activeTransport = null;
            foreach (var candidate in transports)
            {
                if ((candidate is SimulatedTrainerTransport) == wantsSimulator)
                {
                    activeTransport = candidate;
                    break;
                }
            }

            if (activeTransport == null)
            {
                Error?.Invoke("No transport is available for this trainer.");
                return;
            }

            activeTransport.Connect(deviceId);
        }

        public void Disconnect()
        {
            if (activeTransport != null)
            {
                activeTransport.Disconnect();
                activeTransport = null;
                return;
            }
            foreach (var transport in transports) transport.Disconnect();
        }

        private void ForwardState(ITrainerTransport source, TrainerConnectionState state, string message)
        {
            if (activeTransport == null || ReferenceEquals(source, activeTransport))
                StateChanged?.Invoke(state, message);
        }

        private void ForwardPacket(ITrainerTransport source, RawTrainerPacket packet)
        {
            if (activeTransport == null || ReferenceEquals(source, activeTransport))
                PacketReceived?.Invoke(packet);
        }

        private void ForwardError(ITrainerTransport source, string message)
        {
            if (activeTransport == null || ReferenceEquals(source, activeTransport))
                Error?.Invoke(message);
        }

        public void Dispose()
        {
            foreach (var transport in transports)
            {
                transport.DeviceFound -= deviceHandlers[transport];
                transport.StateChanged -= stateHandlers[transport];
                transport.PacketReceived -= packetHandlers[transport];
                transport.Error -= errorHandlers[transport];
                transport.Dispose();
            }
        }
    }
}
