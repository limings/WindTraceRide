using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Rendering;

namespace WindTraceRide.Bootstrap
{
    /// <summary>Bounded, route-sampled environments. Geometry is built per 80 m
    /// chunk; authored modules are instanced and the pool follows the rider.</summary>
    public sealed class ReferenceRideEnvironment : MonoBehaviour
    {
        public struct Frame
        {
            public Vector3 Center, Right, Forward;
        }

        public const float ChunkLength = 80f;
        public const int PoolSize = 9;
        private int level;
        private float courseLength;
        private Func<float, Frame> route;
        private readonly Dictionary<int, GameObject> chunks = new Dictionary<int, GameObject>();
        private readonly Dictionary<string, Material> materials = new Dictionary<string, Material>();
        private readonly Dictionary<string, GameObject> prototypes = new Dictionary<string, GameObject>();
        private readonly Dictionary<string, Quaternion> facadeCorrections=new Dictionary<string, Quaternion>();
        private readonly List<Mesh> generatedMeshes = new List<Mesh>();
        private readonly List<Transform> rotors = new List<Transform>();
        private readonly Dictionary<Transform,Vector3> rotorAxes=new Dictionary<Transform,Vector3>();
        private Material road, ground, water, stone, sandstone, glow, timber, paving, waterfall, mountain;
        private ReferenceWaterReflection reflection;
        private Transform landscapeFloor;
        private readonly List<int> lastRequired = new List<int>();
        private int lastAnchor = int.MinValue;
        private IEnumerator streamingBuild;
        private GameObject streamingRoot;
        private int streamingKey;
        public double LastStreamSliceMilliseconds { get; private set; }
        public int ActiveChunks => chunks.Count;
        public IReadOnlyCollection<GameObject> Chunks => chunks.Values;
        public float ShoreRockFootprintRadius { get; private set; }

        public void Initialize(int index, float length, Func<float, Frame> sampler)
        {
            level = index;
            courseLength = length;
            route = sampler;
            road = Terrain("Trail", new Color(.37f,.25f,.12f), new Color(.64f,.48f,.27f), "GravelTrail", .45f);
            ground = Terrain("Ground", level == 1 ? new Color(.42f,.23f,.105f) : new Color(.10f,.23f,.045f),
                level == 1 ? new Color(.68f,.39f,.20f) : new Color(.30f,.43f,.10f),
                level == 1 ? "CanyonSoil" : "AlpineMeadow", .33f);
            stone = TileMaterial("Fieldstone");
            sandstone = TileMaterial("Sandstone");
            timber = TileMaterial("Larch");
            paving = new Material(Shader.Find("WindTrace/Reference Paving"));
            paving.SetColor("_Color", level == 1 ? new Color(.28f,.31f,.31f) : new Color(.48f,.42f,.30f));
            paving.SetColor("_SecondaryColor", level == 1 ? new Color(.45f,.48f,.46f) : new Color(.69f,.61f,.43f));
            paving.SetFloat("_BlockScale", level == 1 ? .8f : 3.8f);
            materials["Paving"] = paving;
            water = new Material(Shader.Find("WindTrace/Reference Water"));
            water.color = new Color(.018f,.14f,.20f);
            water.SetColor("_SecondaryColor", new Color(.065f,.36f,.40f));
            water.SetColor("_ReflectionColor", new Color(.29f,.55f,.68f));
            water.SetColor("_FoamColor", new Color(.69f,.83f,.81f));
            water.SetFloat("_WaveStrength", level == 0 ? .055f : .028f);
            water.SetFloat("_WaveScale", level == 0 ? .7f : 1.45f);
            water.SetFloat("_FlowSpeed", level == 0 ? .22f : .72f);
            water.SetFloat("_Glossiness", .48f);
            materials["Water"] = water;
            reflection=gameObject.AddComponent<ReferenceWaterReflection>();reflection.Initialize(water);
            waterfall = new Material(Shader.Find("WindTrace/Reference Waterfall"));
            materials["Waterfall"] = waterfall;
            mountain=new Material(Shader.Find("WindTrace/Reference Mountain"));
            mountain.color=level==1?new Color(.40f,.22f,.12f):new Color(.25f,.29f,.24f);
            mountain.SetFloat("_Snowline",level==1?10000f:67f);
            mountain.SetTexture("_MainTex",Resources.Load<Texture2D>("Art/Textures/ReferenceWorld/Fieldstone"));
            materials["Mountain"]=mountain;
            glow = new Material(Shader.Find("Standard"));
            glow.color = new Color(.035f,.41f,.57f);
            glow.EnableKeyword("_EMISSION");
            glow.SetColor("_EmissionColor", new Color(.02f,.68f,.91f)*1.3f);
            materials["Glow"] = glow;
            foreach (var name in new[] { "LakesideChalet", "VillageCottage", "VillageMill", "CanyonTurbine",
                "ShoreRock", "CanyonButte", "CanyonArch", "ForestPine", "VillageOak", "TimberFence",
                "ForestPine_LOD1", "VillageOak_LOD1" }) PreparePrototype(name);
            var floor=GameObject.CreatePrimitive(PrimitiveType.Plane);floor.name="World-space landscape foundation";
            floor.transform.SetParent(transform,false);floor.transform.localScale=new Vector3(120,1,140);
            floor.GetComponent<MeshRenderer>().sharedMaterial=ground;
            floor.GetComponent<MeshRenderer>().shadowCastingMode=ShadowCastingMode.Off;
            Release(floor.GetComponent<Collider>());landscapeFloor=floor.transform;
            UpdateDistance(0f);
        }

