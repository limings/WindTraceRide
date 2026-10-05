using System;
using System.Collections.Generic;
using UnityEngine;
using WindTraceRide.Core;
using WindTraceRide.Devices;

namespace WindTraceRide.Bootstrap
{
    public sealed class WindTraceRideBootstrap : MonoBehaviour
    {
        private const string CallbackObjectName = "WindTraceRideBleBridge";
        private readonly List<TrainerAdvertisement> devices = new List<TrainerAdvertisement>();
        private TrainerConnectionCoordinator coordinator;
        private ITrainerTransport transport;
        private AndroidBleTransport androidTransport;
        private SimulatedTrainerTransport simulatorTransport;
        private SimulatedTrainerTransport demoTransport;
        private readonly FtmsIndoorBikeDataDecoder demoDecoder = new FtmsIndoorBikeDataDecoder();
        private bool demoMode;
        private TrainerTelemetry telemetry;
        private readonly TelemetryFreshnessMonitor freshness = new TelemetryFreshnessMonitor(TimeSpan.FromSeconds(6));
        private RideSessionEngine rideSession;
        private RideSessionSnapshot rideSnapshot;
        private RideWorldController rideWorld;
        private AudioSource rideMusic;
        private readonly string[] levelMusicPaths =
        {
            "Audio/Music/TahoeForest",
            "Audio/Music/WindCanyon",
            "Audio/Music/CloudVillage"
        };
        private readonly float[] levelMusicVolumes = { .8f, .45f, .22f };
        private string statusMessage = "Starting";
        private bool showSettings;
        private bool inRide;
        private bool showSummary;
        private bool showRideSettings;
        private const string LanguagePreferenceKey = "ui.language";
        private bool english;
        private int ftpWatts;
        private int selectedLevel;
        private readonly Texture2D[] levelArtwork = new Texture2D[3];
        private Texture2D boostShoeIcon;
        private Texture2D riderBadgeIcon;
        private Texture2D boostDialTexture;
        private Texture2D routeGradientTexture;
        private Texture2D finishFlagTexture;
        private Texture2D riderMarkerTexture;
        private Texture2D hudPanelTexture;
        private Texture2D hudStageIcon;
        private Texture2D hudExitIcon;
        private GUIStyle hudPanelStyle;
        private int cachedBoostDialPercent = -1;
        private string windFeedback;
        private float windFeedbackUntil;
        private float boostFlashUntil;
        private Vector2 scroll;
        private GUISkin runtimeSkin;
        private int previousSleepTimeout;
        private bool screenWakeOverridden;
        private bool appPaused;
        private bool appFocused = true;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Create()
        {
            if (FindFirstObjectByType<WindTraceRideBootstrap>() != null) return;
            var root = new GameObject(CallbackObjectName);
            DontDestroyOnLoad(root);
            root.AddComponent<WindTraceRideBootstrap>();
        }

        private void Awake()
        {
            gameObject.name = CallbackObjectName;
            previousSleepTimeout = Screen.sleepTimeout;
            UpdateScreenWake();
            runtimeSkin = Resources.GetBuiltinResource<GUISkin>("GameSkin/GameSkin.guiskin");
            rideMusic = gameObject.AddComponent<AudioSource>();
            rideMusic.playOnAwake = false;
            rideMusic.loop = true;
            rideMusic.spatialBlend = 0f;
            rideMusic.volume = .22f;
            EnableImmersiveFullscreen();
#if UNITY_ANDROID && !UNITY_EDITOR
            Application.targetFrameRate=60;
            QualitySettings.vSyncCount=0;
            QualitySettings.antiAliasing=2;
            QualitySettings.shadowDistance=65f;
            QualitySettings.shadowCascades=2;
            QualitySettings.lodBias=.85f;
            // 1920px landscape keeps readable tablet UI while bounding fill cost.
            var longest=Mathf.Max(Screen.width,Screen.height);
            if(longest>1920)Screen.SetResolution(Mathf.RoundToInt(Screen.width*1920f/longest),Mathf.RoundToInt(Screen.height*1920f/longest),FullScreenMode.FullScreenWindow);
#endif
#if UNITY_ANDROID && !UNITY_EDITOR
            androidTransport = new AndroidBleTransport(CallbackObjectName);
            transport = androidTransport;
#else
            simulatorTransport = new SimulatedTrainerTransport();
            transport = simulatorTransport;
#endif
            var trainerSettings = new PlayerPrefsTrainerSettingsStore();
#if UNITY_ANDROID && !UNITY_EDITOR
            // Earlier development builds exposed a simulator on real phones.
            // Remove only that saved mock pairing; preserve real S1 pairings.
            var savedTrainer = trainerSettings.Load();
            if (savedTrainer != null && savedTrainer.Id.StartsWith("simulator://", StringComparison.OrdinalIgnoreCase))
                trainerSettings.Clear();
#endif
            coordinator = new TrainerConnectionCoordinator(
                transport,
                trainerSettings,
                new FtmsIndoorBikeDataDecoder(),
                new YesoulS1Decoder());
            coordinator.StateChanged += OnStateChanged;
            coordinator.DeviceFound += OnDeviceFound;
            coordinator.TelemetryReceived += OnTelemetryReceived;
            coordinator.Error += message => statusMessage = message;
            ftpWatts = Mathf.Clamp(PlayerPrefs.GetInt("fitness.ftp", 150), 50, 500);
            english = PlayerPrefs.GetInt(LanguagePreferenceKey, 0) == 1;
            selectedLevel = Mathf.Clamp(PlayerPrefs.GetInt("ride.level", 0), 0, 2);
            levelArtwork[0] = Resources.Load<Texture2D>("UI/Levels/LakesideTrail");
            levelArtwork[1] = Resources.Load<Texture2D>("UI/Levels/WindCanyon");
            levelArtwork[2] = Resources.Load<Texture2D>("UI/Levels/CloudVillage");
            boostShoeIcon = Resources.Load<Texture2D>("UI/Hud/BoostShoe");
            riderBadgeIcon = Resources.Load<Texture2D>("UI/Hud/RiderBadge");
            routeGradientTexture = CreateRouteGradientTexture();
            finishFlagTexture = CreateFinishFlagTexture();
            riderMarkerTexture = CreateRiderMarkerTexture();
            hudPanelTexture = CreateRoundedPanelTexture();
            hudStageIcon = CreateHudIconTexture(false);
            hudExitIcon = CreateHudIconTexture(true);
            hudPanelStyle = new GUIStyle { normal = { background = hudPanelTexture }, border = new RectOffset(16, 16, 16, 16) };
        }

        private void Start() => coordinator.StartAutoConnect();

        private void OnApplicationPause(bool paused)
        {
            appPaused = paused;
            UpdateScreenWake();
        }

        private void OnApplicationFocus(bool focused)
        {
            appFocused = focused;
            UpdateScreenWake();
        }

        private void UpdateScreenWake()
        {
            if (!appPaused && appFocused)
            {
                Screen.sleepTimeout = SleepTimeout.NeverSleep;
                screenWakeOverridden = true;
            }
            else if (screenWakeOverridden)
            {
                Screen.sleepTimeout = previousSleepTimeout;
                screenWakeOverridden = false;
            }
        }

