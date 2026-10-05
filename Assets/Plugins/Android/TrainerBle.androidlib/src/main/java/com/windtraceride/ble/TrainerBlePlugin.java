package com.windtraceride.ble;

import android.Manifest;
import android.app.Activity;
import android.bluetooth.BluetoothAdapter;
import android.bluetooth.BluetoothDevice;
import android.bluetooth.BluetoothGatt;
import android.bluetooth.BluetoothGattCallback;
import android.bluetooth.BluetoothGattCharacteristic;
import android.bluetooth.BluetoothGattDescriptor;
import android.bluetooth.BluetoothGattService;
import android.bluetooth.BluetoothManager;
import android.bluetooth.BluetoothProfile;
import android.bluetooth.le.BluetoothLeScanner;
import android.bluetooth.le.ScanCallback;
import android.bluetooth.le.ScanResult;
import android.content.Context;
import android.content.pm.PackageManager;
import android.os.Build;
import android.os.Handler;
import android.os.Looper;
import android.os.ParcelUuid;
import android.util.Base64;
import android.util.Log;

import com.unity3d.player.UnityPlayer;

import org.json.JSONObject;

import java.util.HashMap;
import java.util.List;
import java.util.Locale;
import java.util.Map;
import java.util.UUID;

public final class TrainerBlePlugin {
    private static final String TAG = "WindTraceTrainerBle";
    private static final UUID FTMS_SERVICE = uuid16("1826");
    private static final UUID INDOOR_BIKE_DATA = uuid16("2AD2");
    private static final UUID FTMS_CONTROL_POINT = uuid16("2AD9");
    private static final UUID FTMS_STATUS = uuid16("2ADA");
    private static final UUID YESOUL_SERVICE = uuid16("FFF0");
    private static final UUID YESOUL_WRITE = uuid16("FFF1");
    private static final UUID YESOUL_NOTIFY = uuid16("FFF4");
    private static final UUID CLIENT_CONFIGURATION = uuid16("2902");
    private static final byte[] YESOUL_START = new byte[] {
        (byte) 0xF5, 0x20, 0x20, 0x40, (byte) 0xF6
    };

    private static final TrainerBlePlugin INSTANCE = new TrainerBlePlugin();

    private final Handler mainHandler = new Handler(Looper.getMainLooper());
    private final Map<String, String> advertisedProtocols = new HashMap<>();
    private Activity activity;
    private BluetoothAdapter adapter;
    private BluetoothLeScanner scanner;
    private BluetoothGatt gatt;
    private BluetoothGattCharacteristic pendingYesoulNotify;
    private BluetoothGattCharacteristic ftmsControlPoint;
    private BluetoothGattCharacteristic ftmsStatus;
    private String callbackObject = "WindTraceRideBleBridge";
    private String activeProtocol = "unknown";
    private boolean scanning;

    public static TrainerBlePlugin getInstance() {
        return INSTANCE;
    }

    public void initialize(String callbackGameObject) {
        callbackObject = callbackGameObject;
        activity = UnityPlayer.currentActivity;
        BluetoothManager manager = (BluetoothManager) activity.getSystemService(Context.BLUETOOTH_SERVICE);
        adapter = manager == null ? null : manager.getAdapter();
    }

    public void startScan(int timeoutMilliseconds) {
        if (activity == null || adapter == null) {
            sendError("Bluetooth is unavailable on this device.");
            return;
        }
        if (!hasRuntimePermissions()) {
            requestRuntimePermissions();
            send("permission_required", object("message", "Grant Nearby Devices permission, then scan again."));
            return;
        }
        if (!adapter.isEnabled()) {
            sendError("Turn on Bluetooth before scanning.");
            return;
        }

        stopScan();
        advertisedProtocols.clear();
        scanner = adapter.getBluetoothLeScanner();
        if (scanner == null) {
            sendError("Unable to start the Bluetooth scanner.");
            return;
        }

        scanning = true;
        scanner.startScan(scanCallback);
        sendState("Scanning", "Searching for FTMS and YESOUL trainers");
        mainHandler.postDelayed(scanTimeout, Math.max(3000, timeoutMilliseconds));
    }