        public void UpdateDistance(float distance)
        {
            var anchor = Mathf.FloorToInt(distance / ChunkLength);
            if (anchor == lastAnchor) return;
            lastAnchor = anchor;
            var center=route(distance).Center;
            landscapeFloor.position=new Vector3(center.x,level==1?-20f:-2.5f,center.z);
            lastRequired.Clear();
            for (var i = -1; i < PoolSize - 1; i++)
            {
                var key = anchor + i;
                if (key >= 0 && key * ChunkLength <= courseLength) lastRequired.Add(key);
            }
            foreach (var key in chunks.Keys.ToArray())
            {
                if (lastRequired.Contains(key)) continue;
                var chunk = chunks[key];
                if(streamingRoot==chunk){(streamingBuild as IDisposable)?.Dispose();streamingBuild=null;streamingRoot=null;}
                foreach (var filter in chunk.GetComponentsInChildren<MeshFilter>(true))
                    if (generatedMeshes.Remove(filter.sharedMesh)) Release(filter.sharedMesh);
                foreach (var t in chunk.GetComponentsInChildren<Transform>(true)){rotors.Remove(t);rotorAxes.Remove(t);}
                chunk.SetActive(false);
                Release(chunk);
                chunks.Remove(key);
            }
            foreach (var key in lastRequired)
                if (!chunks.ContainsKey(key) && (!Application.isPlaying || anchor==0)) chunks[key] = BuildChunk(key);
        }

        private void Update()
        {
            ProcessStreaming();
            // Derive the plane normal from imported geometry, independent of the
            // Blender/FBX axis convention; never rotate sails edge-over-edge.
            foreach (var rotor in rotors)
                if (rotor != null && rotor.gameObject.activeInHierarchy)
                    rotor.Rotate(rotorAxes[rotor],35f * Time.deltaTime,Space.Self);
        }

        private GameObject BuildChunk(int key)
        {
            var root = new GameObject($"Reference level {level + 1} / segment {key:000}");
            root.transform.SetParent(transform, false);
            var build=BuildChunkSteps(root,key);
            while(build.MoveNext()){}
            (build as IDisposable)?.Dispose();
            return root;
        }

        private void ProcessStreaming()
        {
            if(!Application.isPlaying)return;
            var start=System.Diagnostics.Stopwatch.GetTimestamp();
            var frequency=(double)System.Diagnostics.Stopwatch.Frequency;
            do
            {
                if(streamingBuild==null)
                {
                    var found=false;
                    foreach(var key in lastRequired)
                        if(!chunks.ContainsKey(key)){streamingKey=key;found=true;break;}
                    if(!found)break;
                    streamingRoot=new GameObject($"Reference level {level+1} / segment {streamingKey:000}");
                    streamingRoot.transform.SetParent(transform,false);streamingRoot.SetActive(false);
                    chunks[streamingKey]=streamingRoot;
                    streamingBuild=BuildChunkSteps(streamingRoot,streamingKey);
                }
                if(!streamingBuild.MoveNext())
                {
                    (streamingBuild as IDisposable)?.Dispose();streamingBuild=null;
                    streamingRoot.SetActive(true);streamingRoot=null;
                }
            }
            while((System.Diagnostics.Stopwatch.GetTimestamp()-start)*1000/frequency<2f);
            LastStreamSliceMilliseconds=(System.Diagnostics.Stopwatch.GetTimestamp()-start)*1000/frequency;
        }

        private IEnumerator BuildChunkSteps(GameObject root,int key)
        {
            var start = key * ChunkLength;
            var end = Mathf.Min(courseLength + 20f, start + ChunkLength);
            GroundMesh(root.transform, start, end);yield return null;
            DistantRidges(root.transform,start,end);yield return null;
            Ribbon(root.transform, "Cycleway", start, end, d => -2.25f, d => 2.25f,
                (d,l) => .045f, level == 0 ? road : paving, 3);
            yield return null;
            if (level == 0)
            {
                Ribbon(root.transform, "Lake", start, end, d => -380f, d => Shore(d), (d,l) => .05f-route(d).Center.y, water, 24);
                yield return null;
                Ribbon(root.transform, "Wet shoreline", start, end, d => Shore(d)-.25f, d => Shore(d)+.7f,
                    (d,l) => -.63f + (l-Shore(d))*.38f, stone, 2);
            }
            else if (level == 1)
            {
                Ribbon(root.transform, "River beneath bridge", start, end, d => -54f, d => 52f,
                    (d,l) => -14.6f, water, 20);
                yield return null;
                Bridge(root.transform, start, end, true);
            }
            else
            {
                Ribbon(root.transform, "Winding brook", start, end, d => Brook(d)-2.7f, d => Brook(d)+2.7f,
                    (d,l) => -.85f, water, 5);
                yield return null;
                Ribbon(root.transform, "Brook left bank", start, end, d => Brook(d)-3.1f, d => Brook(d)-2.6f,
                    (d,l) => -.66f, stone, 2);
                Ribbon(root.transform, "Brook right bank", start, end, d => Brook(d)+2.6f, d => Brook(d)+3.1f,
                    (d,l) => -.66f, stone, 2);
                // Brook traverses the trail twice in each village district.
                for (var d = start + 2f; d < end; d += 4f)
                    if (Mathf.Abs(Brook(d)) < 5.3f) Bridge(root.transform, d, Mathf.Min(end, d+4f), false);
            }
            yield return null;
            var dressing=DressSteps(root.transform,start,end,key);
            while(dressing.MoveNext())yield return null;
            var merging=MergeStaticGeometrySteps(root);
            while(merging.MoveNext())yield return null;
        }

