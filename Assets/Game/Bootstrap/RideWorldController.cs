using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using WindTraceRide.Core;

namespace WindTraceRide.Bootstrap
{
    public sealed class RideWorldController : MonoBehaviour
    {
        private readonly List<Transform> roadTiles = new List<Transform>();
        private readonly List<Transform> scenery = new List<Transform>();
        private readonly List<Transform> windOrbs = new List<Transform>();
        private readonly List<float> windOrbDistances = new List<float>();
        private readonly List<StreamedScenery> streamedScenery = new List<StreamedScenery>();
        private readonly List<StreamedCloud> streamedClouds = new List<StreamedCloud>();
        private readonly List<Transform> canyonLandmarks = new List<Transform>();
        private readonly List<Transform> storybookLandmarks = new List<Transform>();
        private readonly List<Transform> boostTrails = new List<Transform>();
        private readonly List<Transform> windmillRotors = new List<Transform>();
        private readonly List<Transform> cameraFacingProps = new List<Transform>();
        private readonly Dictionary<Transform, float> landmarkLaterals = new Dictionary<Transform, float>();
        private readonly Dictionary<Transform, float> landmarkYawOffsets = new Dictionary<Transform, float>();
        private readonly Dictionary<Transform, Quaternion> landmarkAxisOffsets =
            new Dictionary<Transform, Quaternion>();
        private readonly Dictionary<string, Material> hiFiMaterials = new Dictionary<string, Material>();
        private Camera rideCamera;
        private ReferenceRideEnvironment referenceEnvironment;
        private Quaternion cameraRouteRotation = Quaternion.identity;
        private Transform rider;
        private CyclingRiderAnimator cyclistAnimator;
        private Quaternion riderRouteRotationOffset = Quaternion.identity;
        public const string CharacterPreferenceKey = "rider.character";
        public static RiderCharacter SelectedCharacter => PlayerPrefs.GetInt(CharacterPreferenceKey, 0) == 1 ? RiderCharacter.Female : RiderCharacter.Male;
        private Transform guardian;
        private Material roadMaterial;
        private Material rutMaterial;
        private Material edgeMaterial;
        private Material riderMaterial;
        private Material guardianMaterial;
        private Material energyGoldMaterial;
        private Material grassMaterial;
        private Material lakeMaterial;
        private Material cloudMaterial;
        private Material grassBladeMaterial;
        private Material skyMaterial;
        private Material canyonRockMaterial;
        private Material canyonAccentMaterial;
        private Material cottageWallMaterial;
        private Material cottageRoofMaterial;
        private Material routeGlowMaterial;
        private Material boostTrailMaterial;
        private Material cottageBillboardMaterial;
        private Material flowerBillboardMaterial;
        private Transform mountainBackdrop;
        private RideSessionSnapshot snapshot;
        private TrainerTelemetry telemetry;
        private float visualSpeed;
        private float worldDistance;
        private int selectedLevel;
        private int collectedOrbCount;
        private RidePhaseKind visualPhase = RidePhaseKind.Complete;
        private Color targetSky = new Color(.12f, .3f, .42f);
        // Keep a consistent visual pace across the 20, 30 and 70 minute rides.
        // The measured Tahoe trail is ridden as out-and-back laps.
        private const float DefaultRouteLength = 9600f;
        private static float activeRouteLength = DefaultRouteLength;
        private static int activeLevel;
        private static float geoRouteSpan;
        private static Vector3[] geoRoutePoints;
        private static float[] geoRouteDistances;
        private static RideRouteSpline smoothGeoRoute;
        private static float[,] terrainHeightGrid;
        private static Bounds terrainGridBounds;
        private const int TerrainGridCells = 256;
        private float FinishDistance => activeRouteLength - 8f;
        public static float CourseKilometers => (activeRouteLength - 8f) / 1000f;
        private const float RouteChunkLength = 120f;
        private const float SceneryStreamSpan = 480f;
        private const float OrbStreamSpan = 910f;
        private sealed class StreamedScenery
        {
            public Transform Instance;
            public float Distance;
            public float Lateral;
            public Quaternion RouteRotationOffset;
            public Vector3 OriginalScale;
            public int Cycle;
        }
        private sealed class StreamedCloud
        {
            public Transform Instance;
            public float Distance;
        }
        [Serializable]
        private sealed class GeoRouteData
        {
            public float lengthMeters;
            public GeoRoutePoint[] points;
        }
        [Serializable]
        private sealed class GeoRoutePoint
        {
            public float x;
            public float y;
            public float z;
            public float distance;
        }
        public bool ReachedFinish { get; private set; }
        public int CollectedOrbCount => collectedOrbCount;
        public event Action<float, int> WindEnergyCollected;

        private static void LoadGeoRoute()
        {
            activeRouteLength = activeLevel == 0 ? 9600f : activeLevel == 1 ? 14400f : 33600f;
            geoRouteSpan = 0f;
            geoRoutePoints = null;
            geoRouteDistances = null;
            smoothGeoRoute = null;
            terrainHeightGrid = null;
            if (activeLevel != 0) return;
            var asset = Resources.Load<TextAsset>("GeoRoutes/TahoeWestShore");
            if (asset == null) return;
            var data = JsonUtility.FromJson<GeoRouteData>(asset.text);
            if (data?.points == null || data.points.Length < 2) return;
            geoRoutePoints = new Vector3[data.points.Length];
            geoRouteDistances = new float[data.points.Length];
            for (var index = 0; index < data.points.Length; index++)
            {
                var point = data.points[index];
                geoRoutePoints[index] = new Vector3(point.x, point.y, point.z);
                geoRouteDistances[index] = point.distance;
            }
            geoRouteSpan = Mathf.Max(10f, data.lengthMeters);
            var scale=activeRouteLength/geoRouteSpan;
            var flatPoints=geoRoutePoints.Select(p=>new Vector3(p.x*scale,0,p.z*scale)).ToArray();
            smoothGeoRoute=new RideRouteSpline(flatPoints,activeRouteLength);
            Debug.Log($"REAL_ROUTE_LOADED name=TahoeWestShore points={geoRoutePoints.Length} " +
                      $"trail={geoRouteSpan:0.0} course={activeRouteLength:0.0}");
        }

        public void Initialize()
        {
            activeLevel = selectedLevel;
            LoadGeoRoute();
            CreateMaterials();
            CreateCameraAndLight();
            CreateRoad();
            CreateRider();
            CreateGuardian();
            // The georeferenced route starts kilometres away from the Unity
            // origin. Snap once before the first rendered frame, then damp all
            // subsequent motion so the opening camera never flies into place.
            UpdateRouteFollower(true);
        }

        public void Apply(RideSessionSnapshot value, TrainerTelemetry trainerTelemetry)
        {
            snapshot = value;
            telemetry = trainerTelemetry;
            var phase = LevelThemePhase();
            targetSky = PhaseSky(phase);
            if (phase != visualPhase)
            {
                visualPhase = phase;
                ApplyPhaseEnvironment(phase);
            }
            if (guardian != null)
            {
                var bossActive = value != null && value.PhaseKind == RidePhaseKind.Boss;
                guardian.gameObject.SetActive(bossActive);
                if (bossActive)
                    guardian.localScale = Vector3.one * Mathf.Lerp(.6f, 1.7f, value.BossHealth01);
            }
        }

        public void SetLevel(int levelIndex)
        {
            selectedLevel = Mathf.Clamp(levelIndex, 0, 2);
            activeLevel = selectedLevel;
            visualPhase = RidePhaseKind.Complete;
        }

        private RidePhaseKind LevelThemePhase()
        {
            if (selectedLevel == 1) return RidePhaseKind.CadenceChallenge;
            if (selectedLevel == 2) return RidePhaseKind.Boss;
            return RidePhaseKind.Warmup;
        }

        private void LateUpdate()
        {
            if (rideCamera == null) return;
            rideCamera.backgroundColor = Color.Lerp(rideCamera.backgroundColor, targetSky, Time.unscaledDeltaTime * .35f);
            var cadenceFov = snapshot != null
                ? Mathf.Clamp((snapshot.RideSpeedMultiplier - 1f) * 7f, -3f, 5f)
                : 0f;
            rideCamera.fieldOfView = Mathf.Lerp(rideCamera.fieldOfView,
                snapshot != null && snapshot.WindBoostActive ? 69f : 55f + cadenceFov,
                1f - Mathf.Exp(-Time.unscaledDeltaTime * 3.5f));
            // Use the session's debounced cadence so one bad trainer sample does
            // not visibly freeze the rider between otherwise healthy packets.
            var cadence = snapshot != null && snapshot.IsPedaling
                ? snapshot.PedalingCadenceRpm
                : 0f;
            // The authored route represents the whole seven-stage session. Use
            // active pedalling time so no stage ends after a short sprint.
            var previousDistance = worldDistance;
            var plannedSeconds = Mathf.Max(1f, snapshot?.TotalPlannedSeconds ?? 4200f);
            var targetDistance = Mathf.Clamp01((snapshot?.RouteProgressSeconds ?? 0f) / plannedSeconds) * FinishDistance;
            if (!ReachedFinish)
            {
                worldDistance = Mathf.Max(worldDistance, targetDistance);
                if (worldDistance >= FinishDistance)
                {
                    ReachedFinish = true;
                }
            }
            visualSpeed = Time.unscaledDeltaTime > 0f
                ? (worldDistance - previousDistance) / Time.unscaledDeltaTime : 0f;

            UpdateRouteFollower();
            referenceEnvironment?.UpdateDistance(worldDistance);
            UpdateMountainBackdrop();
            UpdateStreamedScenery();
            UpdateStreamedClouds();
            UpdateThemedLandmarks();
            UpdateFacingProps();
            UpdateRouteEffects();
            foreach (var rotor in windmillRotors)
                if (rotor.gameObject.activeInHierarchy) rotor.Rotate(0f, 0f, 35f * Time.deltaTime, Space.Self);

            cyclistAnimator?.Tick(cadence, visualSpeed, Time.unscaledDeltaTime);

            for (var index = 0; index < windOrbs.Count; index++)
            {
                var orb = windOrbs[index];
                if (!orb.gameObject.activeSelf) continue;
                var orbDistance = windOrbDistances[index];
                var orbRoute = RouteAt(orbDistance);
                orb.position = orbRoute.center + orbRoute.up *
                               (1.15f + Mathf.Sin(Time.time * 2.2f + index) * .16f);
                orb.Rotate(0, 65f * Time.deltaTime, 35f * Time.deltaTime, Space.Self);
                if (worldDistance + .8f < orbDistance) continue;
                WindEnergyCollected?.Invoke(8f, 120);
                collectedOrbCount++;
                Debug.Log($"WIND_ENERGY_COLLECTED index={index} distance={orbDistance:0.0}");
                var nextDistance = orbDistance + OrbStreamSpan;
                windOrbDistances[index] = nextDistance;
                orb.gameObject.SetActive(nextDistance < FinishDistance - 16f);
            }

            if (guardian != null && guardian.gameObject.activeSelf)
            {
                guardian.Rotate(0, 22f * Time.deltaTime, 0, Space.World);
                var guardianRoute = RouteAt(Mathf.Repeat(worldDistance + 25f, activeRouteLength));
                guardian.position = guardianRoute.center + guardianRoute.side * 9f +
                                    guardianRoute.up * (2.5f + Mathf.Sin(Time.time * .7f) * .22f);
            }
        }

