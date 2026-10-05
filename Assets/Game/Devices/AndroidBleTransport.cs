using System;
using UnityEngine;
using WindTraceRide.Core;

namespace WindTraceRide.Devices
{
    public sealed class AndroidBleTransport : ITrainerTransport
    {
        [Serializable]
        private sealed class NativeBleEvent
        {
            public string type;
            public string id;
            public string name;
            public string protocol;
            public string state;
            public string message;
            public string payload;
            public int rssi;
        }

        private AndroidJavaObject plugin;

        public event Action<TrainerAdvertisement> DeviceFound;
        public event Action<TrainerConnectionState, string> StateChanged;
        public event Action<RawTrainerPacket> PacketReceived;
        public event Action<string> Error;

        public AndroidBleTransport(string callbackGameObject)
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            using var pluginClass = new AndroidJavaClass("com.windtraceride.ble.TrainerBlePlugin");
            plugin = pluginClass.CallStatic<AndroidJavaObject>("getInstance");
            plugin.Call("initialize", callbackGameObject);
#else
            throw new PlatformNotSupportedException("Android BLE transport requires an Android player build.");
#endif
        }

        public void StartScan(int timeoutMilliseconds)
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            plugin.Call("startScan", timeoutMilliseconds);
#endif
        }

        public void StopScan()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            plugin.Call("stopScan");
#endif
        }

        public void Connect(string deviceId)
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            plugin.Call("connect", deviceId);
#endif
        }

        public void Disconnect()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            plugin.Call("disconnect");
#endif
        }

        public void HandleNativeEvent(string json)
        {
            if (string.IsNullOrWhiteSpace(json)) return;
            NativeBleEvent nativeEvent;
            try
            {
                nativeEvent = JsonUtility.FromJson<NativeBleEvent>(json);
            }
            catch (Exception exception)
            {
                Error?.Invoke($"Invalid native BLE event: {exception.Message}");
                return;
            }

            switch (nativeEvent.type)
            {
                case "device":
                    DeviceFound?.Invoke(new TrainerAdvertisement(
                        nativeEvent.id,
                        nativeEvent.name,
                        nativeEvent.rssi,
                        ParseProtocol(nativeEvent.protocol)));
                    break;
                case "state":
                    StateChanged?.Invoke(ParseState(nativeEvent.state), nativeEvent.message);
                    break;
                case "packet":
                    try
                    {
                        PacketReceived?.Invoke(new RawTrainerPacket(
                            ParseProtocol(nativeEvent.protocol),
                            Convert.FromBase64String(nativeEvent.payload ?? string.Empty)));
                    }
                    catch (FormatException exception)
                    {
                        Error?.Invoke($"Invalid BLE packet: {exception.Message}");
                    }
                    break;
                case "permission_required":
                    StateChanged?.Invoke(TrainerConnectionState.PermissionRequired, nativeEvent.message);
                    break;
                case "error":
                    Error?.Invoke(nativeEvent.message ?? "Bluetooth error");
                    break;
            }
        }

        private static TrainerProtocol ParseProtocol(string value)
        {
            if (string.Equals(value, "ftms", StringComparison.OrdinalIgnoreCase)) return TrainerProtocol.Ftms;
            if (string.Equals(value, "yesoul", StringComparison.OrdinalIgnoreCase)) return TrainerProtocol.YesoulLegacy;
            return TrainerProtocol.Unknown;
        }

        private static TrainerConnectionState ParseState(string value)
        {
            if (Enum.TryParse(value, true, out TrainerConnectionState state)) return state;
            return TrainerConnectionState.Failed;
        }

        public void Dispose()
        {
            Disconnect();
            plugin?.Dispose();
            plugin = null;
        }
    }
}