        public static float Brook(float distance)
        {
            var district = Mathf.Repeat(distance, 480f);
            var a = (district - 164f) / 33f;
            var b = (district - 390f) / 29f;
            return -9.5f + 19f * Mathf.Exp(-a*a) + 18f * Mathf.Exp(-b*b);
        }

        private static float Shore(float d) => -7.7f - Mathf.Sin(d*.025f)*1.3f - Mathf.Sin(d*.071f)*.4f;

        public float SurfaceHeight(float d, float lateral)
        {
            var value = RawSurfaceHeight(d,lateral);
            if(level==1)return value;
            var key=Mathf.FloorToInt(d/ChunkLength);
            for(var neighbor=key-1;neighbor<=key+1;neighbor++)
            {
                if(neighbor<0)continue;
                var start=neighbor*ChunkLength;
                if(level==0 && neighbor%3==0) value=Platform(d,lateral,start+47f,13f,5.0f,value);
                if(level==2)
                {
                    value=Platform(d,lateral,start+33f,11f,4.3f,value);
                    if(neighbor%2==0)
                    {
                        value=Platform(d,lateral,start+61f,-18f,3.8f,value);
                        value=Platform(d,lateral,start+59f,9f,3.7f,value);
                    }
                }
            }
            return value;
        }

        private float Platform(float d,float l,float center,float side,float size,float height)
        {
            var radius=Mathf.Sqrt((d-center)*(d-center)+(l-side)*(l-side));
            return Mathf.Lerp(RawSurfaceHeight(center,side),height,Mathf.SmoothStep(0,1,Mathf.InverseLerp(size,size+2.2f,radius)));
        }

        private float RawSurfaceHeight(float d,float lateral)
        {
            if (level == 1)
                return -17.4f + Mathf.SmoothStep(0f,1f, Mathf.InverseLerp(20f, 53f,Mathf.Abs(lateral)))*19f +
                    Mathf.Sin(d*.014f+lateral*.025f)*1.4f;
            if (level == 0 && lateral < Shore(d)) return -2.3f;
            if (level == 2 && Mathf.Abs(lateral-Brook(d)) < 2.9f) return -1.65f;
            if (Mathf.Abs(lateral) < 3f) return -.08f;
            var hill = Mathf.Max(0f, Mathf.Abs(lateral)-5f)*.075f;
            hill += Mathf.Sin(d*.022f+lateral*.07f)*Mathf.Min(1.25f, Mathf.Abs(lateral)*.05f);
            if (level == 0 && lateral < -3f) hill = -.18f + (lateral-Shore(d))*.06f;
            if (level == 0 && lateral > 3f) hill=Mathf.Min(hill,.95f);
            if (level == 2 && Mathf.Abs(lateral-Brook(d)) < 4.2f)
                hill = Mathf.Lerp(-.58f,hill,Mathf.InverseLerp(2.9f,4.2f,Mathf.Abs(lateral-Brook(d))));
            return hill;
        }

        private void GroundMesh(Transform parent, float start, float end)
        {
            var width=level==0?45f:100f;
            Ribbon(parent,"Left terrain",start,end,d => -width,d => -2.25f,SurfaceHeight,ground,38);
            Ribbon(parent,"Right terrain",start,end,d => 2.25f,d => width,SurfaceHeight,ground,38);
            if (level != 1)
            {
                Ribbon(parent,"Trail shoulder L",start,end,d => -2.9f,d => -2.20f,(d,l) => -.025f,ground,2);
                Ribbon(parent,"Trail shoulder R",start,end,d => 2.20f,d => 2.9f,(d,l) => -.025f,ground,2);
            }
        }