        private void Update()
        {
            coordinator?.Tick(DateTimeOffset.UtcNow);
            simulatorTransport?.Tick(Time.unscaledDeltaTime);
            demoTransport?.Tick(Time.unscaledDeltaTime);
            if (!inRide || rideSession == null || showRideSettings) return;

            var signalFresh = (demoMode || coordinator.State == TrainerConnectionState.Connected) &&
                              freshness.IsFresh(DateTimeOffset.UtcNow);
            rideSnapshot = rideSession.Tick(signalFresh ? Time.unscaledDeltaTime : 0f, signalFresh ? telemetry : null);
            rideWorld?.Apply(rideSnapshot, signalFresh ? telemetry : null);
            if (rideWorld != null && rideWorld.ReachedFinish)
            {
                rideSession.CompleteRoute();
                rideSnapshot = rideSession.Current;
                FinishRide();
                return;
            }
            // Let the world camera reach the gate before swapping to the summary.
            if (rideSnapshot.IsComplete && rideWorld == null) FinishRide();
        }

        public void OnNativeBleEvent(string json) => androidTransport?.HandleNativeEvent(json);

        private void OnTelemetryReceived(TrainerTelemetry value)
        {
            if (demoMode) return;
            telemetry = value;
            freshness.Observe(value);
        }

        private void OnDemoPacket(RawTrainerPacket packet)
        {
            if (!demoMode || packet == null ||
                !demoDecoder.TryDecode(packet.Payload, DateTimeOffset.UtcNow, out var value)) return;
            telemetry = value;
            freshness.Observe(value);
        }

        private void StartDemoMode()
        {
            if (demoMode) return;
            demoTransport = new SimulatedTrainerTransport();
            demoTransport.PacketReceived += OnDemoPacket;
            demoMode = true;
            telemetry = null;
            demoTransport.Connect(SimulatedTrainerTransport.SimulatorId);
            showSettings = false;
            statusMessage = T("体验模式：模拟数据，不代表设备已连接", "Demo mode: simulated data, no trainer connected");
        }

        private void StopDemoMode()
        {
            if (!demoMode) return;
            demoMode = false;
            demoTransport.PacketReceived -= OnDemoPacket;
            demoTransport.Disconnect();
            demoTransport.Dispose();
            demoTransport = null;
            telemetry = null;
            statusMessage = T("体验模式已退出", "Demo mode stopped");
        }

        private void OnStateChanged(TrainerConnectionState state, string message)
        {
            statusMessage = $"{state}: {message}";
            if (!demoMode && (state == TrainerConnectionState.NeedsPairing ||
                              state == TrainerConnectionState.PermissionRequired))
                showSettings = true;
        }

        private void OnDeviceFound(TrainerAdvertisement advertisement)
        {
            var existing = devices.FindIndex(item => item.Id == advertisement.Id);
            if (existing >= 0) devices[existing] = advertisement;
            else devices.Add(advertisement);
        }

        private void OnGUI()
        {
            // Runtime-created MonoBehaviours can receive an early Android OnGUI pass
            // before Unity assigns the implicit skin. Keep a direct built-in resource
            // reference so engine stripping cannot remove it, and skip only that pass.
            if (GUI.skin == null)
            {
                if (runtimeSkin == null)
                    runtimeSkin = Resources.GetBuiltinResource<GUISkin>("GameSkin/GameSkin.guiskin");
                if (runtimeSkin == null) return;
                GUI.skin = runtimeSkin;
            }

            var scale = Mathf.Min(Screen.width / 1280f, Screen.height / 720f);
            var virtualWidth = Screen.width / scale;
            var virtualHeight = Screen.height / scale;
            var contentY = Mathf.Max(0f, (virtualHeight - 720f) * .5f);
            GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1f));
            if (!inRide && !showSummary) DrawBackground(virtualWidth, virtualHeight);