        private void UpdateRouteFollower(bool immediate = false)
        {
            if (rider == null) return;
            var distance = Mathf.Min(worldDistance, FinishDistance);
            var frame = RouteAt(distance);
            rider.position = frame.center + frame.up * .095f;
            var riderTargetRotation = Quaternion.LookRotation(frame.tangent, frame.up) * riderRouteRotationOffset;
            var routeTargetRotation = Quaternion.LookRotation(frame.tangent, frame.up);
            if (immediate)
            {
                cameraRouteRotation = routeTargetRotation;
                var initialForward = cameraRouteRotation * Vector3.forward;
                var initialUp = Vector3.up;
                var initialPosition = frame.center - initialForward * 5.4f + initialUp * 2.25f;
                rider.rotation = riderTargetRotation;
                rideCamera.transform.position = initialPosition;
                rideCamera.transform.rotation = Quaternion.LookRotation(
                    frame.center + initialForward * 8f + initialUp * 1.05f - initialPosition, initialUp);
                return;
            }

            var delta = Mathf.Min(.1f, Time.unscaledDeltaTime);
            // One heading filter, no banking or second delayed yaw filter.
            // Both heading and position derive from the rendered road's sampler.
            var filteredHeading=Quaternion.Slerp(cameraRouteRotation,routeTargetRotation,1f-Mathf.Exp(-delta*4f));
            cameraRouteRotation=Quaternion.RotateTowards(cameraRouteRotation,filteredHeading,22f*delta);
            var cameraForward = cameraRouteRotation * Vector3.forward;
            var cameraUp = Vector3.up;
            var cameraPosition = frame.center - cameraForward * 5.4f + cameraUp * 2.25f;
            var lookTarget = frame.center + cameraForward * 8f + cameraUp * 1.05f;
            var cameraTargetRotation = Quaternion.LookRotation(lookTarget - cameraPosition, cameraUp);
            rider.rotation = Quaternion.Slerp(rider.rotation, riderTargetRotation,
                1f - Mathf.Exp(-delta * 10f));
            rideCamera.transform.SetPositionAndRotation(cameraPosition,cameraTargetRotation);
        }

        private void UpdateMountainBackdrop()
        {
            if (mountainBackdrop == null) return;
            var drift = Mathf.Sin(worldDistance * .0025f) * .8f;
            var flatForward = rideCamera.transform.forward;
            flatForward.y = 0f;
            flatForward.Normalize();
            mountainBackdrop.position = rideCamera.transform.position + flatForward * 165f +
                                        Vector3.down * 13f + rideCamera.transform.right * drift;
            mountainBackdrop.rotation = Quaternion.Euler(0f, rideCamera.transform.eulerAngles.y + 180f, 0f);
        }

        private void UpdateStreamedScenery()
        {
            foreach (var item in streamedScenery)
            {
                if (item.Instance == null) continue;
                if (worldDistance <= item.Distance + 90f) continue;
                do { item.Distance += SceneryStreamSpan; }
                while (worldDistance > item.Distance + 90f);
                var frame = RouteAt(item.Distance);
                item.Cycle++;
                var variation = Mathf.Sin(item.Distance * .061f + item.Cycle * 1.71f);
                item.Instance.position = frame.center + frame.side * (item.Lateral + variation * .7f) - frame.up * .12f;
                var yaw = item.Instance.name.Contains("fence") ? 0f : item.Cycle * 29f % 360f;
                item.Instance.rotation = Quaternion.LookRotation(frame.tangent, frame.up) *
                                         Quaternion.Euler(0f, yaw, 0f) * item.RouteRotationOffset;
                item.Instance.localScale = item.OriginalScale * (1f + variation * .08f);
            }
        }

        private void UpdateStreamedClouds()
        {
            foreach (var cloud in streamedClouds)
            {
                if (cloud.Instance == null || worldDistance <= cloud.Distance + 100f) continue;
                var previous = RouteAt(cloud.Distance).center;
                do { cloud.Distance += 1040f; }
                while (worldDistance > cloud.Distance + 100f);
                cloud.Instance.position += RouteAt(cloud.Distance).center - previous;
            }
        }

        private void RegisterStreamedScenery(Transform instance, float distance, float lateral)
        {
            var frame = RouteAt(distance);
            streamedScenery.Add(new StreamedScenery
            {
                Instance = instance,
                Distance = distance,
                Lateral = lateral,
                RouteRotationOffset = Quaternion.Inverse(Quaternion.LookRotation(frame.tangent, frame.up)) * instance.rotation,
                OriginalScale = instance.localScale
            });
        }

        public void SetReviewDistance(float distance)
        {
            worldDistance = Mathf.Clamp(distance, 0f, FinishDistance);
            referenceEnvironment?.UpdateDistance(worldDistance);
            UpdateStreamedScenery();
            UpdateStreamedClouds();
            UpdateRouteFollower(true);
            UpdateFacingProps();
            UpdateMountainBackdrop();
            UpdateRouteEffects();
        }

        public void SetReviewTheme(RidePhaseKind phase)
        {
            visualPhase = phase;
            targetSky = PhaseSky(phase);
            if (rideCamera != null) rideCamera.backgroundColor = targetSky;
            ApplyPhaseEnvironment(phase);
            UpdateFacingProps();
        }

        private void MoveLoopingObjects(List<Transform> objects, float recycleBehind, float loopLength)
        {
            var movement = visualSpeed * Time.unscaledDeltaTime;
            foreach (var item in objects)
            {
                item.position += Vector3.back * movement;
                if (item.position.z < -recycleBehind)
                    item.position += Vector3.forward * loopLength;
            }
        }

        private void CreateMaterials()
        {
            // Keep the road in the opaque queue so it cannot render over the rider.
            var shader = Shader.Find("WindTrace/Opaque") ??
                         Shader.Find("Standard");
            if (shader == null)
                throw new MissingReferenceException("No compatible built-in shader is available.");
            var terrainShader = Shader.Find("WindTrace/Stylized Terrain") ?? shader;
            var waterShader = Shader.Find("WindTrace/Stylized Water") ?? terrainShader;
            roadMaterial = NewTerrainMaterial(terrainShader, new Color(.38f, .21f, .095f), new Color(.66f, .43f, .22f), .55f, .08f);
            rutMaterial = NewTerrainMaterial(terrainShader, new Color(.3f, .16f, .065f), new Color(.47f, .29f, .12f), .7f, .04f);
            edgeMaterial = NewTerrainMaterial(terrainShader, new Color(.46f, .32f, .15f), new Color(.61f, .44f, .23f), .58f, .05f);
            riderMaterial = NewMaterial(shader, new Color(.12f, .55f, .95f));
            guardianMaterial = NewMaterial(shader, new Color(.35f, .82f, .98f));
            grassMaterial = NewTerrainMaterial(terrainShader, new Color(.035f, .24f, .045f), new Color(.24f, .54f, .105f), .31f, .08f);
            ApplyGroundTexture(roadMaterial, "GravelTrail", .28f, .92f);
            ApplyGroundTexture(rutMaterial, "GravelTrail", .31f, .67f);
            ApplyGroundTexture(edgeMaterial, "GravelTrail", .27f, .76f);
            if (selectedLevel == 1) ApplyGroundTexture(grassMaterial, "CanyonSoil", .2f, .86f);
            else ApplyGroundTexture(grassMaterial, "AlpineMeadow", .18f, selectedLevel == 0 ? .14f : .72f);
            if (grassMaterial.HasProperty("_AmbientLift"))
                grassMaterial.SetFloat("_AmbientLift", selectedLevel == 0 ? .4f : .15f);
            lakeMaterial = NewTerrainMaterial(waterShader, new Color(.018f, .16f, .28f), new Color(.11f, .31f, .39f), .12f, .88f);
            if (lakeMaterial.HasProperty("_FoamColor")) lakeMaterial.SetColor("_FoamColor", new Color(.72f, .94f, .95f));
            if (lakeMaterial.HasProperty("_ReflectionColor")) lakeMaterial.SetColor("_ReflectionColor",
                selectedLevel == 1 ? new Color(.30f, .67f, .82f) : new Color(.43f, .71f, .85f));
            if (lakeMaterial.HasProperty("_WaveStrength")) lakeMaterial.SetFloat("_WaveStrength", selectedLevel == 1 ? .12f : .075f);
            cloudMaterial = NewMaterial(shader, new Color(.93f, .96f, 1f));
            grassBladeMaterial = NewMaterial(shader, new Color(.16f, .43f, .065f));
            canyonRockMaterial = NewTerrainMaterial(terrainShader, new Color(.55f, .25f, .16f), new Color(.76f, .4f, .25f), .4f, .05f);
            canyonAccentMaterial = NewMaterial(shader, new Color(.13f, .53f, .67f));
            cottageWallMaterial = NewMaterial(shader, new Color(.95f, .79f, .56f));
            cottageRoofMaterial = NewMaterial(shader, new Color(.68f, .33f, .24f));
            routeGlowMaterial = NewMaterial(shader, new Color(.09f, .82f, 1f));
            energyGoldMaterial = NewMaterial(shader, new Color(1f, .68f, .11f));
            if (energyGoldMaterial.HasProperty("_Metallic")) energyGoldMaterial.SetFloat("_Metallic", .48f);
            if (energyGoldMaterial.HasProperty("_Glossiness")) energyGoldMaterial.SetFloat("_Glossiness", .83f);
            if (energyGoldMaterial.HasProperty("_EmissionColor"))
            {
                energyGoldMaterial.EnableKeyword("_EMISSION");
                energyGoldMaterial.SetColor("_EmissionColor", new Color(.27f, .12f, .015f));
            }
            boostTrailMaterial = NewMaterial(shader, new Color(.55f, .96f, 1f));
        }

        private static Material NewTerrainMaterial(Shader shader, Color baseColor, Color variation, float noiseScale, float smoothness)
        {
            var material = new Material(shader) { color = baseColor };
            if (material.HasProperty("_SecondaryColor")) material.SetColor("_SecondaryColor", variation);
            if (material.HasProperty("_NoiseScale")) material.SetFloat("_NoiseScale", noiseScale);
            if (material.HasProperty("_Glossiness")) material.SetFloat("_Glossiness", smoothness);
            return material;
        }

        private static void ApplyGroundTexture(Material material, string textureName, float scale, float strength)
        {
            var texture = Resources.Load<Texture2D>($"Art/Textures/Environment/{textureName}");
            if (texture == null || !material.HasProperty("_MainTex")) return;
            texture.wrapMode = TextureWrapMode.Repeat;
            material.SetTexture("_MainTex", texture);
            material.SetFloat("_TextureScale", scale);
            material.SetFloat("_TextureStrength", strength);
        }

        private static Material NewMaterial(Shader shader, Color color)
        {
            var material = new Material(shader) { color = color };
            if (material.HasProperty("_EmissionColor"))
            {
                material.EnableKeyword("_EMISSION");
                material.SetColor("_EmissionColor", color * .45f);
            }
            return material;
        }