        private void DistantRidges(Transform parent,float start,float end)
        {
            if(Mathf.RoundToInt(start/ChunkLength)%2!=0)return;
            foreach(var side in new[]{-1f,1f})
            {
                // World-space height fields, not ribbons offset around tight road
                // bends: those can fold over and create floating mountain roofs.
                const int cells=32;
                var v=new Vector3[(cells+1)*(cells+1)];var uv=new Vector2[v.Length];
                var triangles=new int[cells*cells*6];
                var center=Point(start+40,side*(235+Rand(start,61)*25),0);
                center.y=level==1?-20f:-2.5f;
                var height=75+Rand(start,62)*38;
                for(var row=0;row<=cells;row++)
                    for(var col=0;col<=cells;col++)
                    {
                        var x=(col/(float)cells-.5f)*220;
                        var z=(row/(float)cells-.5f)*280;
                        var radius=Mathf.Sqrt(x*x+z*z*.62f)/110;
                        var taper=Mathf.Pow(Mathf.Max(0,1-radius),.72f);
                        var noise=Mathf.Sin((center.x+x)*.054f)*Mathf.Cos((center.z+z)*.033f)*12;
                        noise+=Mathf.Sin(x*.042f+z*.018f)*16+Mathf.Sin(x*.12f-z*.075f)*6;
                        var index=row*(cells+1)+col;
                        v[index]=center+new Vector3(x,taper*(height+noise),z);
                        uv[index]=new Vector2(x,z)*.01f;
                        if(row==cells||col==cells)continue;
                        var t=(row*cells+col)*6;
                        triangles[t]=index;triangles[t+1]=index+cells+2;triangles[t+2]=index+1;
                        triangles[t+3]=index;triangles[t+4]=index+cells+1;triangles[t+5]=index+cells+2;
                    }
                var mesh=new Mesh{name="World mountain heightfield"};
                mesh.vertices=v;mesh.uv=uv;mesh.triangles=triangles;mesh.RecalculateNormals();
                generatedMeshes.Add(mesh);
                var obj=new GameObject(mesh.name);obj.transform.SetParent(parent,false);
                obj.AddComponent<MeshFilter>().sharedMesh=mesh;
                var renderer=obj.AddComponent<MeshRenderer>();renderer.sharedMaterial=mountain;
                renderer.shadowCastingMode=ShadowCastingMode.Off;
            }
        }

        private Vector3 Point(float d, float l, float height)
        {
            var frame = route(d);
            return frame.Center + frame.Right*l + Vector3.up*height;
        }

        private void Ribbon(Transform parent, string name, float start, float end,
            Func<float,float> left, Func<float,float> right, Func<float,float,float> height,
            Material material, int across)
        {
            var along = Mathf.Max(1, Mathf.CeilToInt((end-start)/3f));
            var vertices=new Vector3[(along+1)*(across+1)];
            var uv=new Vector2[vertices.Length];
            var triangles=new int[along*across*6];
            for(var i=0;i<=along;i++)
            {
                var d=Mathf.Lerp(start,end,i/(float)along);
                for(var j=0;j<=across;j++)
                {
                    var l=Mathf.Lerp(left(d),right(d),j/(float)across);
                    var v=i*(across+1)+j;
                    vertices[v]=Point(d,l,height(d,l));
                    uv[v]=new Vector2(j/(float)across,d*.1f);
                    if(i==along || j==across)continue;
                    var t=(i*across+j)*6;
                    triangles[t]=v;triangles[t+1]=v+across+2;triangles[t+2]=v+1;
                    triangles[t+3]=v;triangles[t+4]=v+across+1;triangles[t+5]=v+across+2;
                }
            }
            var mesh=new Mesh {name=name};
            mesh.vertices=vertices;mesh.uv=uv;mesh.triangles=triangles;
            mesh.RecalculateNormals();mesh.RecalculateTangents();mesh.RecalculateBounds();generatedMeshes.Add(mesh);
            var obj=new GameObject(name);obj.transform.SetParent(parent,false);
            obj.AddComponent<MeshFilter>().sharedMesh=mesh;
            var renderer=obj.AddComponent<MeshRenderer>();renderer.sharedMaterial=material;
            if(material==water)
            {
                obj.layer=4;
                obj.AddComponent<ReferenceWaterTile>().Reflection=reflection;
            }
            if(material==water || material==ground) renderer.shadowCastingMode=ShadowCastingMode.Off;
        }

        private void Bridge(Transform parent,float start,float end,bool elevated)
        {
            var rail = elevated ? sandstone : timber;
            Ribbon(parent,"Bridge deck fascia",start,end,d => -2.58f,d => 2.58f,
                (d,l) => -.22f,stone,1);
            foreach (var sign in new[] {-1f,1f})
            {
                for(var d=start;d<end;d+=4f)
                {
                    var finish=Mathf.Min(end,d+4f);
                    var a=Point(d,sign*2.48f,.88f);
                    var b=Point(finish,sign*2.48f,.88f);
                    Beam(parent,"Bridge parapet",a,b,elevated?.14f:.065f,rail,elevated?.18f:.06f);
                    Beam(parent,"Bridge post",Point(d,sign*2.48f,-.15f),Point(d,sign*2.48f,1.10f),
                        elevated?.15f:.09f,rail);
                    if(elevated)
                    {
                        Beam(parent,"Deck cyan guide",Point(d,sign*2.13f,.07f),Point(finish,sign*2.13f,.07f),.025f,glow);
                        Beam(parent,"Lower parapet",Point(d,sign*2.48f,.35f),Point(finish,sign*2.48f,.35f),.075f,rail);
                    }
                }
            }
            if(elevated)
                for(var d=Mathf.Ceil(start/24f)*24f;d<end;d+=24f)
                {
                    Beam(parent,"Bridge pier",Point(d,0,-16.4f),Point(d,0,-.24f),.72f,stone,1.0f);
                    Beam(parent,"Pier crosshead",Point(d,-2.45f,-1.20f),Point(d,2.45f,-1.20f),.44f,stone);
                }
        }