    public void stopScan() {
        mainHandler.removeCallbacks(scanTimeout);
        if (!scanning || scanner == null || !hasRuntimePermissions()) return;
        try {
            scanner.stopScan(scanCallback);
        } catch (SecurityException ignored) {
        }
        scanning = false;
    }

    public void connect(String address) {
        if (!hasRuntimePermissions()) {
            requestRuntimePermissions();
            send("permission_required", object("message", "Grant Nearby Devices permission, then connect again."));
            return;
        }

        stopScan();
        disconnect();
        try {
            BluetoothDevice device = adapter.getRemoteDevice(address);
            activeProtocol = advertisedProtocols.containsKey(address)
                ? advertisedProtocols.get(address) : "unknown";
            sendState("Connecting", "Connecting to " + safeName(device));
            if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.M) {
                gatt = device.connectGatt(activity, false, gattCallback, BluetoothDevice.TRANSPORT_LE);
            } else {
                gatt = device.connectGatt(activity, false, gattCallback);
            }
        } catch (Exception exception) {
            sendError("Unable to connect: " + exception.getMessage());
        }
    }

    public void disconnect() {
        BluetoothGatt current = gatt;
        gatt = null;
        pendingYesoulNotify = null;
        ftmsControlPoint = null;
        ftmsStatus = null;
        if (current == null) return;
        try {
            if (hasRuntimePermissions()) current.disconnect();
        } catch (SecurityException ignored) {
        }
        current.close();
    }

    private final Runnable scanTimeout = new Runnable() {
        @Override public void run() {
            stopScan();
            sendState("Idle", "Scan finished");
        }
    };

    private final ScanCallback scanCallback = new ScanCallback() {
        @Override public void onScanResult(int callbackType, ScanResult result) {
            publishScanResult(result);
        }

        @Override public void onBatchScanResults(List<ScanResult> results) {
            for (ScanResult result : results) publishScanResult(result);
        }

        @Override public void onScanFailed(int errorCode) {
            scanning = false;
            sendError("Bluetooth scan failed with code " + errorCode + ".");
        }
    };

    private void publishScanResult(ScanResult result) {
        BluetoothDevice device = result.getDevice();
        String name = advertisedName(result);
        String protocol = detectProtocol(result, name);
        Log.d(TAG, "scan name=" + name + " address=" + device.getAddress()
            + " rssi=" + result.getRssi() + " protocol=" + protocol
            + " services=" + advertisedServices(result));
        if ("unknown".equals(protocol)) return;

        String address = device.getAddress();
        advertisedProtocols.put(address, protocol);
        JSONObject data = new JSONObject();
        put(data, "id", address);
        put(data, "name", name);
        put(data, "rssi", result.getRssi());
        put(data, "protocol", protocol);
        send("device", data);
    }

    private String detectProtocol(ScanResult result, String name) {
        if (result.getScanRecord() != null) {
            List<ParcelUuid> services = result.getScanRecord().getServiceUuids();
            if (services != null) {
                for (ParcelUuid service : services) {
                    if (FTMS_SERVICE.equals(service.getUuid())) return "ftms";
                }
                for (ParcelUuid service : services) {
                    if (YESOUL_SERVICE.equals(service.getUuid())) return "yesoul";
                }
            }
        }
        String normalized = name.toUpperCase(Locale.ROOT);
        if (normalized.startsWith("YESOUL") || normalized.startsWith("YS_")) return "yesoul";
        return "unknown";
    }

    private String advertisedName(ScanResult result) {
        if (result.getScanRecord() != null) {
            String localName = result.getScanRecord().getDeviceName();
            if (localName != null && !localName.trim().isEmpty()) return localName;
        }
        return safeName(result.getDevice());
    }

    private String advertisedServices(ScanResult result) {
        if (result.getScanRecord() == null || result.getScanRecord().getServiceUuids() == null)
            return "[]";
        return result.getScanRecord().getServiceUuids().toString();
    }

    private final BluetoothGattCallback gattCallback = new BluetoothGattCallback() {
        @Override public void onConnectionStateChange(BluetoothGatt bluetoothGatt, int status, int newState) {
            if (gatt != bluetoothGatt) {
                bluetoothGatt.close();
                return;
            }
            if (newState == BluetoothProfile.STATE_CONNECTED && status == BluetoothGatt.GATT_SUCCESS) {
                sendState("Connecting", "Discovering trainer services");
                try {
                    bluetoothGatt.discoverServices();
                } catch (SecurityException exception) {
                    sendError("Bluetooth permission was revoked.");
                }
                return;
            }

            if (newState == BluetoothProfile.STATE_DISCONNECTED) {
                if (gatt == bluetoothGatt) gatt = null;
                bluetoothGatt.close();
                sendError("Trainer disconnected (GATT status " + status + "). Please scan and connect again.");
            } else if (status != BluetoothGatt.GATT_SUCCESS) {
                sendError("Trainer connection failed (GATT status " + status + ").");
            }
        }

        @Override public void onServicesDiscovered(BluetoothGatt bluetoothGatt, int status) {
            if (gatt != bluetoothGatt) return;
            if (status != BluetoothGatt.GATT_SUCCESS) {
                sendError("Trainer service discovery failed with status " + status + ".");
                return;
            }

            BluetoothGattService ftms = bluetoothGatt.getService(FTMS_SERVICE);
            if (ftms != null && subscribe(bluetoothGatt, ftms.getCharacteristic(INDOOR_BIKE_DATA))) {
                activeProtocol = "ftms";
                ftmsControlPoint = ftms.getCharacteristic(FTMS_CONTROL_POINT);
                ftmsStatus = ftms.getCharacteristic(FTMS_STATUS);
                Log.i(TAG, "FTMS bike-data subscription queued; controlPoint="
                    + (ftmsControlPoint != null) + " status=" + (ftmsStatus != null));
                return;
            }

            BluetoothGattService yesoul = bluetoothGatt.getService(YESOUL_SERVICE);
            if (yesoul != null) {
                BluetoothGattCharacteristic start = yesoul.getCharacteristic(YESOUL_WRITE);
                pendingYesoulNotify = yesoul.getCharacteristic(YESOUL_NOTIFY);
                activeProtocol = "yesoul";
                // Match OpenBike's S1 sequence: write FFF1, wait for its GATT
                // callback, then enable FFF4 notifications. Some controllers
                // never begin publishing when the subscription comes first.
                if (pendingYesoulNotify == null) {
                    sendError("YESOUL ride-data characteristic FFF4 is missing.");
                    return;
                }
                if (!writeYesoulStart(bluetoothGatt, start)) pendingYesoulNotify = null;
                return;
            }

            sendError("The device does not expose FTMS or a supported YESOUL service.");
        }

        @Override public void onCharacteristicWrite(
            BluetoothGatt bluetoothGatt,
            BluetoothGattCharacteristic characteristic,
            int status) {
            if (!YESOUL_WRITE.equals(characteristic.getUuid()) || gatt != bluetoothGatt) return;
            if (status != BluetoothGatt.GATT_SUCCESS) {
                pendingYesoulNotify = null;
                sendError("Unable to start YESOUL telemetry (write status " + status + ").");
                return;
            }
            BluetoothGattCharacteristic notify = pendingYesoulNotify;
            pendingYesoulNotify = null;
            if (!subscribe(bluetoothGatt, notify))
                sendError("Unable to subscribe to YESOUL ride data (FFF4).");
        }

        @Override public void onDescriptorWrite(
            BluetoothGatt bluetoothGatt,
            BluetoothGattDescriptor descriptor,
            int status) {
            if (gatt != bluetoothGatt) return;
            if (status != BluetoothGatt.GATT_SUCCESS) {
                UUID failedCharacteristic = descriptor.getCharacteristic().getUuid();
                if (INDOOR_BIKE_DATA.equals(failedCharacteristic) || YESOUL_NOTIFY.equals(failedCharacteristic))
                    sendError("Unable to subscribe to trainer telemetry (status " + status + ").");
                else if (FTMS_STATUS.equals(failedCharacteristic))
                    requestFtmsControl(bluetoothGatt);
                else
                    Log.w(TAG, "FTMS control subscription failed; waiting for read-only bike data");
                return;
            }
            UUID characteristic = descriptor.getCharacteristic().getUuid();
            if (YESOUL_NOTIFY.equals(characteristic)) {
                sendState("Connecting", "YESOUL data subscribed; pedal to confirm telemetry");
            } else if (INDOOR_BIKE_DATA.equals(characteristic)) {
                sendState("Connecting", "FTMS data subscribed; completing trainer initialization");
                if (ftmsControlPoint != null && subscribe(bluetoothGatt, ftmsControlPoint)) return;
                Log.w(TAG, "FTMS control point unavailable; waiting for read-only bike data");
            } else if (FTMS_CONTROL_POINT.equals(characteristic)) {
                if (ftmsStatus != null && subscribe(bluetoothGatt, ftmsStatus)) return;
                requestFtmsControl(bluetoothGatt);
            } else if (FTMS_STATUS.equals(characteristic)) {
                requestFtmsControl(bluetoothGatt);
            }
        }

        @SuppressWarnings("deprecation")
        @Override public void onCharacteristicChanged(BluetoothGatt bluetoothGatt, BluetoothGattCharacteristic characteristic) {
            handleNotification(bluetoothGatt, characteristic, characteristic.getValue());
        }

        @Override public void onCharacteristicChanged(
            BluetoothGatt bluetoothGatt,
            BluetoothGattCharacteristic characteristic,
            byte[] value) {
            handleNotification(bluetoothGatt, characteristic, value);
        }
    };

    private void handleNotification(BluetoothGatt bluetoothGatt,
                                    BluetoothGattCharacteristic characteristic, byte[] value) {
        if (gatt != bluetoothGatt || value == null) return;
        UUID uuid = characteristic.getUuid();
        if (FTMS_CONTROL_POINT.equals(uuid)) {
            if (value.length >= 3 && (value[0] & 0xFF) == 0x80) {
                int operation = value[1] & 0xFF;
                int result = value[2] & 0xFF;
                Log.i(TAG, "FTMS control response op=" + operation + " result=" + result);
                if (operation == 0x00 && result == 0x01)
                    writeFtmsControl(bluetoothGatt, (byte) 0x07);
            }
            return;
        }
        if (INDOOR_BIKE_DATA.equals(uuid) || YESOUL_NOTIFY.equals(uuid)) {
            Log.i(TAG, "Ride-data notification protocol=" + activeProtocol + " bytes=" + value.length);
            publishPacket(value);
        }
    }

    private void requestFtmsControl(BluetoothGatt bluetoothGatt) {
        if (!writeFtmsControl(bluetoothGatt, (byte) 0x00))
            Log.w(TAG, "FTMS Request Control unavailable; waiting for read-only bike data");
    }

    @SuppressWarnings("deprecation")
    private boolean writeFtmsControl(BluetoothGatt bluetoothGatt, byte operation) {
        if (ftmsControlPoint == null) return false;
        try {
            if (Build.VERSION.SDK_INT >= 33) {
                return bluetoothGatt.writeCharacteristic(ftmsControlPoint,
                    new byte[] { operation }, BluetoothGattCharacteristic.WRITE_TYPE_DEFAULT)
                    == android.bluetooth.BluetoothStatusCodes.SUCCESS;
            }
            ftmsControlPoint.setWriteType(BluetoothGattCharacteristic.WRITE_TYPE_DEFAULT);
            ftmsControlPoint.setValue(new byte[] { operation });
            return bluetoothGatt.writeCharacteristic(ftmsControlPoint);
        } catch (SecurityException exception) {
            sendError("Bluetooth permission was revoked during FTMS initialization.");
            return false;
        }
    }

    @SuppressWarnings("deprecation")
    private boolean subscribe(BluetoothGatt bluetoothGatt, BluetoothGattCharacteristic characteristic) {
        if (characteristic == null) return false;
        try {
            int properties = characteristic.getProperties();
            byte[] enableValue;
            if ((properties & BluetoothGattCharacteristic.PROPERTY_NOTIFY) != 0)
                enableValue = BluetoothGattDescriptor.ENABLE_NOTIFICATION_VALUE;
            else if ((properties & BluetoothGattCharacteristic.PROPERTY_INDICATE) != 0)
                enableValue = BluetoothGattDescriptor.ENABLE_INDICATION_VALUE;
            else return false;
            if (!bluetoothGatt.setCharacteristicNotification(characteristic, true)) return false;
            BluetoothGattDescriptor descriptor = characteristic.getDescriptor(CLIENT_CONFIGURATION);
            if (descriptor == null) return false;
            if (Build.VERSION.SDK_INT >= 33) {
                return bluetoothGatt.writeDescriptor(descriptor, enableValue)
                    == android.bluetooth.BluetoothStatusCodes.SUCCESS;
            }
            descriptor.setValue(enableValue);
            return bluetoothGatt.writeDescriptor(descriptor);
        } catch (SecurityException exception) {
            sendError("Bluetooth permission was revoked.");
            return false;
        }
    }

    @SuppressWarnings("deprecation")
    private boolean writeYesoulStart(BluetoothGatt bluetoothGatt, BluetoothGattCharacteristic characteristic) {
        if (characteristic == null) {
            sendError("YESOUL start characteristic FFF1 is missing.");
            return false;
        }
        try {
            if (Build.VERSION.SDK_INT >= 33) {
                int result = bluetoothGatt.writeCharacteristic(
                    characteristic,
                    YESOUL_START,
                    BluetoothGattCharacteristic.WRITE_TYPE_DEFAULT);
                if (result != android.bluetooth.BluetoothStatusCodes.SUCCESS) {
                    sendError("Unable to start YESOUL telemetry (write request " + result + ").");
                    return false;
                }
            } else {
                characteristic.setWriteType(BluetoothGattCharacteristic.WRITE_TYPE_DEFAULT);
                characteristic.setValue(YESOUL_START);
                if (!bluetoothGatt.writeCharacteristic(characteristic)) {
                    sendError("Unable to queue YESOUL start command.");
                    return false;
                }
            }
            Log.i(TAG, "YESOUL FFF1 start command queued; waiting for write callback");
            return true;
        } catch (SecurityException exception) {
            sendError("Unable to start YESOUL telemetry: permission denied.");
            return false;
        }
    }

    private void publishPacket(byte[] bytes) {
        if (bytes == null || bytes.length == 0) return;
        JSONObject data = new JSONObject();
        put(data, "protocol", activeProtocol);
        put(data, "payload", Base64.encodeToString(bytes, Base64.NO_WRAP));
        send("packet", data);
    }

    private boolean hasRuntimePermissions() {
        if (activity == null) return false;
        if (Build.VERSION.SDK_INT >= 31) {
            return activity.checkSelfPermission(Manifest.permission.BLUETOOTH_SCAN) == PackageManager.PERMISSION_GRANTED
                && activity.checkSelfPermission(Manifest.permission.BLUETOOTH_CONNECT) == PackageManager.PERMISSION_GRANTED;
        }
        return Build.VERSION.SDK_INT < 23
            || activity.checkSelfPermission(Manifest.permission.ACCESS_FINE_LOCATION) == PackageManager.PERMISSION_GRANTED;
    }

    private void requestRuntimePermissions() {
        if (activity == null || Build.VERSION.SDK_INT < 23) return;
        activity.runOnUiThread(new Runnable() {
            @Override public void run() {
                if (Build.VERSION.SDK_INT >= 31) {
                    activity.requestPermissions(new String[] {
                        Manifest.permission.BLUETOOTH_SCAN,
                        Manifest.permission.BLUETOOTH_CONNECT
                    }, 4107);
                } else {
                    activity.requestPermissions(new String[] {
                        Manifest.permission.ACCESS_FINE_LOCATION
                    }, 4107);
                }
            }
        });
    }

    private String safeName(BluetoothDevice device) {
        try {
            String name = device.getName();
            return name == null || name.trim().isEmpty() ? "Unnamed trainer" : name;
        } catch (SecurityException exception) {
            return "Trainer";
        }
    }

    private void sendState(String state, String message) {
        JSONObject data = new JSONObject();
        put(data, "state", state);
        put(data, "message", message);
        send("state", data);
    }

    private void sendError(String message) {
        send("error", object("message", message));
    }

    private void send(String type, JSONObject data) {
        put(data, "type", type);
        UnityPlayer.UnitySendMessage(callbackObject, "OnNativeBleEvent", data.toString());
    }

    private static JSONObject object(String key, Object value) {
        JSONObject result = new JSONObject();
        put(result, key, value);
        return result;
    }

    private static void put(JSONObject target, String key, Object value) {
        try {
            target.put(key, value);
        } catch (Exception ignored) {
        }
    }

    private static UUID uuid16(String value) {
        return UUID.fromString("0000" + value.toLowerCase(Locale.ROOT) + "-0000-1000-8000-00805f9b34fb");
    }

}