        private void CreateCameraAndLight()
        {
            var cameraObject = new GameObject("Ride Camera");
            cameraObject.transform.SetParent(transform, false);
            cameraObject.transform.position = new Vector3(.62f, 2.62f, -9.25f);
            cameraObject.transform.rotation = Quaternion.LookRotation(
                new Vector3(0, 1.15f, 9f) - cameraObject.transform.position,
                Vector3.up);
            rideCamera = cameraObject.AddComponent<Camera>();
            rideCamera.fieldOfView = 55f;
            rideCamera.clearFlags = CameraClearFlags.Skybox;
            rideCamera.backgroundColor = targetSky;
            rideCamera.farClipPlane = 540f;

            var panoramaName = selectedLevel == 0 ? "TahoePanorama" :
                selectedLevel == 1 ? "CanyonPanorama" : "VillagePanorama";
            var skyTemplate = Resources.Load<Material>($"Art/Materials/Skies/{panoramaName}Sky");
            if (Shader.Find("WindTrace/Reference Sky") is Shader referenceSky)
            {
                skyMaterial = new Material(referenceSky);
                RenderSettings.skybox = skyMaterial;
            }
            else if (skyTemplate != null)
            {
                skyMaterial = new Material(skyTemplate);
                RenderSettings.skybox = skyMaterial;
            }
            else if (Shader.Find("Skybox/Procedural") is Shader skyShader)
            {
                Debug.LogWarning($"SKY_MATERIAL_MISSING {panoramaName}; using procedural fallback");
                skyMaterial = new Material(skyShader);
                skyMaterial.SetColor("_SkyTint", new Color(.24f, .55f, .86f));
                skyMaterial.SetColor("_GroundColor", new Color(.26f, .38f, .3f));
                skyMaterial.SetFloat("_AtmosphereThickness", .72f);
                skyMaterial.SetFloat("_Exposure", .96f);
                RenderSettings.skybox = skyMaterial;
            }

            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(.58f, .76f, .92f);
            RenderSettings.ambientEquatorColor = new Color(.42f, .57f, .48f);
            RenderSettings.ambientGroundColor = new Color(.18f, .22f, .12f);
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogColor = new Color(.55f, .72f, .84f);
            RenderSettings.fogStartDistance = 150f;
            RenderSettings.fogEndDistance = 480f;

            var lightObject = new GameObject("Dawn Light");
            lightObject.transform.SetParent(transform, false);
            lightObject.transform.rotation = Quaternion.Euler(48f, -28f, 0);
            var light = lightObject.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.08f;
            light.color = new Color(1f, .9f, .72f);
            light.shadows = LightShadows.Soft;
        }

        private void CreateRoad()
        {
            var root = new GameObject("Reference ride landscape");
            root.transform.SetParent(transform, false);
            referenceEnvironment = root.AddComponent<ReferenceRideEnvironment>();
            referenceEnvironment.Initialize(selectedLevel, activeRouteLength + 320f, distance =>
            {
                var frame = RouteAt(distance);
                return new ReferenceRideEnvironment.Frame
                { Center = frame.center, Right = frame.side, Forward = frame.tangent };
            });
            CreateRouteEffects();
            CreateFinishGate();
        }

        private void CreateLegacyRoad()
        {
            if (geoRoutePoints != null)
            {
                CreateGeoWorld();
                return;
            }
            var roadSurface = new List<Transform>();
            var rutSurface = new List<Transform>();
            var edgeSurface = new List<Transform>();
            var grassSurface = new List<Transform>();
            var lakeSurface = new List<Transform>();
            // Build one continuous route for the selected ride; the scenery
            // pool moves forward rather than repeating the route or turning back.
            var routeChunks = Mathf.CeilToInt((activeRouteLength + 120f) / RouteChunkLength) + 1;
            for (var index = 0; index < routeChunks; index++)
            {
                var z = index * RouteChunkLength - RouteChunkLength;
                roadSurface.Add(CreateRouteRibbon($"Road {index:00}", z, -2.4f, 2.4f, .02f, roadMaterial));
                edgeSurface.Add(CreateRouteRibbon($"Left road edge {index:00}", z, -2.53f, -2.34f, .055f, edgeMaterial));
                edgeSurface.Add(CreateRouteRibbon($"Right road edge {index:00}", z, 2.34f, 2.53f, .055f, edgeMaterial));

                rutSurface.Add(CreateRouteRibbon($"Left wheel rut {index:00}", z, -.94f, -.77f, .028f, rutMaterial));
                rutSurface.Add(CreateRouteRibbon($"Right wheel rut {index:00}", z, .77f, .94f, .028f, rutMaterial));

                var leftBank = selectedLevel == 2 ? -5.3f : -8.5f;
                grassSurface.Add(CreateRouteRibbon($"Left meadow {index:00}", z, leftBank, -2.53f, -.16f, grassMaterial));
                grassSurface.Add(CreateRouteRibbon($"Right meadow {index:00}", z, 2.53f, 22f, -.16f, grassMaterial));
                if (selectedLevel == 1)
                    lakeSurface.Add(CreateRouteRibbon($"Canyon river {index:00}", z, -33f, -8.5f, -.22f, lakeMaterial));
                else if (selectedLevel == 2)
                    lakeSurface.Add(CreateRouteRibbon($"Village brook {index:00}", z, -17f, -5.3f, -.22f, lakeMaterial));
            }
            roadTiles.AddRange(CombineRoutePieceBatches("Road surface", roadSurface, roadMaterial));
            roadTiles.AddRange(CombineRoutePieceBatches("Wheel ruts", rutSurface, rutMaterial));
            roadTiles.AddRange(CombineRoutePieceBatches("Road markings", edgeSurface, edgeMaterial));
            roadTiles.AddRange(CombineRoutePieceBatches("Meadow surface", grassSurface, grassMaterial));
            roadTiles.AddRange(CombineRoutePieceBatches("Lake surface", lakeSurface, lakeMaterial));

            for (var index = 0; index < 12; index++)
            {
                CreateScenery(index, -1);
                CreateScenery(index, 1);
            }
            if (selectedLevel == 2)
            {
                CreateDistantForest();
                CreateLongRouteForest();
            }
            // The panoramic sky carries detailed clouds; mesh puffs looked like white polygons.
            // The previous Blender landmark set is intentionally disabled while its
            // art direction is being redesigned to match the ride environments.
            CreateRouteEffects();
            CreateFinishGate();

            if (selectedLevel == 2)
            {
                mountainBackdrop = InstantiateArt("Art/Models/HiFi/LakesideMountainHiFi", "Village mountain range");
                if (mountainBackdrop != null)
                {
                    mountainBackdrop.localPosition = new Vector3(0, -1.4f, 82f);
                    mountainBackdrop.localRotation = Quaternion.Euler(0, 180f, 0);
                    mountainBackdrop.localScale *= .9f;
                }
            }
        }

        private void CreateGeoWorld()
        {
            var terrainRoot = new GameObject("Tahoe West Shore terrain");
            terrainRoot.transform.SetParent(transform, false);
            for (var tileY = 0; tileY < 2; tileY++)
            {
                for (var tileX = 0; tileX < 2; tileX++)
                {
                    var path = $"Art/Models/Geo/TahoeWestShore/TahoeTerrain_{tileX}_{tileY}";
                    var prefab = Resources.Load<GameObject>(path);
                    if (prefab == null)
                        throw new MissingReferenceException($"Missing generated terrain tile: {path}");
                    var tile = Instantiate(prefab, terrainRoot.transform, false);
                    tile.name = $"USGS terrain {tileX},{tileY}";
                    foreach (var renderer in tile.GetComponentsInChildren<MeshRenderer>(true))
                        renderer.sharedMaterial = grassMaterial;
                }
            }
            BuildTerrainHeightGrid(terrainRoot);

            // Lake Tahoe is east of this route. The 3DEP mesh contains a flat lake
            // surface; a shallow water layer supplies the game material and hides
            // the green terrain material in that portion of the crop.
            var lake = CreatePrimitive(PrimitiveType.Cube, "Lake Tahoe water", lakeMaterial);
            lake.position = new Vector3(3350f, 17.2f, -800f);
            lake.localScale = new Vector3(4900f, .08f, 12600f);
            var lakeCollider = lake.GetComponent<Collider>();
            if (lakeCollider != null) Destroy(lakeCollider);

            var roadSurface = new List<Transform>();
            var routeChunks = Mathf.CeilToInt(geoRouteSpan / RouteChunkLength);
            for (var index = 0; index < routeChunks; index++)
            {
                var start = index * RouteChunkLength;
                var length = Mathf.Min(RouteChunkLength, geoRouteSpan - start);
                roadSurface.Add(CreateRouteRibbon($"Real cycleway {index:00}", start, -2.15f, 2.15f, .055f, roadMaterial, length));
            }
            roadTiles.AddRange(CombineRoutePieceBatches("Tahoe cycleway", roadSurface, roadMaterial));

            for (var index = 0; index < 18; index++)
            {
                CreateScenery(index, -1);
                CreateScenery(index, 1);
            }
            CreateDistantForest();
            // Keep a dense, recycled corridor of trees and trail dressing around
            // the rider for the full measured route, not only at the start point.
            CreateLongRouteForest();
            // The panoramic sky carries detailed clouds; mesh puffs looked like white polygons.
            CreateRouteEffects();
            CreateFinishGate();
        }

        private static void BuildTerrainHeightGrid(GameObject terrainRoot)
        {
            var filters = terrainRoot.GetComponentsInChildren<MeshFilter>(true);
            var hasBounds = false;
            var bounds = new Bounds();
            foreach (var filter in filters)
            {
                var renderer = filter.GetComponent<MeshRenderer>();
                if (renderer == null || filter.sharedMesh == null) continue;
                if (!hasBounds) { bounds = renderer.bounds; hasBounds = true; }
                else bounds.Encapsulate(renderer.bounds);
            }
            if (!hasBounds) return;

            var heights = new float[TerrainGridCells + 1, TerrainGridCells + 1];
            for (var row = 0; row <= TerrainGridCells; row++)
                for (var column = 0; column <= TerrainGridCells; column++)
                    heights[row, column] = float.NegativeInfinity;

            foreach (var filter in filters)
            {
                if (filter.sharedMesh == null) continue;
                foreach (var local in filter.sharedMesh.vertices)
                {
                    var vertex = filter.transform.TransformPoint(local);
                    var column = Mathf.Clamp(Mathf.RoundToInt((vertex.x - bounds.min.x) / bounds.size.x * TerrainGridCells), 0, TerrainGridCells);
                    var row = Mathf.Clamp(Mathf.RoundToInt((vertex.z - bounds.min.z) / bounds.size.z * TerrainGridCells), 0, TerrainGridCells);
                    heights[row, column] = Mathf.Max(heights[row, column], vertex.y);
                }
            }
            terrainHeightGrid = heights;
            terrainGridBounds = bounds;
        }

        private static float TerrainHeightAt(float x, float z)
        {
            if (terrainHeightGrid == null) return float.NegativeInfinity;
            var column = Mathf.Clamp((x - terrainGridBounds.min.x) / terrainGridBounds.size.x * TerrainGridCells, 0f, TerrainGridCells - .001f);
            var row = Mathf.Clamp((z - terrainGridBounds.min.z) / terrainGridBounds.size.z * TerrainGridCells, 0f, TerrainGridCells - .001f);
            var x0 = Mathf.FloorToInt(column);
            var z0 = Mathf.FloorToInt(row);
            var a = Mathf.Lerp(terrainHeightGrid[z0, x0], terrainHeightGrid[z0, x0 + 1], column - x0);
            var b = Mathf.Lerp(terrainHeightGrid[z0 + 1, x0], terrainHeightGrid[z0 + 1, x0 + 1], column - x0);
            return Mathf.Lerp(a, b, row - z0);
        }