        private void Beam(Transform parent,string name,Vector3 a,Vector3 b,float halfWidth,Material material,float depth=0)
        {
            var obj=GameObject.CreatePrimitive(PrimitiveType.Cube);
            obj.name=name;obj.transform.SetParent(parent,false);
            obj.transform.position=(a+b)*.5f;
            obj.transform.rotation=Quaternion.LookRotation((b-a).normalized,Vector3.up);
            obj.transform.localScale=new Vector3(halfWidth*2f,(depth>0?depth:halfWidth)*2f,(b-a).magnitude);
            obj.GetComponent<MeshRenderer>().sharedMaterial=material;
            Release(obj.GetComponent<Collider>());
        }

        private float Rand(float d,int salt) => Mathf.Repeat(Mathf.Sin(d*12.9898f+salt*78.233f)*43758.5453f,1f);

        private IEnumerator DressSteps(Transform parent,float start,float end,int key)
        {
            if(level==1)
            {
                // Cliff islands around, below and beyond the bridge, with silhouettes
                // that move in parallax as the cyclist crosses the river.
                for(var i=0;i<2;i++)
                {
                    var d=start+19+i*39;
                    var side=i%2==0?-1f:1f;
                    var l=side*(34f+Rand(d,2)*14f);
                    var size=2.6f+Rand(d,3)*1.0f;
                    Place(parent,"CanyonButte",d,l,-15.3f,size,Rand(d,4)*360f);
                    // Sparse bushes belong on rock terraces, not rows of alpine flowers.
                    Place(parent,"VillageOak_LOD1",d,side*42f,SurfaceHeight(d,side*42f),.55f,Rand(d,8)*360);
                    if(key%2==i) Place(parent,"CanyonTurbine",d,l,-15.3f+13f*size,.95f,side>0?-165f:165f);
                    yield return null;
                }
                if(key%3==1)Place(parent,"CanyonArch",start+48f,-52f,-15f,1.8f,25f);
                for(var i=0;i<6;i++)
                    Place(parent,"ShoreRock",start+8+i*12, (i%2==0?-1:1)*(8+Rand(i+start,3)*8),-15f,.7f+Rand(i,4),i*39);
                if(key%4==2) Waterfall(parent,start+19f,-22f,27f);
                yield break;
            }
            // A dense inner corridor plus a cheaper forest beyond it.
            for(var i=0;i<18;i++)
            {
                var d=start+3f+i*4.2f;
                var side=i%3==0?-1f:1f;
                var l=side*(5.0f+Rand(d,1)*12f);
                if(level==0 && l<Shore(d)+1.4f)continue;
                if(level==2 && Mathf.Abs(l-Brook(d))<4.5f)continue;
                if(NearArchitecture(d,l,5f))continue;
                var tree=level==0?(i%5==0?"VillageOak":"ForestPine"):(i%4==0?"ForestPine":"VillageOak");
                PlaceTree(parent,tree,d,l,.80f+Rand(d,9)*.5f,Rand(d,7)*360);
                yield return null;
            }
            for(var i=0;i<18;i++)
            {
                var d=start+i*4.35f;
                var l=17f+Rand(d,11)*26f;
                Place(parent,level==0?"ForestPine_LOD1":"VillageOak_LOD1",d,l,SurfaceHeight(d,l),
                    .8f+Rand(d,6)*.55f,Rand(d,12)*360);
                yield return null;
            }
            for(var i=0;i<12;i++)
            {
                var d=start+i*6.65f;
                var l=level==0?Shore(d)+.3f:Brook(d)+(i%2==0?-3.0f:3.0f);
                var scale=.45f+Rand(d,4)*.5f;
                if(level==2 && !RockClearsRoad(d,l,scale))continue;
                Place(parent,"ShoreRock",d,l,SurfaceHeight(d,l)-.15f,scale,Rand(d,5)*360);
                yield return null;
            }
            for(var i=0;i<7;i++)
            {
                var d=start+5f+i*10.6f;
                if(level==0 || Mathf.Abs(Brook(d)+3.5f)>4)
                    Place(parent,"TimberFence",d,-3.65f,SurfaceHeight(d,-3.65f),1f,90f);
                yield return null;
            }
            if(level==0 && key%3==0)
                Place(parent,"LakesideChalet",start+47f,13f,SurfaceHeight(start+47,13),.90f,-135f);
            if(level==2)
            {
                var d=start+33f;
                Place(parent,"VillageCottage",d,11f,SurfaceHeight(d,11f),.9f,-135f);
                if(key%2==0)
                {
                    Place(parent,"VillageCottage",start+61f,-18f,SurfaceHeight(start+61,-18f),.76f,135f);
                    Place(parent,"VillageMill",start+59f,9f,SurfaceHeight(start+59,9),1.65f,-160f);
                }
            }
            yield return null;
            GroundDressing(parent,start,end);
            yield return null;
        }

