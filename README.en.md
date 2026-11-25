# Wind Trace Ride

[简体中文](README.md) · English

Wind Trace Ride is an indoor cycling adventure for Android. Connect a Bluetooth exercise bike and your cadence moves the rider through lakeside trails, a wind canyon and a windmill village. Collect wind energy to boost and match target cadences to build combos.

**[Download the Android preview APK](https://github.com/limings/WindTraceRide/releases/download/v1.0.0-preview.1/WindTraceRide-1.0.0-preview.1.apk)** · [18-second gameplay clip](https://github.com/limings/WindTraceRide/releases/download/v1.0.0-preview.1/WindTraceRide-demo.mp4) · [Release notes](https://github.com/limings/WindTraceRide/releases/tag/v1.0.0-preview.1) · [Report bike compatibility](https://github.com/limings/WindTraceRide/issues/new?template=bike-compatibility.md)

![Riding along Tahoe Lakeside with cadence, power and route progress on screen](docs/screenshots/tahoe-lakeside.png)

## Play on Android

The APK supports ARM64 phones and tablets running Android 6.0 or newer. The game has English and Chinese interfaces.

1. Install the APK and open the game. Choose **English** in the trainer settings to switch the interface.
2. Choose **Start demo · simulated ride** to play without a bike. To use your bike, wake it, choose **Scan for trainers**, allow Bluetooth access and connect.
3. Choose a course from the home screen.

The game reads Bluetooth FTMS Indoor Bike Data and legacy `0xFFF0` telemetry from the YESOUL S1. Available readings depend on the bike and firmware. Adjust resistance on the bike. If you try a real bike, please share the model, connection result and how closely the on-screen cadence follows your pedaling. [Compatibility feedback](https://github.com/limings/WindTraceRide/issues/new?template=bike-compatibility.md)

## Run from source

Open the repository in Unity **6000.0.60f1**, load `Assets/Scenes/Bootstrap.unity` and press Play. Demo mode also works in the editor. Install Unity's Android Build Support to build an APK. [Build instructions and project structure](README.md#构建-android)

Wind Trace Ride is source-available and free for personal, noncommercial use. Commercial use requires written authorization. [License](LICENSE)