        private void CreateDistantForest()
        {
            for (var index = 0; index < 24; index++)
            {
                var distance = 18f + index * 11f;
                var side = selectedLevel == 2 ? 1 : index % 4 == 0 ? -1 : 1;
                var path = TreeModelPath(index, false);
                var tree = InstantiateArt(path, $"Distant forest {index:00}");
                if (tree == null) continue;
                var route = RouteAt(distance);
                var lateral = side < 0 ? -7.5f - index % 3 * 1.5f :
                    10.5f + index % 5 * 3.4f;
                tree.position = route.center + route.side * lateral - route.up * .12f;
                tree.localRotation = Quaternion.Euler(0, index * 47f % 360f, 0) * tree.localRotation;
                tree.localScale *= .56f + index % 4 * .1f;
                AddMobileDistanceCulling(tree);
                scenery.Add(tree);
                RegisterStreamedScenery(tree, distance, lateral);
            }
        }

        private void CreateThemedLandmarks()
        {
            for (var index = 0; index < 10; index++)
            {
                var canyon = InstantiateArt("Art/Models/Themed/CanyonWindTurbine",
                    $"Canyon wind turbine {index:00}");
                if (canyon != null)
                {
                    RegisterLandmark(canyon, canyonLandmarks, 10.5f + index % 3 * 1.35f, 90f);
                    var rotor = FindNamedChild(canyon, "Rotor");
                    if (rotor != null) windmillRotors.Add(rotor);
                    AddMobileDistanceCulling(canyon);
                }

                var cottage = InstantiateArt("Art/Models/Themed/CloudVillageCottage",
                    $"Cloud village home {index:00}");
                if (cottage != null)
                {
                    cottage.localScale *= .86f + index % 3 * .06f;
                    RegisterLandmark(cottage, storybookLandmarks, 7.7f + index % 3 * 1.55f, 90f);
                    AddMobileDistanceCulling(cottage);
                }

                if (index % 2 != 0) continue;
                var windmill = InstantiateArt("Art/Models/Themed/CloudVillageWindmill",
                    $"Cloud village windmill {index / 2:00}");
                if (windmill == null) continue;
                RegisterLandmark(windmill, storybookLandmarks, 14.2f + index % 4 * .8f, 90f);
                var villageRotor = FindNamedChild(windmill, "Rotor");
                if (villageRotor != null) windmillRotors.Add(villageRotor);
                AddMobileDistanceCulling(windmill);
            }
            RepositionLandmarks(canyonLandmarks);
            RepositionLandmarks(storybookLandmarks);
        }

        private void RegisterLandmark(Transform instance, List<Transform> group, float lateral, float yawOffset)
        {
            group.Add(instance);
            landmarkLaterals[instance] = lateral;
            landmarkYawOffsets[instance] = yawOffset;
            // FBX roots carry Blender-to-Unity's Z-up -> Y-up correction. Preserve that
            // authored rotation when positioning the landmark instead of flattening it.
            landmarkAxisOffsets[instance] = instance.localRotation;
        }

        private Transform NewLandmarkRoot(string name)
        {
            var root = new GameObject(name).transform;
            root.SetParent(transform, false);
            return root;
        }

        private void CreateCanyonWindTurbine(Transform parent, float side)
        {
            var x = side * 9.5f;
            CreateTaperedLandmark(parent, "Tapered turbine tower", new Vector3(x, 0f, -2f),
                8.8f, .52f, .27f, 16, cottageWallMaterial);
            var nacelle = LandmarkPart(parent, PrimitiveType.Capsule, "Turbine nacelle", cottageWallMaterial,
                new Vector3(x, 8.9f, -2.05f), new Vector3(.43f, .78f, .43f));
            nacelle.localRotation = Quaternion.Euler(90f, 0f, 0f);
            var rotor = NewLandmarkRoot("Turbine rotor");
            rotor.SetParent(parent, false);
            rotor.localPosition = new Vector3(x, 8.9f, -2.55f);
            LandmarkPart(rotor, PrimitiveType.Sphere, "Turbine hub", cottageWallMaterial,
                Vector3.zero, Vector3.one * .48f);
            for (var blade = 0; blade < 3; blade++)
                CreateTurbineBlade(rotor, blade * 120f);
            windmillRotors.Add(rotor);
        }

        private void CreateTurbineBlade(Transform rotor, float angle)
        {
            var blade = new GameObject("Swept turbine blade").transform;
            blade.SetParent(rotor, false);
            blade.localRotation = Quaternion.Euler(0f, 0f, angle);
            var outline = new[]
            {
                new Vector2(-.16f, .28f), new Vector2(-.4f, 1.12f),
                new Vector2(-.52f, 2.45f), new Vector2(-.17f, 4.05f),
                new Vector2(.04f, 4.2f), new Vector2(.2f, 2.45f),
                new Vector2(.17f, .9f)
            };
            var count = outline.Length;
            var vertices = new Vector3[count * 2];
            var triangles = new List<int>();
            for (var i = 0; i < count; i++)
            {
                vertices[i] = new Vector3(outline[i].x, outline[i].y, -.055f);
                vertices[i + count] = new Vector3(outline[i].x, outline[i].y, .055f);
                var next = (i + 1) % count;
                triangles.Add(i); triangles.Add(next); triangles.Add(i + count);
                triangles.Add(next); triangles.Add(next + count); triangles.Add(i + count);
            }
            for (var i = 1; i < count - 1; i++)
            {
                triangles.Add(0); triangles.Add(i); triangles.Add(i + 1);
                triangles.Add(count); triangles.Add(count + i + 1); triangles.Add(count + i);
            }
            var mesh = new Mesh { name = "Swept turbine blade mesh" };
            mesh.vertices = vertices;
            mesh.triangles = triangles.ToArray();
            mesh.RecalculateNormals();
            blade.gameObject.AddComponent<MeshFilter>().sharedMesh = mesh;
            blade.gameObject.AddComponent<MeshRenderer>().sharedMaterial = cottageWallMaterial;
        }

        private void CreateVillageWindmill(Transform parent, float side)
        {
            var x = 15f;
            var basePosition = new Vector3(x, 0f, 1f);
            CreateTaperedLandmark(parent, "Stone windmill tower", basePosition,
                6.0f, 1.32f, .78f, 12, cottageWallMaterial);
            CreateTaperedLandmark(parent, "Windmill conical roof", basePosition + Vector3.up * 6f,
                1.45f, 1.05f, .07f, 12, cottageRoofMaterial);
            LandmarkPart(parent, PrimitiveType.Cube, "Windmill timber door", cottageRoofMaterial,
                new Vector3(x, .95f, -.32f), new Vector3(.75f, 1.65f, .12f));
            var rotor = NewLandmarkRoot("Village windmill rotor");
            rotor.SetParent(parent, false);
            rotor.localPosition = new Vector3(x, 5.65f, -.45f);
            LandmarkPart(rotor, PrimitiveType.Cylinder, "Timber rotor axle", cottageRoofMaterial,
                Vector3.zero, new Vector3(.3f, .26f, .3f)).localRotation = Quaternion.Euler(90f, 0f, 0f);
            for (var blade = 0; blade < 4; blade++)
            {
                var sail = NewLandmarkRoot("Timber framed sail");
                sail.SetParent(rotor, false);
                sail.localRotation = Quaternion.Euler(0f, 0f, blade * 90f);
                LandmarkPart(sail, PrimitiveType.Cube, "Sail spar", cottageRoofMaterial,
                    new Vector3(0f, 1.65f, 0f), new Vector3(.13f, 3.15f, .13f));
                LandmarkPart(sail, PrimitiveType.Cube, "Canvas sail", cottageWallMaterial,
                    new Vector3(.36f, 1.95f, .04f), new Vector3(.62f, 1.8f, .035f));
                for (var rung = 0; rung < 4; rung++)
                    LandmarkPart(sail, PrimitiveType.Cube, "Sail timber batten", cottageRoofMaterial,
                        new Vector3(.36f, 1.3f + rung * .42f, -.01f), new Vector3(.7f, .045f, .07f));
            }
            windmillRotors.Add(rotor);
        }

        private void CreateTaperedLandmark(Transform parent, string name, Vector3 bottom,
            float height, float bottomRadius, float topRadius, int segments, Material material)
        {
            var vertices = new Vector3[segments * 2 + 2];
            var triangles = new List<int>();
            for (var i = 0; i < segments; i++)
            {
                var angle = i * Mathf.PI * 2f / segments;
                var direction = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
                vertices[i] = direction * bottomRadius;
                vertices[i + segments] = direction * topRadius + Vector3.up * height;
                var next = (i + 1) % segments;
                triangles.Add(i); triangles.Add(i + segments); triangles.Add(next);
                triangles.Add(next); triangles.Add(i + segments); triangles.Add(next + segments);
            }
            vertices[segments * 2] = Vector3.zero;
            vertices[segments * 2 + 1] = Vector3.up * height;
            for (var i = 0; i < segments; i++)
            {
                var next = (i + 1) % segments;
                triangles.Add(segments * 2); triangles.Add(next); triangles.Add(i);
                triangles.Add(segments * 2 + 1); triangles.Add(i + segments); triangles.Add(next + segments);
            }
            var mesh = new Mesh { name = name + " mesh" };
            mesh.vertices = vertices;
            mesh.triangles = triangles.ToArray();
            mesh.RecalculateNormals();
            var part = new GameObject(name).transform;
            part.SetParent(parent, false);
            part.localPosition = bottom;
            part.gameObject.AddComponent<MeshFilter>().sharedMesh = mesh;
            part.gameObject.AddComponent<MeshRenderer>().sharedMaterial = material;
        }

        private void CreateSandstoneButte(Transform parent, float side, int seed)
        {
            const int segments = 11;
            var levels = new[] { 0f, 1.1f, 2.4f, 3.55f, 4.8f, 5.5f };
            var radii = new[] { 3.8f, 4.1f, 3.6f, 3.75f, 2.95f, 2.5f };
            var vertices = new Vector3[levels.Length * segments + 1];
            var triangles = new List<int>();
            for (var ring = 0; ring < levels.Length; ring++)
                for (var segment = 0; segment < segments; segment++)
                {
                    var angle = segment * Mathf.PI * 2f / segments;
                    var variation = 1f + Mathf.Sin(seed * 3.7f + segment * 4.1f + ring * .9f) * .12f;
                    var radius = radii[ring] * variation;
                    vertices[ring * segments + segment] = new Vector3(
                        side * 12f + Mathf.Cos(angle) * radius,
                        levels[ring],
                        .45f + Mathf.Sin(angle) * radius * .82f);
                    if (ring == levels.Length - 1) continue;
                    var next = (segment + 1) % segments;
                    var a = ring * segments + segment;
                    var b = ring * segments + next;
                    var c = (ring + 1) * segments + segment;
                    var d = (ring + 1) * segments + next;
                    triangles.Add(a); triangles.Add(c); triangles.Add(b);
                    triangles.Add(b); triangles.Add(c); triangles.Add(d);
                }
            var cap = vertices.Length - 1;
            vertices[cap] = new Vector3(side * 12f, levels[levels.Length - 1], .45f);
            for (var segment = 0; segment < segments; segment++)
            {
                triangles.Add(cap);
                triangles.Add((levels.Length - 1) * segments + (segment + 1) % segments);
                triangles.Add((levels.Length - 1) * segments + segment);
            }
            var mesh = new Mesh { name = "Layered sandstone butte" };
            mesh.vertices = vertices;
            mesh.triangles = triangles.ToArray();
            mesh.RecalculateNormals();
            var rock = new GameObject("Layered sandstone butte");
            rock.transform.SetParent(parent, false);
            rock.AddComponent<MeshFilter>().sharedMesh = mesh;
            rock.AddComponent<MeshRenderer>().sharedMaterial = canyonRockMaterial;
        }