        private void GroundDressing(Transform parent,float start,float end)
        {
            // Use existing authored 3D flower patches; randomized patches and grass
            // merge into a single draw per material after chunk creation.
            for(var i=0;i<18;i++)
            {
                var d=start+2+i*4.2f;
                var l=(i%2==0?-1f:1f)*(3.1f+Rand(d,16)*1.3f);
                if(level==2 && Mathf.Abs(l-Brook(d))<3.3f)continue;
                var prefab=Resources.Load<GameObject>("Art/Models/HiFi/LakesideFlowerPatch");
                if(prefab==null)continue;
                var o=Instantiate(prefab,parent,false);
                o.transform.position=Point(d,l,SurfaceHeight(d,l));
                o.transform.rotation=Quaternion.LookRotation(route(d).Forward,Vector3.up)*o.transform.localRotation;
                o.transform.localScale*=.45f+Rand(d,14)*.20f;
            }
            // Thin blades along both trail shoulders, with a fixed deterministic seed.
            var v=new List<Vector3>();var t=new List<int>();
            for(var i=0;i<650;i++)
            {
                var d=Mathf.Lerp(start,end,Rand(i+start,30));
                var l=(i%2==0?-1:1)*(2.8f+Rand(i+start,31)*2.1f);
                if(level==2 && Mathf.Abs(l-Brook(d))<3.3f)continue;
                var c=Point(d,l,SurfaceHeight(d,l)+.025f);
                var size=.16f+Rand(i+start,32)*.25f;
                var right=route(d).Right*.018f;
                var bend=route(d).Forward*size*.20f;
                var n=v.Count;v.Add(c-right);v.Add(c+right);
                v.Add(c+Vector3.up*size*.65f+right*.3f+bend*.5f);
                v.Add(c+Vector3.up*size+bend);
                t.Add(n);t.Add(n+2);t.Add(n+1);t.Add(n);t.Add(n+3);t.Add(n+2);
            }
            var m=new Mesh{name="Roadside blades"};m.SetVertices(v);m.SetTriangles(t,0);m.RecalculateNormals();
            generatedMeshes.Add(m);
            var blades=new GameObject("Roadside blades");blades.transform.SetParent(parent,false);
            blades.AddComponent<MeshFilter>().sharedMesh=m;
            blades.AddComponent<MeshRenderer>().sharedMaterial=TileMaterial("BroadLeaves");
        }

        private bool NearArchitecture(float d,float l,float clearance)
        {
            var key=Mathf.FloorToInt(d/ChunkLength);
            for(var k=Mathf.Max(0,key-1);k<=key+1;k++)
            {
                var start=k*ChunkLength;
                if(level==0 && k%3==0 && Mathf.Abs(d-start-47)<7 && Mathf.Abs(l-13)<clearance)return true;
                if(level==2)
                {
                    if(Mathf.Abs(d-start-33)<6 && Mathf.Abs(l-11)<clearance)return true;
                    if(k%2==0 && Mathf.Abs(d-start-61)<6 && Mathf.Abs(l+18)<clearance)return true;
                    if(k%2==0 && Mathf.Abs(d-start-59)<40 && Mathf.Abs(l-9)<10)return true;
                }
            }
            return false;
        }

        private bool RockClearsRoad(float distance,float lateral,float scale)
        {
            // The brook crosses the road. Skip bank rocks there instead of moving
            // them onto the bridge. Include the whole rotated mesh footprint and
            // nearby curved road, not just the object's lateral pivot coordinate.
            var center=Point(distance,lateral,0);center.y=0;
            var clearance=2.25f+.8f+ShoreRockFootprintRadius*scale;
            var range=clearance+4f;
            for(var offset=-range;offset<=range;offset+=.5f)
            {
                var roadCenter=route(Mathf.Max(0,distance+offset)).Center;roadCenter.y=0;
                if((center-roadCenter).sqrMagnitude<clearance*clearance)return false;
            }
            return true;
        }

        private void Waterfall(Transform parent,float distance,float lateral,float height)
        {
            // Water mesh is vertical and follows world gravity, with independently
            // scrolling fine ripples and a low-cost splash surface below.
            var a=Point(distance,lateral,-14.3f);
            var b=a+Vector3.up*height;
            var r=route(distance).Right*1.2f;
            var mesh=new Mesh{name="Waterfall sheet"};
            mesh.vertices=new[]{a-r,a+r,b-r,b+r};mesh.uv=new[]{Vector2.zero,Vector2.right,Vector2.up,Vector2.one};
            mesh.triangles=new[]{0,2,1,1,2,3};mesh.RecalculateNormals();mesh.RecalculateTangents();
            generatedMeshes.Add(mesh);
            var obj=new GameObject("Canyon waterfall");obj.transform.SetParent(parent,false);
            obj.AddComponent<MeshFilter>().sharedMesh=mesh;
            obj.AddComponent<MeshRenderer>().sharedMaterial=waterfall;
        }