            if (inRide)
            {
                GUI.BeginGroup(new Rect(0f, contentY, virtualWidth, 720f));
                DrawRideHud(virtualWidth);
                if (showRideSettings) DrawRideSettings(virtualWidth);
                GUI.EndGroup();
                return;
            }
            GUI.BeginGroup(new Rect(0f, contentY, virtualWidth, 720f));
            DrawHeader(virtualWidth);
            if (showSummary) DrawSummary(virtualWidth);
            else if (showSettings) DrawSettings(virtualWidth);
            else DrawHome(virtualWidth);
            GUI.EndGroup();
        }

        private static void DrawBackground(float width, float height)
        {
            var previous = GUI.color;
            GUI.color = new Color(0.035f, 0.06f, 0.09f, 1f);
            GUI.DrawTexture(new Rect(0, 0, width, height), Texture2D.whiteTexture);
            GUI.color = previous;
        }

        private string T(string chinese, string englishText) => english ? englishText : chinese;

        private void SaveLanguage(bool useEnglish)
        {
            english = useEnglish;
            PlayerPrefs.SetInt(LanguagePreferenceKey, useEnglish ? 1 : 0);
            PlayerPrefs.Save();
        }

        private static void EnableImmersiveFullscreen()
        {
            Screen.fullScreenMode = FullScreenMode.FullScreenWindow;
            Screen.fullScreen = true;
#if UNITY_ANDROID && !UNITY_EDITOR
            using (var unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
            using (var activity = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity"))
            {
                activity.Call("runOnUiThread", new AndroidJavaRunnable(() =>
                {
                    using (var uiUnityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
                    using (var uiActivity = uiUnityPlayer.GetStatic<AndroidJavaObject>("currentActivity"))
                    using (var window = uiActivity?.Call<AndroidJavaObject>("getWindow"))
                    using (var decor = window?.Call<AndroidJavaObject>("getDecorView"))
                    {
                        // layout stable/fullscreen/hide navigation + immersive sticky
                        decor?.Call("setSystemUiVisibility", 5894);
                    }
                }));
            }
#endif
        }

        private void DrawHeader(float width)
        {
            var title = new GUIStyle(GUI.skin.label) { fontSize = 34, fontStyle = FontStyle.Bold };
            title.normal.textColor = new Color(0.78f, 0.95f, 1f);
            GUI.Label(new Rect(56, 35, 500, 50), "WIND TRACE RIDE", title);

            var statusStyle = new GUIStyle(GUI.skin.box) { fontSize = 17, alignment = TextAnchor.MiddleCenter };
            GUI.Box(new Rect(width - 500, 35, 330, 45), ConnectionStatus(), statusStyle);
            if (GUI.Button(new Rect(width - 155, 35, 100, 45), showSettings || showSummary ? T("主页", "Home") : T("设置", "Settings")))
            {
                showSummary = false;
                showSettings = !showSettings;
            }
        }

        private string ChineseStatus()
        {
            if (demoMode) return "体验模式 · 模拟数据";
            if (coordinator == null) return "正在启动";
            switch (coordinator.State)
            {
                case TrainerConnectionState.NeedsPairing: return "请在设置中连接设备";
                case TrainerConnectionState.Scanning: return "正在扫描附近设备";
                case TrainerConnectionState.Connecting: return "正在初始化，等待踩踏数据";
                case TrainerConnectionState.Connected: return "设备已连接";
                case TrainerConnectionState.Reconnecting: return "正在重新连接";
                case TrainerConnectionState.PermissionRequired: return "需要蓝牙权限";
                case TrainerConnectionState.Failed: return "连接失败，请重试";
                default: return "设备未连接";
            }
        }

        private string ConnectionStatus()
        {
            if (demoMode) return T("体验模式 · 模拟数据", "DEMO · SIMULATED DATA");
            if (!english) return ChineseStatus();
            if (coordinator == null) return "Starting";
            switch (coordinator.State)
            {
                case TrainerConnectionState.NeedsPairing: return "Connect a trainer in Settings";
                case TrainerConnectionState.Scanning: return "Scanning for trainers";
                case TrainerConnectionState.Connecting: return "Waiting for trainer data";
                case TrainerConnectionState.Connected: return "Trainer connected";
                case TrainerConnectionState.Reconnecting: return "Reconnecting";
                case TrainerConnectionState.PermissionRequired: return "Bluetooth permission required";
                case TrainerConnectionState.Failed: return "Connection failed. Retry.";
                default: return "Trainer disconnected";
            }
        }

        private void DrawHome(float width)
        {
            var margin = 55f;
            var gap = 26f;
            var cardWidth = (width - 2f * margin - 2f * gap) / 3f;
            var heading = new GUIStyle(GUI.skin.label) { fontSize = 34, fontStyle = FontStyle.Bold };
            heading.normal.textColor = Color.white;
            GUI.Label(new Rect(margin, 105, width - 2f * margin, 48), T("选择今天的风之旅程", "Choose today's wind trail"), heading);
            var sub = new GUIStyle(GUI.skin.label) { fontSize = 16 };
            sub.normal.textColor = new Color(1f, 1f, 1f, .68f);
            GUI.Label(new Rect(margin + 2f, 151, width - 2f * margin, 28),
                T("三个关卡拥有不同场景与训练强度，进入后包含 7 个骑行阶段。",
                    "Three levels, three environments and intensities. Each ride contains seven stages."), sub);

            for (var index = 0; index < 3; index++)
                DrawLevelCard(index, new Rect(margin + index * (cardWidth + gap), 195, cardWidth, 300));

            GUI.enabled = demoMode || coordinator.State == TrainerConnectionState.Connected;
            if (GUI.Button(new Rect((width - 340f) * .5f, 548, 340, 68),
                    T($"进入关卡 {selectedLevel + 1}", $"RIDE LEVEL {selectedLevel + 1}"))) StartFirstRide();
            GUI.enabled = true;
            if (!demoMode && coordinator.State != TrainerConnectionState.Connected)
                GUI.Label(new Rect((width - 500f) * .5f, 625, 500, 30), T("连接动感单车后即可进入所选关卡。", "Connect a trainer to enter the selected level."),
                    new GUIStyle(sub) { alignment = TextAnchor.MiddleCenter });
            else if (demoMode)
                GUI.Label(new Rect((width - 600f) * .5f, 625, 600, 30),
                    T("体验模式：踏频、阻力、功率和速度均为模拟数据。", "Demo: cadence, resistance, power and speed are simulated."),
                    new GUIStyle(sub) { alignment = TextAnchor.MiddleCenter });
            else
                GUI.Label(new Rect((width - 500f) * .5f, 625, 500, 30),
                    $"{T("已连接", "CONNECTED")}  ·  {telemetry?.CadenceRpm ?? 0f:0} rpm  ·  {telemetry?.PowerWatts ?? 0} W",
                    new GUIStyle(sub) { alignment = TextAnchor.MiddleCenter });
        }

        private void DrawLevelCard(int index, Rect rect)
        {
            var selected = selectedLevel == index;
            var accent = index == 0 ? new Color(.22f, .92f, .69f) :
                index == 1 ? new Color(.18f, .73f, 1f) : new Color(.63f, .52f, 1f);
            Fill(new Rect(rect.x - (selected ? 4 : 1), rect.y - (selected ? 4 : 1),
                rect.width + (selected ? 8 : 2), rect.height + (selected ? 8 : 2)),
                selected ? accent : new Color(1f, 1f, 1f, .18f));
            Fill(rect, new Color(.025f, .055f, .085f, 1f));
            if (levelArtwork[index] != null)
                GUI.DrawTexture(new Rect(rect.x, rect.y, rect.width, 176), levelArtwork[index], ScaleMode.ScaleAndCrop);
            Fill(new Rect(rect.x, rect.y + 142, rect.width, 158), new Color(.018f, .04f, .065f, .94f));

            var title = new GUIStyle(GUI.skin.label) { fontSize = 23, fontStyle = FontStyle.Bold };
            title.normal.textColor = Color.white;
            var detail = new GUIStyle(GUI.skin.label) { fontSize = 14 };
            detail.normal.textColor = new Color(1f, 1f, 1f, .72f);
            GUI.Label(new Rect(rect.x + 18, rect.y + 155, rect.width - 36, 34), LevelName(index), title);
            GUI.Label(new Rect(rect.x + 18, rect.y + 192, rect.width - 36, 24), LevelSubtitle(index), detail);
            GUI.Label(new Rect(rect.x + 18, rect.y + 228, rect.width - 145, 24),
                index == 0 ? T("轻松 · 湖岸探索", "EASY · LAKESIDE") :
                index == 1 ? T("进阶 · 风谷节奏", "TEMPO · WIND CANYON") :
                T("挑战 · 云端冲刺", "HARD · CLOUD SPRINT"), detail);
            GUI.Label(new Rect(rect.x + rect.width - 120, rect.y + 228, 100, 24), selected ? T("已选择 ✓", "SELECTED ✓") : T("选择", "SELECT"),
                new GUIStyle(detail) { alignment = TextAnchor.MiddleRight, normal = { textColor = accent } });
            var minutes = index == 0 ? 20 : index == 1 ? 30 : 70;
            var kilometers = index == 0 ? 9.6f : index == 1 ? 14.4f : 33.6f;
            GUI.Label(new Rect(rect.x + 18, rect.y + 258, rect.width - 36, 24),
                T($"约 {minutes} 分钟 · {kilometers:0.0} km", $"~{minutes} min · {kilometers:0.0} km"), detail);

            var invisible = new GUIStyle(GUI.skin.button);
            invisible.normal.background = null;
            invisible.hover.background = null;
            invisible.active.background = null;
            if (GUI.Button(rect, GUIContent.none, invisible))
            {
                selectedLevel = index;
                PlayerPrefs.SetInt("ride.level", index);
                PlayerPrefs.Save();
            }
        }

        private string LevelName(int index)
        {
            if (index == 1) return T("风谷峡桥", "WIND CANYON");
            if (index == 2) return T("云端风车镇", "CLOUD VILLAGE");
            return T("太浩湖畔", "TAHOE LAKESIDE");
        }

        private string LevelSubtitle(int index)
        {
            if (index == 1) return T("高架风桥与峡谷节奏训练", "Sky bridges and rhythm intervals");
            if (index == 2) return T("山麓风车镇与高强度冲刺", "Mountain windmill town and hard sprints");
            return T("森林、湖岸与舒适耐力骑行", "Forest, shoreline and endurance riding");
        }

        private static void DrawMetric(float x, float y, string label, object value, string unit)
        {
            var labelStyle = new GUIStyle(GUI.skin.label) { fontSize = 16, alignment = TextAnchor.MiddleCenter };
            var valueStyle = new GUIStyle(GUI.skin.label) { fontSize = 42, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
            valueStyle.normal.textColor = new Color(0.25f, 0.9f, 0.78f);
            GUI.Label(new Rect(x, y, 200, 30), label, labelStyle);
            GUI.Label(new Rect(x, y + 45, 200, 55), value == null ? "--" : FormatValue(value), valueStyle);
            GUI.Label(new Rect(x, y + 108, 200, 25), unit, labelStyle);
        }

        private static string FormatValue(object value)
        {
            if (value is float number) return number.ToString("0.0");
            return value?.ToString() ?? "--";
        }

        private void DrawSettings(float width)
        {
            var xScale = width / 1280f;
            Func<float, float, float, float, Rect> r = (x, y, w, h) => new Rect(x * xScale, y, w * xScale, h);
            GUI.Box(r(70, 120, 1140, 530), string.Empty);
            var heading = new GUIStyle(GUI.skin.label) { fontSize = 28, fontStyle = FontStyle.Bold };
            GUI.Label(r(105, 145, 400, 45), T("骑行设置", "Trainer settings"), heading);

            var saved = coordinator.SavedTrainer;
            GUI.Label(r(105, 205, 680, 30), saved == null
                ? T("尚未保存设备。扫描并选择你的动感单车。", "No trainer saved. Scan and select your own bike.")
                : $"{T("已保存", "Saved")}: {saved.Name} ({saved.Protocol})");

            if (GUI.Button(r(105, 250, 185, 48), T("扫描设备", "Scan for trainers")))
            {
                devices.Clear();
                coordinator.ScanForPairing();
            }
            if (saved != null && GUI.Button(r(305, 250, 185, 48), T("自动重连", "Auto reconnect")))
                coordinator.StartAutoConnect();
            if (saved != null && GUI.Button(r(505, 250, 185, 48), T("忘记设备", "Forget trainer")))
                coordinator.ForgetTrainer();
            GUI.Label(r(105, 298, 610, 26), statusMessage,
                new GUIStyle(GUI.skin.label) { fontSize = 14, normal = { textColor = new Color(.55f, .88f, 1f) } });

            GUI.Label(r(735, 205, 130, 30), T("体能 FTP", "Fitness FTP"));
            if (GUI.Button(r(735, 248, 45, 45), "−")) SaveFtp(ftpWatts - 5);
            GUI.Box(r(790, 248, 95, 45), $"{ftpWatts} W");
            if (GUI.Button(r(895, 248, 45, 45), "+")) SaveFtp(ftpWatts + 5);

            GUI.Label(r(970, 205, 180, 30), T("骑手角色", "Rider character"));
            var selected = RideWorldController.SelectedCharacter;
            if (GUI.Button(r(970, 248, 95, 45), T("男骑手", "Male") + (selected == RiderCharacter.Male ? " ✓" : ""))) SaveCharacter(RiderCharacter.Male);
            if (GUI.Button(r(1075, 248, 95, 45), T("女骑手", "Female") + (selected == RiderCharacter.Female ? " ✓" : ""))) SaveCharacter(RiderCharacter.Female);

            GUI.Label(r(735, 315, 150, 30), T("语言 / Language", "Language / 语言"));
            if (GUI.Button(r(735, 350, 100, 44), "中文" + (!english ? " ✓" : ""))) SaveLanguage(false);
            if (GUI.Button(r(845, 350, 100, 44), "English" + (english ? " ✓" : ""))) SaveLanguage(true);

            GUI.Label(r(105, 325, 600, 28), T("附近的兼容设备", "Nearby compatible trainers"));
            scroll = GUI.BeginScrollView(r(105, 360, 610, 225), scroll, new Rect(0, 0, 590 * xScale, Mathf.Max(220, devices.Count * 58)));
            for (var index = 0; index < devices.Count; index++)
            {
                var device = devices[index];
                GUI.Label(r(10, index * 58, 365, 46), $"{device.Name}   {device.Protocol}   {device.Rssi} dBm");
                if (GUI.Button(r(380, index * 58, 190, 42), T("连接并保存", "Connect and save")))
                    coordinator.Connect(device);
            }
            GUI.EndScrollView();
            GUI.Label(r(105, 602, 960, 26), T("提示：扫描前先踩踏，唤醒电池供电的设备。", "Tip: pedal first to wake a battery-powered trainer before scanning."));
            GUI.Label(r(735, 414, 430, 44),
                T("路线参考：太浩湖西岸 · OpenStreetMap 贡献者 · 原创湖岸场景",
                    "Route reference: Tahoe west shore · OpenStreetMap contributors · Original lakeside scenery"),
                new GUIStyle(GUI.skin.label) { fontSize = 13, wordWrap = true });
            if (GUI.Button(r(735, 500, 430, 54),
                    demoMode ? T("退出体验模式", "Exit demo mode") : T("开启体验模式 · 模拟骑行", "Start demo · simulated ride")))
            {
                if (demoMode) StopDemoMode();
                else StartDemoMode();
            }
            GUI.Label(r(735, 562, 430, 50),
                T("只用于体验关卡与界面；不会保存为真实设备。", "For previewing levels and UI only; never saved as a real trainer."),
                new GUIStyle(GUI.skin.label) { fontSize = 14, wordWrap = true });
        }

        private void DrawRideHud(float width)
        {
            if (demoMode)
                GUI.Box(new Rect(28f, 113f, 230f, 36f), T("体验模式 · 模拟数据", "DEMO · SIMULATED DATA"));
            if (rideSnapshot != null && rideSnapshot.WindBoostActive)
                DrawBoostSpeedLines(width, rideSnapshot.IsPedaling);
            var mint = new Color(.31f, .93f, .79f);
            var cyan = new Color(.2f, .79f, 1f);
            var progressX = width - 52f;
            var white = new GUIStyle(GUI.skin.label) { normal = { textColor = Color.white } };
            var small = new GUIStyle(white) { fontSize = 15 };
            var bold = new GUIStyle(white) { fontSize = 22, fontStyle = FontStyle.Bold };
            var center = new GUIStyle(white) { alignment = TextAnchor.MiddleCenter, fontSize = 16 };
            var windCharge = rideSnapshot?.UltimateCharge ?? 0f;
            var barX = 10f;
            var barY = 10f;
            var barWidth = width - 110f;
            var barHeight = 99f;
            GUI.Box(new Rect(barX, barY, barWidth, barHeight), GUIContent.none, hudPanelStyle);
            if (hudStageIcon != null) GUI.DrawTexture(new Rect(barX + 12f, barY + 11f, 76f, 76f), hudStageIcon);
            var titleStyle = new GUIStyle(white) { fontSize = 19, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleLeft };
            GUI.Label(new Rect(barX + 97f, barY + 14f, 270f, 30f), LevelName(selectedLevel), titleStyle);
            var stageLine = rideSnapshot == null ? T("准备骑行", "Preparing ride") :
                $"{T("阶段", "Stage")} {rideSnapshot.PhaseIndex + 1}/{rideSnapshot.PhaseCount}  ·  {T("分数", "Score")} {rideSnapshot.Score:N0}";
            GUI.Label(new Rect(barX + 97f, barY + 56f, 285f, 25f), stageLine, new GUIStyle(small) { fontSize = 14 });

            var introWidth = Mathf.Max(350f, barWidth * .31f);
            var metricWidth = (barWidth - introWidth) / 5f;
            for (var column = 0; column < 5; column++)
                Fill(new Rect(barX + introWidth + column * metricWidth, barY + 23f, 1f, 55f),
                    new Color(1f, 1f, 1f, .17f));
            DrawRideMetric(barX + introWidth, metricWidth, T("踏频", "CADENCE"), telemetry?.CadenceRpm, "rpm");
            DrawRideMetric(barX + introWidth + metricWidth, metricWidth, T("阻力", "RESISTANCE"), telemetry?.ResistanceLevel, string.Empty);
            DrawRideMetric(barX + introWidth + metricWidth * 2f, metricWidth, T("功率", "POWER"), telemetry?.PowerWatts, "W");
            DrawRideMetric(barX + introWidth + metricWidth * 3f, metricWidth, T("速度", "SPEED"), telemetry?.SpeedKph, "km/h");
            DrawRideMetric(barX + introWidth + metricWidth * 4f, metricWidth, T("计时", "TIME"),
                rideSnapshot == null ? null : FormatTime(rideSnapshot.TotalActiveSeconds), string.Empty);

            var menuRect = new Rect(width - 82f, 22f, 66f, 66f);
            if (hudExitIcon != null) GUI.DrawTexture(menuRect, hudExitIcon);
            if (GUI.Button(menuRect, GUIContent.none, InvisibleButtonStyle()))
            {
                showRideSettings = !showRideSettings;
                if (showRideSettings) rideMusic?.Pause();
                else rideMusic?.UnPause();
            }

            if (rideSnapshot != null)
            {
                var overall = rideSnapshot.TotalPlannedSeconds <= 0f ? 0f :
                    Mathf.Clamp01(rideSnapshot.RouteProgressSeconds / rideSnapshot.TotalPlannedSeconds);

                Fill(new Rect(progressX + 5, 251, 12, 278), new Color(.11f, .15f, .17f, .52f));
                Fill(new Rect(progressX + 8, 252, 6, 274), new Color(.76f, .8f, .82f, .5f));
                if (overall > 0f)
                    GUI.DrawTexture(new Rect(progressX + 8, 526 - 274 * overall, 6, 274 * overall), routeGradientTexture);
                GUI.DrawTexture(new Rect(progressX - 6, 216, 34, 34), finishFlagTexture);
                for (var i = 0; i <= rideSnapshot.PhaseCount; i++)
                {
                    var nodeY = 526 - 274f * i / rideSnapshot.PhaseCount;
                    Fill(new Rect(progressX + 3, nodeY - 1, 16, 3), new Color(1f, 1f, 1f, .86f));
                }
                var riderY = 526 - 274 * overall;
                if (riderBadgeIcon == null)
                    GUI.DrawTexture(new Rect(progressX - 17, riderY - 25, 56, 50), riderMarkerTexture);
                if (riderBadgeIcon != null)
                    GUI.DrawTexture(new Rect(progressX - 14, riderY - 25, 50, 50), riderBadgeIcon, ScaleMode.ScaleToFit);
                GUI.Label(new Rect(progressX - 70, 558, 116, 34), $"{overall * RideWorldController.CourseKilometers:0.0} km",
                    new GUIStyle(center) { fontSize = 18, fontStyle = FontStyle.Bold });

                var boostReady = windCharge >= RideSessionEngine.WindBoostCost && !rideSnapshot.WindBoostActive;
                var boostRect = new Rect(30, 560, 124, 124);
                var dialPercent = rideSnapshot.WindBoostActive
                    ? rideSnapshot.WindBoostSeconds / RideSessionEngine.WindBoostDurationSeconds * 100f
                    : windCharge;
                UpdateBoostDialTexture(dialPercent, rideSnapshot.WindBoostActive);
                if (boostDialTexture != null) GUI.DrawTexture(boostRect, boostDialTexture);
                if (boostShoeIcon != null)
                    GUI.DrawTexture(new Rect(40, 570, 104, 104), boostShoeIcon, ScaleMode.ScaleToFit);
                var canBoostNow = boostReady && rideSnapshot.IsPedaling &&
                                  (demoMode || coordinator.State == TrainerConnectionState.Connected) &&
                                  freshness.IsFresh(DateTimeOffset.UtcNow);
                if (canBoostNow && GUI.Button(boostRect, GUIContent.none, InvisibleButtonStyle()))
                    ActivateWindBoost();
                var boostLabel = new GUIStyle(small) { fontSize = 18, fontStyle = FontStyle.Bold };
                boostLabel.normal.textColor = boostReady ? cyan : Color.white;
                var boostTitle = rideSnapshot.WindBoostActive
                    ? T($"疾风冲刺 {rideSnapshot.WindBoostSeconds:0.0}s", $"TAILWIND {rideSnapshot.WindBoostSeconds:0.0}s")
                    : boostReady
                        ? T(canBoostNow ? "点击释放疾风冲刺" : "踩踏后可释放冲刺", canBoostNow ? "TAP FOR TAILWIND" : "PEDAL TO BOOST")
                        : T($"风能 {windCharge:0} / {RideSessionEngine.WindBoostCost:0}", $"WIND {windCharge:0} / {RideSessionEngine.WindBoostCost:0}");
                GUI.Label(new Rect(163, 586, 310, 28), boostTitle, boostLabel);
                GUI.Label(new Rect(163, 616, 330, 24),
                    T("冲刺：路程 ×2 · 得分 ×2", "BOOST: 2x PROGRESS · 2x SCORE"), small);

                if (rideSnapshot.WindBoostActive)
                {
                    var bannerY = rideSnapshot.PhaseKind == RidePhaseKind.Boss ? 260f : 133f;
                    Fill(new Rect((width - 410f) * .5f, bannerY, 410f, 64f), new Color(.015f, .18f, .24f, .88f));
                    Fill(new Rect((width - 410f) * .5f, bannerY, 410f *
                        Mathf.Clamp01(rideSnapshot.WindBoostSeconds / RideSessionEngine.WindBoostDurationSeconds), 5f), cyan);
                    GUI.Label(new Rect((width - 390f) * .5f, bannerY + 8f, 390f, 28f),
                        T($"疾风冲刺  {rideSnapshot.WindBoostSeconds:0.0}s", $"TAILWIND  {rideSnapshot.WindBoostSeconds:0.0}s"),
                        new GUIStyle(bold) { alignment = TextAnchor.MiddleCenter, normal = { textColor = cyan } });
                    GUI.Label(new Rect((width - 390f) * .5f, bannerY + 38f, 390f, 20f),
                        T("路程推进 ×2  ·  得分 ×2", "2x PROGRESS  ·  2x SCORE"), center);
                }

                if (rideSnapshot.PhaseKind == RidePhaseKind.Boss)
                {
                    Fill(new Rect((width - 500f) * .5f, 165, 500, 14), new Color(.15f, .07f, .2f, .8f));
                    Fill(new Rect((width - 500f) * .5f, 165, 500 * rideSnapshot.BossHealth01, 14), new Color(.85f, .3f, .96f));
                    GUI.Label(new Rect((width - 500f) * .5f, 185, 500, 28), T("风暴守护者", "TEMPEST GUARDIAN"), center);
                }
            }

            if (!string.IsNullOrEmpty(windFeedback) && Time.unscaledTime < windFeedbackUntil)
            {
                Fill(new Rect((width - 520f) * .5f, 202, 520, 48), new Color(.02f, .12f, .18f, .9f));
                GUI.Label(new Rect((width - 500f) * .5f, 208, 500, 36), windFeedback,
                    new GUIStyle(bold) { alignment = TextAnchor.MiddleCenter, normal = { textColor = cyan } });
            }

            var signalFresh = freshness.IsFresh(DateTimeOffset.UtcNow) &&
                              (demoMode || coordinator.State == TrainerConnectionState.Connected);
            if (!signalFresh)
            {
                var warning = new GUIStyle(GUI.skin.box) { fontSize = 24, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
                warning.normal.textColor = new Color(1f, .78f, .25f);
                GUI.Box(new Rect((width - 600f) * .5f, 565, 600, 80), T("骑行已暂停 · 正在重新连接设备", "Ride paused · reconnecting trainer"), warning);
            }
            else if (rideSnapshot == null || !rideSnapshot.IsPedaling)
            {
                GUI.Box(new Rect((width - 440f) * .5f, 565, 440, 65), T("开始踩踏，继续前进", "Start pedaling to move"));
            }
        }

        private string LocalizedPhaseTitle()
        {
            if (rideSnapshot == null) return T("准备骑行", "Preparing ride");
            if (english) return rideSnapshot.PhaseTitle;
            if (selectedLevel == 1)
            {
                switch (rideSnapshot.PhaseKind)
                {
                    case RidePhaseKind.Warmup: return "风谷入口 · 热身";
                    case RidePhaseKind.Explore: return "峡谷河道 · 探索";
                    case RidePhaseKind.CadenceChallenge: return "高架风桥 · 稳住节奏";
                    case RidePhaseKind.Recovery: return "砂岩弯道 · 恢复";
                    case RidePhaseKind.Sprint: return "峡谷风口 · 冲刺";
                    case RidePhaseKind.Boss: return "风谷峰顶挑战";
                    case RidePhaseKind.Cooldown: return "峡谷落日 · 放松";
                    default: return "骑行完成";
                }
            }
            if (selectedLevel == 2)
            {
                switch (rideSnapshot.PhaseKind)
                {
                    case RidePhaseKind.Warmup: return "风车镇 · 热身";
                    case RidePhaseKind.Explore: return "风车小径 · 探索";
                    case RidePhaseKind.CadenceChallenge: return "花野路段 · 稳住节奏";
                    case RidePhaseKind.Recovery: return "溪畔缓行 · 恢复";
                    case RidePhaseKind.Sprint: return "云岭 · 冲刺";
                    case RidePhaseKind.Boss: return "风车峰顶挑战";
                    case RidePhaseKind.Cooldown: return "小镇黄昏 · 放松";
                    default: return "骑行完成";
                }
            }
            switch (rideSnapshot.PhaseKind)
            {
                case RidePhaseKind.Warmup: return "太浩城 · 热身";
                case RidePhaseKind.Explore: return "西岸步道 · 探索";
                case RidePhaseKind.CadenceChallenge: return "松林湾 · 稳住节奏";
                case RidePhaseKind.Recovery: return "湖岸缓行 · 恢复";
                case RidePhaseKind.Sprint: return "霍姆伍德 · 冲刺";
                case RidePhaseKind.Boss: return "西岸爬坡挑战";
                case RidePhaseKind.Cooldown: return "太浩湖暮色 · 放松";
                default: return "骑行完成";
            }
        }

        private void DrawRideSettings(float width)
        {
            var x = width - 545f;
            Fill(new Rect(x, 112, 510, 295), new Color(.025f, .055f, .085f, .94f));
            var title = new GUIStyle(GUI.skin.label) { fontSize = 27, fontStyle = FontStyle.Bold };
            title.normal.textColor = Color.white;
            GUI.Label(new Rect(x + 25, 132, 450, 40), T("骑行设置", "Ride settings"), title);
            GUI.Label(new Rect(x + 25, 192, 450, 28), T("语言 / Language", "Language / 语言"));
            if (GUI.Button(new Rect(x + 25, 226, 165, 50), "中文" + (!english ? " ✓" : ""))) SaveLanguage(false);
            if (GUI.Button(new Rect(x + 207, 226, 165, 50), "English" + (english ? " ✓" : ""))) SaveLanguage(true);
            GUI.Label(new Rect(x + 25, 282, 445, 34),
                selectedLevel == 0
                    ? T("路线参考：太浩湖西岸 · OSM / 原创湖岸场景",
                        "Route reference: Tahoe West Shore · OSM / authored lakeside")
                    : T($"原创场景：{LevelName(selectedLevel)}",
                        $"Original environment: {LevelName(selectedLevel)}"),
                new GUIStyle(GUI.skin.label) { fontSize = 13 });
            if (GUI.Button(new Rect(x + 25, 324, 165, 51), T("继续骑行", "Continue")))
            {
                showRideSettings = false;
                rideMusic?.UnPause();
            }
            if (GUI.Button(new Rect(x + 207, 324, 165, 51), T("退出骑行", "Exit ride"))) ExitRide();
        }

        private static void Fill(Rect rect, Color color)
        {
            var previous = GUI.color;
            GUI.color = color;
            GUI.DrawTexture(rect, Texture2D.whiteTexture);
            GUI.color = previous;
        }

        private void DrawBoostSpeedLines(float width, bool moving)
        {
            if (Time.unscaledTime < boostFlashUntil)
            {
                var flash = Mathf.Clamp01((boostFlashUntil - Time.unscaledTime) / .55f);
                Fill(new Rect(0f, 0f, width, 720f), new Color(.45f, .94f, 1f, flash * .18f));
            }
            if (!moving) return;
            Fill(new Rect(0f, 130f, 7f, 520f), new Color(.35f, .9f, 1f, .3f));
            Fill(new Rect(width - 7f, 130f, 7f, 520f), new Color(.35f, .9f, 1f, .3f));
            for (var i = 0; i < 12; i++)
            {
                var y = 150f + Mathf.Repeat(Time.unscaledTime * (270f + i % 3 * 35f) + i * 67f, 510f);
                var length = 75f + i % 4 * 35f;
                var thickness = 2f + i % 3;
                var color = new Color(.38f, .92f, 1f, .15f + i % 3 * .045f);
                Fill(new Rect(14f + i % 3 * 21f, y, length, thickness), color);
                Fill(new Rect(width - 14f - i % 3 * 21f - length, y + 18f, length, thickness), color);
            }
        }

        private void UpdateBoostDialTexture(float charge, bool active)
        {
            var percent = Mathf.Clamp(Mathf.RoundToInt(charge), 0, 100);
            var cacheKey = active ? percent + 101 : percent;
            if (cacheKey == cachedBoostDialPercent && boostDialTexture != null) return;
            cachedBoostDialPercent = cacheKey;
            if (boostDialTexture != null) Destroy(boostDialTexture);
            const int size = 192;
            var pixels = new Color32[size * size];
            var arcEnd = 158f - 164f * percent / 100f;
            for (var y = 0; y < size; y++)
            for (var x = 0; x < size; x++)
            {
                var dx = (x + .5f - size * .5f) / (size * .5f);
                var dy = (y + .5f - size * .5f) / (size * .5f);
                var radius = Mathf.Sqrt(dx * dx + dy * dy);
                var angle = Mathf.Atan2(dy, dx) * Mathf.Rad2Deg;
                var pixel = new Color32(0, 0, 0, 0);
                if (radius < .83f) pixel = new Color32(8, 18, 18, 188);
                if (radius >= .84f && radius <= .875f) pixel = new Color32(180, 190, 186, 160);
                if (radius >= .88f && radius <= .98f && angle <= 158f && angle >= arcEnd)
                    pixel = active ? new Color32(65, 230, 255, 255) : new Color32(255, 178, 15, 255);
                pixels[y * size + x] = pixel;
            }
            boostDialTexture = new Texture2D(size, size, TextureFormat.RGBA32, false);
            boostDialTexture.SetPixels32(pixels);
            boostDialTexture.Apply();
            boostDialTexture.filterMode = FilterMode.Bilinear;
        }

        private static Texture2D CreateRoundedPanelTexture()
        {
            const int size = 64;
            const float radius = 16f;
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
            var pixels = new Color32[size * size];
            for (var y = 0; y < size; y++)
            for (var x = 0; x < size; x++)
            {
                var px = Mathf.Abs(x + .5f - size * .5f) - (size * .5f - radius);
                var py = Mathf.Abs(y + .5f - size * .5f) - (size * .5f - radius);
                var distance = new Vector2(Mathf.Max(px, 0f), Mathf.Max(py, 0f)).magnitude +
                               Mathf.Min(Mathf.Max(px, py), 0f) - radius;
                var coverage = Mathf.Clamp01(.5f - distance);
                var border = Mathf.Clamp01(distance + 2.5f);
                var color = Color.Lerp(new Color(.045f, .075f, .13f, .82f),
                    new Color(.71f, .75f, .8f, .65f), border);
                color.a *= coverage;
                pixels[y * size + x] = color;
            }
            texture.SetPixels32(pixels);
            texture.Apply();
            texture.filterMode = FilterMode.Bilinear;
            return texture;
        }

        private static Texture2D CreateHudIconTexture(bool exit)
        {
            const int size = 96;
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
            var pixels = new Color32[size * size];
            for (var y = 0; y < size; y++)
            for (var x = 0; x < size; x++)
            {
                var nx = (x + .5f) / size;
                var top = 1f - (y + .5f) / size;
                var radius = Vector2.Distance(new Vector2(nx, top), new Vector2(.5f, .5f));
                if (radius > .495f) continue;
                var circleEdge = radius > .465f;
                var color = circleEdge ? new Color(.73f, .77f, .83f, .66f) :
                    new Color(.13f, .16f, .2f, .90f);
                bool glyph;
                if (exit)
                {
                    var door = nx > .27f && nx < .58f && top > .26f && top < .75f &&
                               (nx < .32f || nx > .53f || top < .31f || top > .70f);
                    var shaft = nx > .37f && nx < .72f && top > .465f && top < .535f;
                    var arrow = nx > .64f && nx < .83f && Mathf.Abs(top - .5f) < (.83f - nx) * .8f;
                    glyph = door || shaft || arrow;
                }
                else
                {
                    var roof = top > .25f && top < .52f && Mathf.Abs(nx - .5f) < (top - .2f) * 1.1f;
                    var walls = nx > .32f && nx < .68f && top > .46f && top < .75f;
                    var door = nx > .455f && nx < .545f && top > .58f && top < .76f;
                    var window = nx > .355f && nx < .425f && top > .53f && top < .62f;
                    glyph = (roof || walls) && !door && !window;
                }
                pixels[y * size + x] = glyph ? new Color32(255, 255, 255, 255) : (Color32)color;
            }
            texture.SetPixels32(pixels);
            texture.Apply();
            texture.filterMode = FilterMode.Bilinear;
            return texture;
        }

        private static Texture2D CreateRouteGradientTexture()
        {
            var texture = new Texture2D(1, 128, TextureFormat.RGBA32, false);
            for (var y = 0; y < 128; y++)
            {
                var t = y / 127f;
                texture.SetPixel(0, y, Color.Lerp(new Color(.14f, .83f, 1f),
                    new Color(1f, .68f, .12f), Mathf.SmoothStep(0f, 1f, t)));
            }
            texture.Apply();
            texture.filterMode = FilterMode.Bilinear;
            return texture;
        }

        private static Texture2D CreateFinishFlagTexture()
        {
            const int size = 32;
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
            var pixels = new Color32[size * size];
            for (var y = 0; y < size; y++)
            for (var x = 0; x < size; x++)
            {
                var inside = x >= 4 && x < 28 && y >= 6 && y < 30;
                pixels[y * size + x] = !inside ? new Color32(0, 0, 0, 0) :
                    (x / 6 + y / 6) % 2 == 0 ? new Color32(255, 255, 255, 255) :
                    new Color32(18, 22, 24, 255);
            }
            texture.SetPixels32(pixels);
            texture.Apply();
            texture.filterMode = FilterMode.Point;
            return texture;
        }

        private static Texture2D CreateRiderMarkerTexture()
        {
            const int size = 64;
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
            var pixels = new Color32[size * size];
            for (var y = 0; y < size; y++)
            for (var x = 0; x < size; x++)
            {
                var dx = (x + .5f - size * .5f) / (size * .5f);
                var dy = (y + .5f - size * .5f) / (size * .5f);
                var radius = Mathf.Sqrt(dx * dx + dy * dy);
                pixels[y * size + x] = radius > .98f ? new Color32(0, 0, 0, 0) :
                    radius > .79f ? new Color32(63, 215, 255, 255) :
                    radius > .68f ? new Color32(255, 255, 255, 255) :
                    new Color32(30, 89, 105, 255);
            }
            texture.SetPixels32(pixels);
            texture.Apply();
            texture.filterMode = FilterMode.Bilinear;
            return texture;
        }

        private static GUIStyle InvisibleButtonStyle()
        {
            var style = new GUIStyle(GUI.skin.button);
            style.normal.background = null;
            style.hover.background = null;
            style.active.background = null;
            return style;
        }

        private static void DrawRideMetric(float x, float width, string label, object value, string unit)
        {
            var style = new GUIStyle(GUI.skin.label) { fontSize = 16, alignment = TextAnchor.MiddleLeft };
            style.normal.textColor = Color.white;
            var valueStyle = new GUIStyle(style) { fontSize = 32, fontStyle = FontStyle.Bold };
            valueStyle.normal.textColor = new Color(.25f, .95f, .78f);
            var valueText = value == null ? "--" : FormatValue(value);
            GUI.Label(new Rect(x + 26f, 22f, width - 42f, 26f), label, style);
            GUI.Label(new Rect(x + 25f, 55f, width - 38f, 42f), valueText, valueStyle);
            var unitX = Mathf.Min(width - 39f, 29f + valueStyle.CalcSize(new GUIContent(valueText)).x);
            GUI.Label(new Rect(x + unitX, 67f, 43f, 24f), unit, new GUIStyle(style) { fontSize = 14 });
        }

        private static void DrawBar(Rect rect, float progress, Color color)
        {
            GUI.Box(rect, string.Empty);
            var old = GUI.color;
            GUI.color = color;
            GUI.DrawTexture(new Rect(rect.x + 3, rect.y + 3, (rect.width - 6) * Mathf.Clamp01(progress), rect.height - 6), Texture2D.whiteTexture);
            GUI.color = old;
        }

        private void DrawSummary(float width)
        {
            var center = width * .5f;
            var heading = new GUIStyle(GUI.skin.label) { fontSize = 42, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
            heading.normal.textColor = new Color(.25f, .95f, .78f);
            GUI.Label(new Rect(center - 400, 145, 800, 60), T("首次骑行完成", "FIRST RIDE COMPLETE"), heading);
            GUI.Box(new Rect(center - 310, 235, 620, 250), string.Empty);
            GUI.Label(new Rect(center - 230, 285, 460, 40), $"{T("分数", "Score")}     {(rideSnapshot?.Score ?? 0):N0}");
            GUI.Label(new Rect(center - 230, 345, 460, 40), $"{T("骑行时间", "Active time")}     {FormatTime(rideSnapshot?.TotalActiveSeconds ?? 0)}");
            GUI.Label(new Rect(center - 230, 405, 460, 40), $"{T("距离", "Distance")}     {RideWorldController.CourseKilometers:0.0} km");
            if (GUI.Button(new Rect(center - 175, 540, 350, 65), T("再次骑行", "RIDE AGAIN"))) StartFirstRide();
        }

        private void StartFirstRide()
        {
            if (rideWorld != null) Destroy(rideWorld.gameObject);
            rideSession = new RideSessionEngine(RideSessionConfig.ForLevel(selectedLevel, ftpWatts));
            rideSnapshot = rideSession.Current;
            var world = new GameObject("First Ride World");
            rideWorld = world.AddComponent<RideWorldController>();
            rideWorld.SetLevel(selectedLevel);
            rideWorld.Initialize();
            world.AddComponent<RideRuntimeDiagnostics>();
            rideWorld.WindEnergyCollected += OnWindEnergyCollected;
            rideWorld.Apply(rideSnapshot, telemetry);
            showSettings = false;
            showRideSettings = false;
            showSummary = false;
            inRide = true;
            PlayLevelMusic();
        }

        private void PlayLevelMusic()
        {
            if (rideMusic == null) return;
            rideMusic.Stop();
            var clip = Resources.Load<AudioClip>(levelMusicPaths[selectedLevel]);
            if (clip == null)
            {
                Debug.LogWarning($"Missing level music: {levelMusicPaths[selectedLevel]}");
                return;
            }
            rideMusic.clip = clip;
            rideMusic.volume = levelMusicVolumes[selectedLevel];
            rideMusic.Play();
            Debug.Log($"LEVEL_MUSIC_PLAYING level={selectedLevel + 1} clip={clip.name}");
        }

        private void OnWindEnergyCollected(float charge, int scoreBonus)
        {
            rideSession?.CollectWindEnergy(charge, scoreBonus);
            rideSnapshot = rideSession?.Current;
            var ready = (rideSnapshot?.UltimateCharge ?? 0f) >= RideSessionEngine.WindBoostCost;
            windFeedback = ready
                ? T($"能量点 +{charge:0} · 冲刺已就绪！", $"ORB +{charge:0} · BOOST READY!")
                : T($"能量点 +{charge:0} 风能 · +{scoreBonus} 分", $"ORB +{charge:0} WIND · +{scoreBonus} SCORE");
            windFeedbackUntil = Time.unscaledTime + 2.2f;
        }

        private void ActivateWindBoost()
        {
            if (rideSession == null || !rideSession.ActivateWindBoost()) return;
            rideSnapshot = rideSession.Current;
            rideWorld?.Apply(rideSnapshot, telemetry);
            boostFlashUntil = Time.unscaledTime + .55f;
            windFeedback = T("疾风冲刺！路程与得分翻倍", "TAILWIND! 2x PROGRESS AND SCORE");
            windFeedbackUntil = Time.unscaledTime + 2.4f;
        }

        private void FinishRide()
        {
            rideMusic?.Stop();
            inRide = false;
            showSummary = true;
            rideWorld?.Apply(rideSnapshot, null);
        }

        private void ExitRide()
        {
            rideMusic?.Stop();
            inRide = false;
            showSummary = false;
            showRideSettings = false;
            if (rideWorld != null) Destroy(rideWorld.gameObject);
            rideWorld = null;
            rideSession = null;
        }

        private void SaveFtp(int value)
        {
            ftpWatts = Mathf.Clamp(value, 50, 500);
            PlayerPrefs.SetInt("fitness.ftp", ftpWatts);
            PlayerPrefs.Save();
        }

        private void SaveCharacter(RiderCharacter character)
        {
            if (rideWorld != null) rideWorld.SetCharacter(character);
            else
            {
                PlayerPrefs.SetInt(RideWorldController.CharacterPreferenceKey, (int)character);
                PlayerPrefs.Save();
            }
        }

        private static string FormatTime(float seconds)
        {
            var span = TimeSpan.FromSeconds(seconds);
            return $"{(int)span.TotalMinutes:00}:{span.Seconds:00}";
        }

        private void OnDestroy()
        {
            StopDemoMode();
            if (screenWakeOverridden) Screen.sleepTimeout = previousSleepTimeout;
            if (boostDialTexture != null) Destroy(boostDialTexture);
            if (routeGradientTexture != null) Destroy(routeGradientTexture);
            if (finishFlagTexture != null) Destroy(finishFlagTexture);
            if (riderMarkerTexture != null) Destroy(riderMarkerTexture);
            if (hudPanelTexture != null) Destroy(hudPanelTexture);
            if (hudStageIcon != null) Destroy(hudStageIcon);
            if (hudExitIcon != null) Destroy(hudExitIcon);
            if (coordinator == null) return;
            coordinator.StateChanged -= OnStateChanged;
            coordinator.DeviceFound -= OnDeviceFound;
            coordinator.TelemetryReceived -= OnTelemetryReceived;
            coordinator.Dispose();
        }
    }
}