        private Transform LandmarkPart(Transform root, PrimitiveType shape, string name, Material material,
            Vector3 position, Vector3 scale)
        {
            var part = CreatePrimitive(shape, name, material);
            part.SetParent(root, false);
            part.localPosition = position;
            part.localScale = scale;
            return part;
        }

        private void RepositionLandmarks(List<Transform> landmarks)
        {
            var spacing = landmarks == storybookLandmarks ? 27f : 39f;
            for (var i = 0; i < landmarks.Count; i++)
                PositionLandmark(landmarks[i], worldDistance + 32f + i * spacing);
        }

        private void PositionLandmark(Transform item, float distance)
        {
            var frame = RouteAt(distance);
            var lateral = landmarkLaterals.TryGetValue(item, out var storedLateral) ? storedLateral : 8f;
            var yaw = landmarkYawOffsets.TryGetValue(item, out var storedYaw) ? storedYaw : 0f;
            var axisOffset = landmarkAxisOffsets.TryGetValue(item, out var storedAxisOffset)
                ? storedAxisOffset
                : Quaternion.identity;
            item.position = frame.center + frame.side * lateral +
                            frame.up * SyntheticGroundRise(distance, lateral);
            item.rotation = Quaternion.LookRotation(frame.tangent, frame.up) *
                            Quaternion.Euler(0f, yaw, 0f) * axisOffset;
        }

        private static float SyntheticGroundRise(float distance, float lateral)
        {
            if (Mathf.Abs(lateral) <= 3f) return 0f;
            return lateral > 3f
                ? (lateral - 3f) * .065f + Mathf.Sin(distance * .075f + lateral * .24f) * .18f
                : Mathf.Sin(distance * .09f + lateral * .28f) * .08f;
        }

        private void UpdateThemedLandmarks()
        {
            var landmarks = visualPhase == RidePhaseKind.CadenceChallenge ||
                            visualPhase == RidePhaseKind.Recovery || visualPhase == RidePhaseKind.Sprint
                ? canyonLandmarks : storybookLandmarks;
            for (var i = 0; i < landmarks.Count; i++)
            {
                var item = landmarks[i];
                if (!item.gameObject.activeSelf || item.position.z >= worldDistance - 30f) continue;
                var distance = worldDistance + 320f + i * 5f;
                PositionLandmark(item, distance);
            }
        }

        private void CreateRouteEffects()
        {
            for (var i = 0; i < 24; i++)
            {
                var streak = CreatePrimitive(PrimitiveType.Cube, $"Boost road streak {i:00}", boostTrailMaterial);
                streak.gameObject.SetActive(false);
                boostTrails.Add(streak);
            }
        }

        private void UpdateRouteEffects()
        {
            var boost = snapshot != null && snapshot.WindBoostActive;
            for (var i = 0; i < boostTrails.Count; i++)
            {
                var streak = boostTrails[i];
                if (streak.gameObject.activeSelf != boost) streak.gameObject.SetActive(boost);
                if (!boost) continue;
                var distance = Mathf.Min(FinishDistance, worldDistance + 5f + (i / 2) * 6f);
                var frame = RouteAt(distance);
                var side = i % 2 == 0 ? -1f : 1f;
                streak.position = frame.center + frame.side * side * 1.42f + frame.up * .13f;
                streak.rotation = Quaternion.LookRotation(frame.tangent, frame.up);
                var pulse = .75f + .25f * Mathf.Sin(Time.unscaledTime * 13f + i * .8f);
                streak.localScale = new Vector3(.14f * pulse, .035f, 2.7f + pulse);
            }
        }

        private void ApplyPhaseEnvironment(RidePhaseKind phase)
        {
            if (referenceEnvironment != null)
            {
                // Each course keeps its authored identity through workout phases.
                RenderSettings.fogColor = selectedLevel == 1 ? new Color(.63f,.73f,.77f) :
                    new Color(.66f,.78f,.82f);
                RenderSettings.ambientSkyColor = new Color(.58f,.72f,.83f);
                RenderSettings.ambientEquatorColor = new Color(.43f,.48f,.36f);
                RenderSettings.ambientGroundColor = new Color(.18f,.19f,.13f);
                return;
            }
            var canyon = phase == RidePhaseKind.CadenceChallenge || phase == RidePhaseKind.Recovery ||
                         phase == RidePhaseKind.Sprint;
            var storybook = phase == RidePhaseKind.Boss || phase == RidePhaseKind.Cooldown;
            var realTerrain = geoRoutePoints != null;
            foreach (var landmark in canyonLandmarks) landmark.gameObject.SetActive(canyon);
            foreach (var landmark in storybookLandmarks) landmark.gameObject.SetActive(storybook);
            if (canyon) RepositionLandmarks(canyonLandmarks);
            if (storybook) RepositionLandmarks(storybookLandmarks);
            foreach (var item in scenery)
            {
                if (item == null) continue;
                var tree = item.name.Contains("tree") || item.name.Contains("forest");
                if (tree) item.gameObject.SetActive(realTerrain || !canyon);
            }
            if (mountainBackdrop != null) mountainBackdrop.gameObject.SetActive(!canyon);
            if (realTerrain)
            {
                // Stage changes tint the real location subtly. The Tahoe forest,
                // lake and USGS relief remain recognizable throughout the ride.
                grassMaterial.color = storybook ? new Color(.12f, .35f, .13f) :
                    canyon ? new Color(.13f, .31f, .12f) : new Color(.48f, .69f, .27f);
                if (grassMaterial.HasProperty("_SecondaryColor"))
                    grassMaterial.SetColor("_SecondaryColor", storybook ? new Color(.38f, .64f, .24f) :
                        canyon ? new Color(.23f, .45f, .17f) : new Color(.66f, .82f, .38f));
                lakeMaterial.color = storybook ? new Color(.025f, .18f, .25f) :
                    canyon ? new Color(.02f, .17f, .24f) : new Color(.02f, .19f, .27f);
                lakeMaterial.SetColor("_SecondaryColor", storybook ? new Color(.23f, .42f, .46f) :
                    canyon ? new Color(.22f, .43f, .49f) : new Color(.21f, .43f, .49f));
                roadMaterial.color = storybook ? new Color(.43f, .34f, .24f) :
                    canyon ? new Color(.38f, .34f, .28f) : new Color(.39f, .31f, .22f);
                RenderSettings.fogColor = storybook ? new Color(.64f, .73f, .82f) :
                    canyon ? new Color(.51f, .67f, .78f) : new Color(.55f, .72f, .84f);
                RenderSettings.ambientSkyColor = storybook ? new Color(.69f, .77f, .86f) :
                    canyon ? new Color(.58f, .75f, .89f) : new Color(.58f, .76f, .92f);
                if (skyMaterial != null && skyMaterial.HasProperty("_SkyTint"))
                {
                    skyMaterial.SetColor("_SkyTint", storybook ? new Color(.45f, .57f, .76f) :
                        canyon ? new Color(.28f, .56f, .79f) : new Color(.24f, .55f, .86f));
                    skyMaterial.SetColor("_GroundColor", storybook ? new Color(.31f, .39f, .32f) :
                        canyon ? new Color(.28f, .37f, .3f) : new Color(.26f, .38f, .3f));
                }
                routeGlowMaterial.color = storybook ? new Color(.5f, 1f, .38f) : new Color(.09f, .82f, 1f);
                return;
            }
            grassMaterial.color = canyon ? new Color(.43f, .23f, .12f) :
                storybook ? new Color(.22f, .53f, .18f) : new Color(.035f, .24f, .045f);
            if (grassMaterial.HasProperty("_SecondaryColor"))
                grassMaterial.SetColor("_SecondaryColor", canyon ? new Color(.67f, .42f, .24f) :
                    storybook ? new Color(.38f, .64f, .24f) : new Color(.24f, .54f, .105f));
            lakeMaterial.color = canyon ? new Color(.018f, .15f, .22f) :
                storybook ? new Color(.025f, .18f, .25f) : new Color(.012f, .17f, .24f);
            lakeMaterial.SetColor("_SecondaryColor", canyon ? new Color(.23f, .45f, .5f) :
                storybook ? new Color(.22f, .43f, .47f) : new Color(.2f, .42f, .48f));
            roadMaterial.color = canyon ? new Color(.37f, .34f, .32f) :
                storybook ? new Color(.54f, .35f, .2f) : new Color(.38f, .21f, .095f);
            RenderSettings.fogColor = canyon ? new Color(.47f, .62f, .78f) :
                storybook ? new Color(.72f, .76f, .91f) : new Color(.55f, .72f, .84f);
            RenderSettings.ambientSkyColor = canyon ? new Color(.53f, .77f, .98f) :
                storybook ? new Color(.91f, .78f, .94f) : new Color(.58f, .76f, .92f);
            if (skyMaterial != null && skyMaterial.HasProperty("_SkyTint"))
            {
                skyMaterial.SetColor("_SkyTint", canyon ? new Color(.15f, .61f, .92f) :
                    storybook ? new Color(.69f, .65f, .92f) : new Color(.24f, .55f, .86f));
                skyMaterial.SetColor("_GroundColor", canyon ? new Color(.42f, .38f, .29f) :
                    storybook ? new Color(.52f, .53f, .42f) : new Color(.26f, .38f, .3f));
            }
            routeGlowMaterial.color = storybook ? new Color(.5f, 1f, .38f) : new Color(.09f, .82f, 1f);
        }

        private void CreateLongRouteForest()
        {
            for (var index = 0; index < 70; index++)
            {
                var distance = 220f + index * 12f;
                var side = selectedLevel == 2 ? 1f : index % 2 == 0 ? -1f : 1f;
                var path = TreeModelPath(index + 7, false);
                var tree = InstantiateArt(path, $"Long route forest {index:00}");
                if (tree == null) continue;
                var route = RouteAt(distance);
                var lateral = side * (6.2f + index % 4 * 1.55f);
                tree.position = route.center + route.side * lateral - route.up * .12f;
                tree.localRotation = Quaternion.Euler(0f, index * 53f % 360f, 0f) * tree.localRotation;
                tree.localScale *= .82f + index % 3 * .1f;
                AddMobileDistanceCulling(tree);
                scenery.Add(tree);
                RegisterStreamedScenery(tree, distance, lateral);

                if (index % 2 == 0)
                {
                    var companionPath = TreeModelPath(index + 19, false);
                    var companion = InstantiateArt(companionPath, $"Long route companion tree {index:00}");
                    if (companion != null)
                    {
                        var companionRoute = RouteAt(distance + 9f);
                        var companionLateral = selectedLevel == 2
                            ? 12f + index % 3 * 1.2f : -side * (7.1f + index % 3 * 1.2f);
                        companion.position = companionRoute.center + companionRoute.side * companionLateral - companionRoute.up * .12f;
                        companion.localRotation = Quaternion.Euler(0f, index * 71f % 360f, 0f) * companion.localRotation;
                        companion.localScale *= .76f + index % 3 * .09f;
                        AddMobileDistanceCulling(companion);
                        scenery.Add(companion);
                        RegisterStreamedScenery(companion, distance + 9f, companionLateral);
                    }
                }

                if (distance < 280f) continue;
                if (index % 2 == 0 && (selectedLevel != 2 || side > 0f))
                    PlaceRouteModule("Art/Models/HiFi/LakesideFenceModule", $"Long route fence {index:00}", distance + 5f, side * 7.2f, .92f, true);
                if (index % 3 == 0)
                    PlaceRouteModule("Art/Models/HiFi/LakesideFlowerPatch", $"Long route flowers {index:00}", distance + 11f, side * 3.65f, .72f, true);
            }
        }