        private Transform Place(Transform parent,string asset,float d,float lateral,float height,float scale,float yaw)
        {
            if(!prototypes.TryGetValue(asset,out var prefab))return null;
            var wrapper=new GameObject(asset).transform;wrapper.SetParent(parent,false);
            var f=route(d);
            wrapper.position=Point(d,lateral,height);
            wrapper.rotation=Quaternion.LookRotation(new Vector3(f.Forward.x,0,f.Forward.z),Vector3.up)*Quaternion.Euler(0,yaw,0);
            if(facadeCorrections.TryGetValue(asset,out var correction))wrapper.rotation*=correction;
            wrapper.localScale=Vector3.one*scale;
            var obj=Instantiate(prefab,wrapper,false);obj.SetActive(true);
            foreach(var t in obj.GetComponentsInChildren<Transform>(true))
                if(t.name.StartsWith("Rotor",StringComparison.Ordinal))
                {
                    rotors.Add(t);rotorAxes[t]=RotorAxis(t);
                }
            return wrapper;
        }

        private void PlaceTree(Transform parent,string name,float d,float lateral,float scale,float yaw)
        {
            var tree=Place(parent,name,d,lateral,SurfaceHeight(d,lateral),scale,yaw);
            if(tree==null)return;
            var near=tree.GetComponentsInChildren<Renderer>(true);
            if(!prototypes.TryGetValue(name+"_LOD1",out var farPrefab))return;
            var far=Instantiate(farPrefab,tree,false);far.SetActive(true);
            var group=tree.gameObject.AddComponent<LODGroup>();
            group.SetLODs(new[]{new LOD(.13f,near),new LOD(.018f,far.GetComponentsInChildren<Renderer>(true))});
            group.RecalculateBounds();
        }

        private static Vector3 RotorAxis(Transform rotor)
        {
            var bounds=new Bounds(Vector3.zero,Vector3.zero);
            foreach(var filter in rotor.GetComponentsInChildren<MeshFilter>(true))
            {
                var b=filter.sharedMesh.bounds;
                for(var i=0;i<8;i++)
                {
                    var p=b.center+Vector3.Scale(b.extents,new Vector3((i&1)==0?-1:1,(i&2)==0?-1:1,(i&4)==0?-1:1));
                    bounds.Encapsulate(rotor.InverseTransformPoint(filter.transform.TransformPoint(p)));
                }
            }
            var size=bounds.size;
            return size.x<size.y && size.x<size.z?Vector3.right:size.y<size.z?Vector3.up:Vector3.forward;
        }

        private void PreparePrototype(string name)
        {
            var source=Resources.Load<GameObject>("Art/Models/ReferenceWorld/"+name);
            if(source==null)throw new MissingReferenceException("Reference asset missing: "+name);
            var obj=Instantiate(source,transform,false);obj.name=name+" prototype";
            foreach(var renderer in obj.GetComponentsInChildren<Renderer>())
            {
                renderer.sharedMaterials=renderer.sharedMaterials.Select(m=>TileMaterial(m.name.Split(' ')[0])).ToArray();
                renderer.shadowCastingMode=ShadowCastingMode.TwoSided;
                if(name=="ShoreRock")
                {
                    var b=renderer.bounds;
                    for(var i=0;i<8;i++)
                    {
                        var p=b.center+Vector3.Scale(b.extents,new Vector3((i&1)==0?-1:1,(i&2)==0?-1:1,(i&4)==0?-1:1))-obj.transform.position;
                        ShoreRockFootprintRadius=Mathf.Max(ShoreRockFootprintRadius,new Vector2(p.x,p.z).magnitude);
                    }
                }
            }
            obj.SetActive(false);prototypes[name]=obj;
            if(name=="VillageMill"||name=="CanyonTurbine")
            {
                var pivot=obj.GetComponentsInChildren<Transform>(true).FirstOrDefault(t=>t.name.StartsWith("Rotor",StringComparison.Ordinal));
                if(pivot!=null)SaveFacadeDirection(name,pivot.position-obj.transform.position);
            }
            else if(name=="VillageCottage"||name=="LakesideChalet")
            {
                // The unique graphite door handle locates the authored facade.
                // Infer it after FBX import instead of assuming a +/-Z convention.
                foreach(var filter in obj.GetComponentsInChildren<MeshFilter>(true))
                {
                    var renderer=filter.GetComponent<MeshRenderer>();
                    var mesh=filter.sharedMesh;
                    for(var sub=0;sub<mesh.subMeshCount;sub++)
                    {
                        if(renderer.sharedMaterials[sub]!=materials["Graphite"])continue;
                        var indices=mesh.GetTriangles(sub);var center=Vector3.zero;
                        foreach(var i in indices)center+=filter.transform.TransformPoint(mesh.vertices[i]);
                        if(indices.Length>0)SaveFacadeDirection(name,center/indices.Length-obj.transform.position);
                    }
                }
            }
        }