        private void CreateFinishGate()
        {
            var frame = RouteAt(FinishDistance);
            var levelForward = new Vector3(frame.tangent.x, 0f, frame.tangent.z).normalized;
            var levelRight = Vector3.Cross(Vector3.up, levelForward).normalized;
            var gateRotation = Quaternion.LookRotation(levelForward, Vector3.up);
            for (var side = -1; side <= 1; side += 2)
            {
                var post = CreatePrimitive(PrimitiveType.Cube, $"Finish gate post {side}", edgeMaterial);
                post.position = frame.center + levelRight * side * 3.05f + Vector3.up * 1.75f;
                post.rotation = gateRotation;
                post.localScale = new Vector3(.22f, 3.5f, .22f);
                scenery.Add(post);
            }
            var top = CreatePrimitive(PrimitiveType.Cube, "Finish gate top", cloudMaterial);
            top.position = frame.center + Vector3.up * 3.45f;
            top.rotation = gateRotation;
            top.localScale = new Vector3(6.35f, .34f, .28f);
            scenery.Add(top);

            var label = new GameObject("Finish label");
            label.transform.SetParent(transform, false);
            label.transform.position = frame.center + Vector3.up * 3.42f - levelForward * .19f;
            label.transform.rotation = gateRotation;
            var text = label.AddComponent<TextMesh>();
            text.text = "FINISH";
            text.anchor = TextAnchor.MiddleCenter;
            text.alignment = TextAlignment.Center;
            text.fontSize = 72;
            text.characterSize = .12f;
            text.color = new Color(.15f, .08f, .025f);
            scenery.Add(label.transform);
        }

        private void CreateCloudBank()
        {
            for (var cluster = 0; cluster < 20; cluster++)
            {
                var pieces = new List<Transform>();
                var route = RouteAt(52f + cluster * 52f);
                var center = route.center + route.side * (cluster % 2 == 0 ? -21f : 19f) +
                             Vector3.up * (18f + cluster % 3 * 1.8f);
                for (var puff = 0; puff < 4; puff++)
                {
                    var cloud = CreateCloudPuff($"Cloud {cluster}-{puff}");
                    cloud.position = center + new Vector3((puff - 1.5f) * 2.4f, puff % 2 * 1.05f, puff * .38f);
                    cloud.localScale = new Vector3(3.6f + puff % 2, 1.45f + (puff + 1) % 2 * .55f, 1.9f);
                    pieces.Add(cloud);
                }
                var bank = CombineRoutePieces($"Modeled cloud cluster {cluster:00}", pieces, cloudMaterial);
                scenery.Add(bank);
                streamedClouds.Add(new StreamedCloud { Instance = bank, Distance = 52f + cluster * 52f });
            }
        }

        private Transform CreateCloudPuff(string objectName)
        {
            const int segments = 8;
            const int rings = 4;
            var vertices = new Vector3[(rings + 1) * segments];
            var triangles = new int[rings * segments * 6];
            for (var ring = 0; ring <= rings; ring++)
            {
                var latitude = Mathf.PI * ring / rings;
                var radius = Mathf.Sin(latitude);
                var height = Mathf.Cos(latitude);
                for (var segment = 0; segment < segments; segment++)
                {
                    var longitude = Mathf.PI * 2f * segment / segments;
                    vertices[ring * segments + segment] = new Vector3(
                        Mathf.Cos(longitude) * radius, height, Mathf.Sin(longitude) * radius);
                    if (ring == rings) continue;
                    var next = (segment + 1) % segments;
                    var triangle = (ring * segments + segment) * 6;
                    var currentVertex = ring * segments + segment;
                    var nextVertex = ring * segments + next;
                    triangles[triangle] = currentVertex;
                    triangles[triangle + 1] = (ring + 1) * segments + next;
                    triangles[triangle + 2] = nextVertex;
                    triangles[triangle + 3] = currentVertex;
                    triangles[triangle + 4] = (ring + 1) * segments + segment;
                    triangles[triangle + 5] = (ring + 1) * segments + next;
                }
            }
            var mesh = new Mesh { name = objectName + " mesh", vertices = vertices, triangles = triangles };
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            var created = new GameObject(objectName);
            created.transform.SetParent(transform, false);
            created.AddComponent<MeshFilter>().sharedMesh = mesh;
            created.AddComponent<MeshRenderer>().sharedMaterial = cloudMaterial;
            return created.transform;
        }

        private void CreateGroundDetail()
        {
            var vertices = new List<Vector3>();
            var triangles = new List<int>();
            for (var index = 0; index < 570; index++)
            {
                var distance = 1.5f + index * 1.9f;
                var route = RouteAt(distance);
                var side = index % 2 == 0 ? -1f : 1f;
                var scatter = Mathf.Abs(Mathf.Sin(index * 12.9898f) * 4.15f);
                var lateral = side * (2.82f + scatter);
                if (index % 9 == 0) lateral = selectedLevel == 2 ? 7.65f : -7.65f + Mathf.Sin(index) * .32f;
                if (selectedLevel == 2 && lateral < -4.4f) continue;
                var center = route.center + route.side * lateral + route.up * -.11f;
                var height = .24f + Mathf.Abs(Mathf.Sin(index * 2.17f)) * .34f;
                var width = .035f + index % 3 * .012f;
                AddGrassQuad(vertices, triangles, center, route.side * width, route.up * height);
                AddGrassQuad(vertices, triangles, center, route.tangent * width, route.up * height * .86f);
            }

            var mesh = new Mesh { name = "Modeled roadside grass mesh" };
            mesh.SetVertices(vertices);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            var created = new GameObject("Modeled roadside grass");
            created.transform.SetParent(transform, false);
            created.AddComponent<MeshFilter>().sharedMesh = mesh;
            created.AddComponent<MeshRenderer>().sharedMaterial = grassBladeMaterial;
            scenery.Add(created.transform);
        }

        private static void AddGrassQuad(List<Vector3> vertices, List<int> triangles, Vector3 center, Vector3 halfWidth, Vector3 height)
        {
            var first = vertices.Count;
            vertices.Add(center - halfWidth);
            vertices.Add(center + halfWidth);
            vertices.Add(center + height + halfWidth * .18f);
            vertices.Add(center + height - halfWidth * .18f);
            triangles.Add(first); triangles.Add(first + 2); triangles.Add(first + 1);
            triangles.Add(first); triangles.Add(first + 3); triangles.Add(first + 2);
            triangles.Add(first + 1); triangles.Add(first + 2); triangles.Add(first);
            triangles.Add(first + 2); triangles.Add(first + 3); triangles.Add(first);
        }

        private Transform CombineRoutePieces(string objectName, List<Transform> pieces, Material material)
        {
            var combine = pieces.Select(piece => new CombineInstance
            {
                mesh = piece.GetComponent<MeshFilter>().sharedMesh,
                transform = piece.localToWorldMatrix
            }).ToArray();
            var mesh = new Mesh { name = objectName + " mesh" };
            mesh.CombineMeshes(combine, true, true, false);
            var created = new GameObject(objectName);
            created.transform.SetParent(transform, false);
            created.AddComponent<MeshFilter>().sharedMesh = mesh;
            created.AddComponent<MeshRenderer>().sharedMaterial = material;
            foreach (var piece in pieces)
            {
                if (Application.isPlaying) Destroy(piece.gameObject);
                else DestroyImmediate(piece.gameObject);
            }
            return created.transform;
        }

        private IEnumerable<Transform> CombineRoutePieceBatches(string objectName, List<Transform> pieces, Material material)
        {
            const int sourcePiecesPerBatch = 3;
            var batch = 0;
            for (var start = 0; start < pieces.Count; start += sourcePiecesPerBatch)
            {
                var count = Mathf.Min(sourcePiecesPerBatch, pieces.Count - start);
                yield return CombineRoutePieces($"{objectName} {batch++:00}", pieces.GetRange(start, count), material);
            }
        }

        private Transform CreateRouteRibbon(string objectName, float start, float left, float right, float elevation, Material material, float length = RouteChunkLength)
        {
            const int divisions = 24;
            var across = material == grassMaterial ? 5 : material == lakeMaterial ? 6 : 1;
            var vertices = new Vector3[(divisions + 1) * (across + 1)];
            var uv = new Vector2[vertices.Length];
            var triangles = new int[divisions * across * 6];
            for (var index = 0; index <= divisions; index++)
            {
                var distance = start + length * index / divisions;
                var frame = RouteAt(distance);
                for (var cross = 0; cross <= across; cross++)
                {
                    var lateral = Mathf.Lerp(left, right, cross / (float)across);
                    if ((material == grassMaterial && left < -5f && cross == 0) ||
                        (material == lakeMaterial && right > -9f && cross == across))
                        lateral += Mathf.Sin(distance * .06f) * .72f + Mathf.Sin(distance * .19f) * .22f;
                    if (Mathf.Abs(lateral) > 2f && Mathf.Abs(lateral) < 3f)
                    {
                        var shoulderWander = Mathf.Sin(distance * .13f) * .12f +
                                             Mathf.Sin(distance * .31f + 1.2f) * .06f;
                        lateral += Mathf.Sign(lateral) * shoulderWander;
                    }
                    var terrainRise = 0f;
                    if (material == grassMaterial)
                    {
                        terrainRise = lateral > 3f
                            ? (lateral - 3f) * .065f + Mathf.Sin(distance * .075f + lateral * .24f) * .18f
                            : Mathf.Sin(distance * .09f + lateral * .28f) * .08f;
                    }
                    var waterWave = material == lakeMaterial
                        ? Mathf.Sin(distance * .49f + lateral * .36f) * .035f +
                          Mathf.Sin(distance * .17f - lateral * .52f) * .022f
                        : 0f;
                    vertices[index * (across + 1) + cross] =
                        frame.center + frame.side * lateral + frame.up * (elevation + terrainRise + waterWave);
                    uv[index * (across + 1) + cross] = new Vector2(cross / (float)across, distance * .035f);
                }
                if (index == divisions) continue;
                for (var cross = 0; cross < across; cross++)
                {
                    var triangle = (index * across + cross) * 6;
                    var vertex = index * (across + 1) + cross;
                    triangles[triangle] = vertex;
                    triangles[triangle + 1] = vertex + across + 2;
                    triangles[triangle + 2] = vertex + 1;
                    triangles[triangle + 3] = vertex;
                    triangles[triangle + 4] = vertex + across + 1;
                    triangles[triangle + 5] = vertex + across + 2;
                }
            }

            var mesh = new Mesh { name = objectName + " mesh" };
            mesh.vertices = vertices; mesh.uv = uv; mesh.triangles = triangles; mesh.RecalculateNormals(); mesh.RecalculateBounds();
            var created = new GameObject(objectName);
            created.transform.SetParent(transform, false);
            created.AddComponent<MeshFilter>().sharedMesh = mesh;
            created.AddComponent<MeshRenderer>().sharedMaterial = material;
            return created.transform;
        }

        private static RouteFrame RouteAt(float distance)
        {
            if(geoRoutePoints!=null && distance>activeRouteLength)
            {
                var end=RouteAt(activeRouteLength);
                end.center+=end.tangent*(distance-activeRouteLength);
                return end;
            }
            if (smoothGeoRoute != null)
            {
                distance = Mathf.Clamp(distance, 0f, activeRouteLength);
                var geoCenter=smoothGeoRoute.Position(distance);
                geoCenter.y = .70f + Mathf.Sin(distance*.003f)*.22f;
                var geoTangent=smoothGeoRoute.Forward(distance);
                var geoSide = Vector3.Cross(Vector3.up, geoTangent).normalized;
                return new RouteFrame { center = geoCenter, side = geoSide, up = Vector3.up, tangent = geoTangent };
            }
            Vector3 center;
            Vector3 derivative;
            if (activeLevel == 1)
            {
                center = new Vector3(38f * Mathf.Sin(distance * .010f) + 5f * Mathf.Sin(distance * .023f),
                    1.5f * Mathf.Sin(distance * .012f) + .45f * Mathf.Sin(distance * .035f), distance);
                derivative = new Vector3(.38f * Mathf.Cos(distance * .010f) + .115f * Mathf.Cos(distance * .023f),
                    .018f * Mathf.Cos(distance * .012f) + .01575f * Mathf.Cos(distance * .035f), 1f).normalized;
            }
            else
            {
                center = new Vector3(20f * Mathf.Sin(distance * .011f) + 5f * Mathf.Sin(distance * .031f),
                    .7f * Mathf.Sin(distance * .009f) + .45f * Mathf.Sin(distance * .029f), distance);
                derivative = new Vector3(.22f * Mathf.Cos(distance * .011f) + .155f * Mathf.Cos(distance * .031f),
                    .0063f * Mathf.Cos(distance * .009f) + .01305f * Mathf.Cos(distance * .029f), 1f).normalized;
            }
            var side = Vector3.Cross(Vector3.up, derivative).normalized;
            var up = Vector3.Cross(derivative, side).normalized;
            up = Quaternion.AngleAxis(Mathf.Sin(distance * .035f), derivative) * up;
            return new RouteFrame { center = center, side = side, up = up, tangent = derivative };
        }

        private struct RouteFrame { public Vector3 center; public Vector3 side; public Vector3 up; public Vector3 tangent; }

        private void CreateScenery(int index, int side)
        {
            if (selectedLevel == 2 && side < 0) return;
            var broadleaf = index % 3 == 0 || index % 5 == 0;
            var treePath = TreeModelPath(index + (side > 0 ? 3 : 0), index < 6);
            var tree = InstantiateArt(treePath, $"Lakeside tree {side} {index}");
            if (tree == null)
            {
                tree = CreatePrimitive(PrimitiveType.Cylinder, $"Wind tree {side} {index}", edgeMaterial);
                tree.localScale = new Vector3(.15f, 1.2f + index % 3 * .18f, .15f);
            }
            var scale = broadleaf
                ? .72f + index % 3 * .08f
                : .65f + index % 4 * .055f;
            tree.localScale *= scale;
            var treeDistance = side < 0 ? 5.3f + index % 3 * 1.35f : 5.2f + index % 4 * 1.25f;
            var route = RouteAt(index * 18f);
            tree.position = route.center + route.side * side * treeDistance - route.up * .1f;
            tree.localRotation = Quaternion.Euler(0, (index * 37 + side * 11) % 360, 0) * tree.localRotation;
            AddMobileDistanceCulling(tree);
            scenery.Add(tree);

            if (side > 0 && index == 2)
            {
                var sign = InstantiateArt("Art/Models/TrailSign", "Lakeside direction sign");
                if (sign != null)
                {
                    var signRoute = RouteAt(28f);
                    sign.position = signRoute.center + signRoute.side * 4.65f - signRoute.up * .08f;
                    sign.rotation = Quaternion.LookRotation(signRoute.tangent, signRoute.up) * sign.localRotation;
                    scenery.Add(sign);
                }
            }

            CreateRouteDressing(index, side);
        }

        private void CreateRouteDressing(int index, int side)
        {
            var distance = index * 18f + (side > 0 ? 4f : 0f);
            if ((side < 0 || index % 2 == 0) && (selectedLevel != 2 || side > 0))
                PlaceRouteModule("Art/Models/HiFi/LakesideFenceModule", $"Lakeside fence {side} {index}", distance, side * 7.2f, 1f);

            PlaceRouteModule("Art/Models/HiFi/LakesideFlowerPatch", $"Wildflower patch {side} {index}", distance + 2.1f, side * (3.95f + index % 3 * .42f), .78f + index % 3 * .1f);

        }

        private static string TreeModelPath(int seed, bool near)
        {
            if (near)
                return seed % 3 == 0 ? "Art/Models/HiFi/LakesideBroadleafScanNear3D" :
                    "Art/Models/HiFi/LakesidePineScanNear3D";
            switch (seed % 4)
            {
                case 0: return "Art/Models/HiFi/LakesideBroadleafMid3D";
                case 1: return "Art/Models/HiFi/LakesideFirTall3D";
                case 2: return "Art/Models/HiFi/LakesideFirSlender3D";
                default: return "Art/Models/HiFi/LakesidePineScanNear3D";
            }
        }

        private void PlaceRouteModule(string resourcePath, string objectName, float distance, float lateral, float scale, bool stream = false)
        {
            var module = InstantiateArt(resourcePath, objectName);
            if (module == null) return;
            var route = RouteAt(distance);
            module.position = route.center + route.side * lateral - route.up * .08f;
            module.rotation = Quaternion.LookRotation(route.tangent, route.up) * module.localRotation;
            module.localScale *= scale;
            AddMobileDistanceCulling(module);
            scenery.Add(module);
            if (stream) RegisterStreamedScenery(module, distance, lateral);
        }

        private static void AddMobileDistanceCulling(Transform instance)
        {
            var renderers = instance.GetComponentsInChildren<Renderer>(true).Where(renderer => renderer.enabled).ToArray();
            if (renderers.Length == 0 || instance.GetComponent<LODGroup>() != null) return;
            var group = instance.gameObject.AddComponent<LODGroup>();
            group.fadeMode = LODFadeMode.CrossFade;
            group.animateCrossFading = true;
            // These source assets do not yet have authored low-poly variants. Retain
            // detail near the rider and cull only the small distant screen coverage.
            group.SetLODs(new[] { new LOD(.006f, renderers), new LOD(.0015f, System.Array.Empty<Renderer>()) });
            group.RecalculateBounds();
        }

        private void CreateRider()
        {
            rider = InstantiateArt("Art/Cyclists/" + SelectedCharacter, "Wind Rider and Bicycle");
            if (rider == null) throw new MissingReferenceException("Selected cyclist prefab is missing.");
            rider.localPosition = Vector3.zero;
            var front = CyclingRiderAnimator.Find(rider, "FrontWheelPivot_ROTATE_X");
            var rear = CyclingRiderAnimator.Find(rider, "RearWheelPivot_ROTATE_X");
            var forward = front.position - rear.position; forward.y = 0;
            rider.rotation = Quaternion.FromToRotation(forward, Vector3.forward) * rider.rotation;
            riderRouteRotationOffset = rider.rotation;
            var startFrame = RouteAt(0f);
            rider.position = startFrame.center + startFrame.up * .095f;
            cyclistAnimator = rider.GetComponent<CyclingRiderAnimator>();
            cyclistAnimator.Initialize();

            for (var index = windOrbs.Count; index < 14; index++)
            {
                var orb = InstantiateArt("Art/Models/HiFi/WindEnergyHiFi", $"Wind energy {index:00}");
                if (orb == null) continue;
                foreach (var renderer in orb.GetComponentsInChildren<Renderer>(true))
                    renderer.sharedMaterial = energyGoldMaterial;
                var orbDistance = 36f + index * 65f;
                var orbRoute = RouteAt(orbDistance);
                orb.position = orbRoute.center + orbRoute.up * 1.15f;
                orb.localScale *= 1.05f;
                windOrbs.Add(orb);
                windOrbDistances.Add(orbDistance);
            }
        }

        public void SetCharacter(RiderCharacter character)
        {
            PlayerPrefs.SetInt(CharacterPreferenceKey, character == RiderCharacter.Female ? 1 : 0);
            PlayerPrefs.Save();
            if (rider != null) { rider.gameObject.SetActive(false); Destroy(rider.gameObject); }
            cyclistAnimator = null;
            CreateRider();
        }

        private Transform InstantiateArt(string resourcePath, string objectName)
        {
            if (resourcePath.EndsWith("/LakesideFlowerPatch"))
                return CreateFlowerBillboard(objectName);
            var prefab = Resources.Load<GameObject>(resourcePath);
            if (prefab == null) return null;
            var instance = Instantiate(prefab, transform, false);
            instance.name = objectName;
            if (resourcePath.Contains("/HiFi/"))
            {
                ApplyHiFiMaterials(instance.transform);
                OptimizeStaticModel(instance.transform);
            }
            return instance.transform;
        }

        private Transform CreateFlowerBillboard(string objectName)
        {
            if (flowerBillboardMaterial == null)
            {
                var texture = Resources.Load<Texture2D>("Art/Textures/Environment/AlpineWildflowers");
                var shader = Shader.Find("WindTrace/Foliage Billboard");
                if (texture == null || shader == null) return null;
                flowerBillboardMaterial = new Material(shader);
                flowerBillboardMaterial.SetTexture("_MainTex", texture);
            }
            return CreateCrossBillboard(objectName, flowerBillboardMaterial, 2.8f, 1.8f);
        }

        private Transform CreateCrossBillboard(string objectName, Material material, float width, float height)
        {
            var half = width * .5f;
            var vertices = new[]
            {
                new Vector3(-half, 0, 0), new Vector3(half, 0, 0),
                new Vector3(half, height, 0), new Vector3(-half, height, 0),
                new Vector3(0, 0, -half), new Vector3(0, 0, half),
                new Vector3(0, height, half), new Vector3(0, height, -half)
            };
            var uv = new[]
            {
                new Vector2(0, 0), new Vector2(1, 0), new Vector2(1, 1), new Vector2(0, 1),
                new Vector2(0, 0), new Vector2(1, 0), new Vector2(1, 1), new Vector2(0, 1)
            };
            var mesh = new Mesh { name = objectName + " foliage" };
            mesh.vertices = vertices;
            mesh.uv = uv;
            mesh.triangles = new[] { 0, 2, 1, 0, 3, 2, 4, 6, 5, 4, 7, 6 };
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            var instance = new GameObject(objectName);
            instance.transform.SetParent(transform, false);
            instance.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = instance.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.TwoSided;
            return instance.transform;
        }

        private void CreateCottageBillboard(Transform village, float side)
        {
            if (cottageBillboardMaterial == null)
            {
                var texture = Resources.Load<Texture2D>("Art/Textures/Environment/LakesideCottage");
                var shader = Shader.Find("WindTrace/Foliage Billboard");
                if (texture == null || shader == null) return;
                cottageBillboardMaterial = new Material(shader);
                cottageBillboardMaterial.SetTexture("_MainTex", texture);
            }
            var width = 5.2f;
            var height = 5.2f;
            var mesh = new Mesh { name = "Cottage facade cutout" };
            mesh.vertices = new[]
            {
                new Vector3(-width * .5f, 0, 0), new Vector3(width * .5f, 0, 0),
                new Vector3(width * .5f, height, 0), new Vector3(-width * .5f, height, 0)
            };
            mesh.uv = new[] { new Vector2(0, 0), new Vector2(1, 0), new Vector2(1, 1), new Vector2(0, 1) };
            mesh.triangles = new[] { 0, 2, 1, 0, 3, 2 };
            mesh.RecalculateNormals();
            var prop = new GameObject("Detailed lakeside cottage").transform;
            prop.SetParent(village, false);
            prop.localPosition = new Vector3(side * 7.1f, 0, 0);
            prop.gameObject.AddComponent<MeshFilter>().sharedMesh = mesh;
            prop.gameObject.AddComponent<MeshRenderer>().sharedMaterial = cottageBillboardMaterial;
            cameraFacingProps.Add(prop);
        }