        private void SaveFacadeDirection(string name,Vector3 forward)
        {
            forward.y=0;
            if(forward.sqrMagnitude<.01f)return;
            forward=Mathf.Abs(forward.x)>Mathf.Abs(forward.z)?Vector3.right*Mathf.Sign(forward.x):Vector3.forward*Mathf.Sign(forward.z);
            // An opposite forward vector must turn around the vertical axis;
            // FromToRotation may choose a horizontal axis and flip the building.
            facadeCorrections[name]=Quaternion.AngleAxis(Vector3.SignedAngle(forward,Vector3.forward,Vector3.up),Vector3.up);
        }

        private Material TileMaterial(string name)
        {
            name=name.Replace("(Instance)","").Trim();
            if(materials.TryGetValue(name,out var existing))return existing;
            var mat=new Material(Shader.Find("Standard"));
            mat.mainTexture=Resources.Load<Texture2D>("Art/Textures/ReferenceWorld/"+name);
            mat.color=Color.white;mat.enableInstancing=true;
            if(name=="Sandstone")mat.color=new Color(.80f,.62f,.48f);
            mat.SetFloat("_Glossiness",name=="Glazing"?.72f:name=="Graphite"?.48f:.22f);
            mat.SetFloat("_Metallic",name=="Graphite"?.65f:0f);
            // FBX modules use generated UVs; triplanar material gives stable detail
            // to wood and stone even at bevels and across combined submeshes.
            if(name!="Glazing" && name!="Graphite" && name!="TurbineCoating" && name!="SafetyTip")
            {
                mat.shader=Shader.Find("WindTrace/Reference Surface");
                mat.SetFloat("_TextureScale",name.Contains("Leaves")||name=="PineNeedles"?1.1f:.48f);
            }
            materials[name]=mat;
            return mat;
        }

        private Material Terrain(string name,Color low,Color high,string texture,float strength)
        {
            var mat=new Material(Shader.Find("WindTrace/Stylized Terrain"));
            mat.color=low;mat.SetColor("_SecondaryColor",high);mat.SetFloat("_NoiseScale",.18f);
            mat.SetTexture("_MainTex",Resources.Load<Texture2D>("Art/Textures/Environment/"+texture));
            mat.SetFloat("_TextureStrength",strength);mat.SetFloat("_TextureScale",.32f);mat.SetFloat("_Glossiness",.08f);
            materials[name]=mat;return mat;
        }

        private static void Release(UnityEngine.Object value)
        {
            if(value==null)return;
            if(Application.isPlaying)Destroy(value);else DestroyImmediate(value);
        }

        private IEnumerator MergeStaticGeometrySteps(GameObject root)
        {
            // 80 m is the culling unit. Merge bridge rails, fences, cliffs and
            // distant trees per material, while keeping near-tree LODs and rotors.
            var groups=new Dictionary<Material,List<CombineInstance>>();
            var originals=new List<MeshFilter>();
            var processed=0;
            foreach(var filter in root.GetComponentsInChildren<MeshFilter>(true))
            {
                if(filter.name=="Cycleway")continue;
                if(filter.GetComponentInParent<LODGroup>()!=null)continue;
                var cursor=filter.transform;var animated=false;
                while(cursor!=root.transform && cursor!=null)
                {
                    if(cursor.name.StartsWith("Rotor",StringComparison.Ordinal)){animated=true;break;}
                    cursor=cursor.parent;
                }
                if(animated)continue;
                var renderer=filter.GetComponent<MeshRenderer>();
                if(renderer==null)continue;
                if(renderer.sharedMaterials.Contains(waterfall))continue;
                var mesh=filter.sharedMesh;
                for(var i=0;i<mesh.subMeshCount;i++)
                {
                    var mat=renderer.sharedMaterials[Mathf.Min(i,renderer.sharedMaterials.Length-1)];
                    // Water remains separately drawable for reflection exclusion.
                    if(mat==water)continue;
                    if(!groups.TryGetValue(mat,out var entries))groups[mat]=entries=new List<CombineInstance>();
                    entries.Add(new CombineInstance{mesh=mesh,subMeshIndex=i,transform=root.transform.worldToLocalMatrix*filter.transform.localToWorldMatrix});
                }
                if(renderer.sharedMaterials.Contains(water))continue;
                originals.Add(filter);
                if(++processed%8==0)yield return null;
            }
            foreach(var group in groups)
            {
                var combined=new Mesh{name="Batched "+group.Key.name,indexFormat=IndexFormat.UInt32};
                combined.CombineMeshes(group.Value.ToArray(),true,true);generatedMeshes.Add(combined);
                var obj=new GameObject(combined.name);obj.transform.SetParent(root.transform,false);
                obj.AddComponent<MeshFilter>().sharedMesh=combined;
                obj.AddComponent<MeshRenderer>().sharedMaterial=group.Key;
                yield return null;
            }
            foreach(var filter in originals)
            {
                var source=filter.sharedMesh;
                Release(filter.GetComponent<MeshRenderer>());Release(filter);
                if(generatedMeshes.Remove(source))Release(source);
                if(++processed%8==0)yield return null;
            }
        }

        private void OnDestroy()
        {
            (streamingBuild as IDisposable)?.Dispose();streamingBuild=null;
            foreach(var m in generatedMeshes)Release(m);
            foreach(var m in materials.Values)Release(m);
        }
    }
}