        private void UpdateFacingProps()
        {
            if (rideCamera == null) return;
            foreach (var prop in cameraFacingProps)
            {
                if (prop == null) continue;
                var towardCamera = rideCamera.transform.position - prop.position;
                towardCamera.y = 0;
                if (towardCamera.sqrMagnitude > .01f)
                    prop.rotation = Quaternion.LookRotation(-towardCamera.normalized, Vector3.up);
            }
        }

        private static void OptimizeStaticModel(Transform instance)
        {
            var filters = instance.GetComponentsInChildren<MeshFilter>(true)
                .Where(filter => filter.sharedMesh != null && filter.GetComponent<MeshRenderer>() != null)
                .ToArray();
            if (filters.Length < 2) return;
            // Keep the imported renderers visible if a platform has discarded CPU
            // mesh data. This is a correctness fallback for existing installed assets.
            if (filters.Any(filter => !filter.sharedMesh.isReadable)) return;

            var grouped = new Dictionary<Material, List<CombineInstance>>();
            foreach (var filter in filters)
            {
                var renderer = filter.GetComponent<MeshRenderer>();
                if (renderer.sharedMaterials.Length == 0) continue;
                for (var subMesh = 0; subMesh < filter.sharedMesh.subMeshCount; subMesh++)
                {
                    var material = renderer.sharedMaterials[Mathf.Min(subMesh, renderer.sharedMaterials.Length - 1)];
                    if (material == null) continue;
                    if (!grouped.TryGetValue(material, out var entries)) grouped.Add(material, entries = new List<CombineInstance>());
                    entries.Add(new CombineInstance
                    {
                        mesh = filter.sharedMesh,
                        subMeshIndex = subMesh,
                        transform = instance.worldToLocalMatrix * filter.transform.localToWorldMatrix
                    });
                }
                renderer.enabled = false;
            }
            if (grouped.Count == 0) return;

            var materials = grouped.Keys.ToArray();
            var materialMeshes = materials.Select(material =>
            {
                var merged = new Mesh { name = instance.name + " " + material.name + " mesh" };
                merged.CombineMeshes(grouped[material].ToArray(), true, true, false);
                return merged;
            }).ToArray();
            var finalMesh = new Mesh { name = instance.name + " optimized mesh" };
            finalMesh.CombineMeshes(materialMeshes.Select(mesh => new CombineInstance { mesh = mesh, transform = Matrix4x4.identity }).ToArray(), false, false, false);
            var optimized = new GameObject("Optimized Render");
            optimized.transform.SetParent(instance, false);
            optimized.AddComponent<MeshFilter>().sharedMesh = finalMesh;
            optimized.AddComponent<MeshRenderer>().sharedMaterials = materials;
        }

        private void ApplyHiFiMaterials(Transform instance)
        {
            foreach (var renderer in instance.GetComponentsInChildren<Renderer>(true))
            {
                var materials = renderer.sharedMaterials;
                for (var index = 0; index < materials.Length; index++)
                {
                    var source = materials[index];
                    if (source == null) continue;
                    var name = source.name.ToLowerInvariant();
                    string texturePath = null;
                    var fallback = Color.white;
                    var cutout = false;

                    if (name.Contains("model.001")) texturePath = "Art/Textures/HiFi/RiderBikeAlbedo";
                    else if (name.Contains("model.002")) texturePath = "Art/Textures/HiFi/CyclistAlbedo";
                    else if (name.Contains("fir_tree_01_twig"))
                    {
                        texturePath = "Art/Textures/HiFi/PineTwigAlbedo";
                        fallback = new Color(.32f, .58f, .2f);
                        cutout = true;
                    }
                    else if (name.Contains("fir_tree_01_trunk"))
                    {
                        texturePath = "Art/Textures/HiFi/PineTrunkAlbedo";
                        fallback = new Color(.3f, .18f, .08f);
                    }
                    else if (name.Contains("fir_tree_01_bark") || name.Contains("dead_branches"))
                    {
                        texturePath = "Art/Textures/HiFi/PineBarkAlbedo";
                        fallback = new Color(.28f, .17f, .08f);
                    }
                    else if (name.Contains("jacaranda_tree_leaves"))
                    {
                        texturePath = "Art/Textures/HiFi/BroadleafLeavesAlbedo";
                        fallback = new Color(.3f, .62f, .19f);
                        cutout = true;
                    }
                    else if (name.Contains("jacaranda_tree_trunk"))
                    {
                        texturePath = "Art/Textures/HiFi/BroadleafTrunkAlbedo";
                        fallback = new Color(.32f, .19f, .09f);
                    }
                    else if (name.Contains("jacaranda_tree_branches"))
                    {
                        texturePath = "Art/Textures/HiFi/BroadleafBranchesAlbedo";
                        fallback = new Color(.31f, .2f, .1f);
                    }
                    else if (name.Contains("dark_wooden_planks"))
                    {
                        texturePath = "__color_only__";
                        fallback = name.Contains("roof")
                            ? new Color(.18f, .055f, .025f)
                            : new Color(.44f, .2f, .07f);
                    }

                    if (texturePath == null) continue;
                    var cacheKey = name + "|" + texturePath;
                    if (!hiFiMaterials.TryGetValue(cacheKey, out var material))
                    {
                        material = new Material(source) { color = fallback };
                        var texture = Resources.Load<Texture2D>(texturePath);
                        if (texture != null)
                        {
                            material.mainTexture = texture;
                            material.color = Color.white;
                        }
                        if (cutout && material.shader != null && material.shader.name == "Standard")
                        {
                            var foliageShader = Shader.Find("WindTrace/Foliage Double Sided");
                            if (foliageShader != null) material.shader = foliageShader;
                            material.SetFloat("_Cutoff", .42f);
                            material.EnableKeyword("_ALPHATEST_ON");
                            material.renderQueue = 2450;
                        }
                        hiFiMaterials.Add(cacheKey, material);
                    }
                    materials[index] = material;
                }
                renderer.sharedMaterials = materials;
            }
        }

        private static void FindNamedChildren(Transform root, string objectName, List<Transform> results)
        {
            foreach (var child in root.GetComponentsInChildren<Transform>(true))
            {
                if (child.name == objectName) results.Add(child);
            }
        }

        private static Transform FindNamedChild(Transform root, string objectName)
        {
            foreach (var child in root.GetComponentsInChildren<Transform>(true))
            {
                if (child.name == objectName) return child;
            }
            return null;
        }

        private void CreateGuardian()
        {
            guardian = NewLandmarkRoot("Tempest Guardian wind vortex");
            guardian.position = new Vector3(9f, 2.5f, 21f);
            guardian.localScale = Vector3.one * 1.7f;
            CreateWindRing(guardian, "Outer wind orbit", 1.05f, .09f, new Vector3(18f, 0f, -22f));
            CreateWindRing(guardian, "Crossing wind orbit", .82f, .075f, new Vector3(-28f, 37f, 31f));
            CreateWindRing(guardian, "Inner wind orbit", .57f, .055f, new Vector3(47f, -24f, 11f));
            LandmarkPart(guardian, PrimitiveType.Sphere, "Wind vortex core", guardianMaterial,
                Vector3.zero, Vector3.one * .3f);
            guardian.gameObject.SetActive(false);
        }

        private void CreateWindRing(Transform parent, string name, float radius, float halfWidth, Vector3 rotation)
        {
            const int segments = 48;
            var vertices = new Vector3[segments * 2];
            var triangles = new int[segments * 12];
            for (var i = 0; i < segments; i++)
            {
                var angle = i * Mathf.PI * 2f / segments;
                var direction = new Vector3(Mathf.Cos(angle), Mathf.Sin(angle), 0f);
                vertices[i * 2] = direction * (radius - halfWidth);
                vertices[i * 2 + 1] = direction * (radius + halfWidth);
                var next = (i + 1) % segments;
                var offset = i * 12;
                triangles[offset] = i * 2;
                triangles[offset + 1] = next * 2;
                triangles[offset + 2] = i * 2 + 1;
                triangles[offset + 3] = i * 2 + 1;
                triangles[offset + 4] = next * 2;
                triangles[offset + 5] = next * 2 + 1;
                for (var reverse = 0; reverse < 6; reverse++)
                    triangles[offset + 6 + reverse] = triangles[offset + 5 - reverse];
            }
            var mesh = new Mesh { name = name + " mesh" };
            mesh.vertices = vertices;
            mesh.triangles = triangles;
            mesh.RecalculateNormals();
            var ring = new GameObject(name).transform;
            ring.SetParent(parent, false);
            ring.localRotation = Quaternion.Euler(rotation);
            ring.gameObject.AddComponent<MeshFilter>().sharedMesh = mesh;
            ring.gameObject.AddComponent<MeshRenderer>().sharedMaterial = guardianMaterial;
        }

        private Transform CreatePrimitive(PrimitiveType type, string objectName, Material material)
        {
            var created = GameObject.CreatePrimitive(type);
            created.name = objectName;
            created.transform.SetParent(transform, false);
            var collider = created.GetComponent<Collider>();
            if (collider != null)
            {
                if (Application.isPlaying) Destroy(collider);
                else DestroyImmediate(collider);
            }
            created.GetComponent<Renderer>().sharedMaterial = material;
            return created.transform;
        }

        private static Color PhaseSky(RidePhaseKind phase)
        {
            switch (phase)
            {
                case RidePhaseKind.Warmup: return new Color(.24f, .58f, .82f);
                case RidePhaseKind.Explore: return new Color(.18f, .52f, .68f);
                case RidePhaseKind.CadenceChallenge: return new Color(.09f, .42f, .7f);
                case RidePhaseKind.Recovery: return new Color(.18f, .51f, .71f);
                case RidePhaseKind.Sprint: return new Color(.13f, .39f, .69f);
                case RidePhaseKind.Boss: return new Color(.43f, .52f, .79f);
                case RidePhaseKind.Cooldown: return new Color(.67f, .56f, .76f);
                default: return new Color(.12f, .3f, .42f);
            }
        }

        private void OnDestroy()
        {
            Destroy(roadMaterial);
            Destroy(rutMaterial);
            Destroy(edgeMaterial);
            Destroy(riderMaterial);
            Destroy(guardianMaterial);
            Destroy(energyGoldMaterial);
            Destroy(grassMaterial);
            Destroy(lakeMaterial);
            Destroy(cloudMaterial);
            Destroy(grassBladeMaterial);
            Destroy(canyonRockMaterial);
            Destroy(canyonAccentMaterial);
            Destroy(cottageWallMaterial);
            Destroy(cottageRoofMaterial);
            Destroy(routeGlowMaterial);
            Destroy(boostTrailMaterial);
            Destroy(cottageBillboardMaterial);
            Destroy(flowerBillboardMaterial);
            Destroy(skyMaterial);
            foreach (var material in hiFiMaterials.Values) Destroy(material);
            hiFiMaterials.Clear();
        }
    }
}
