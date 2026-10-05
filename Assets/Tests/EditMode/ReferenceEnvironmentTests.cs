using System.Linq;
using NUnit.Framework;
using UnityEngine;
using WindTraceRide.Bootstrap;

namespace WindTraceRide.Tests
{
    public sealed class ReferenceEnvironmentTests
    {
        [TestCase("CanyonTurbine",9f)]
        [TestCase("VillageMill",7f)]
        [TestCase("VillageCottage",4f)]
        [TestCase("LakesideChalet",4f)]
        [TestCase("ForestPine",7f)]
        [TestCase("VillageOak",5f)]
        public void ImportedModulesAreUprightAndGrounded(string name,float minimumHeight)
        {
            var source=Resources.Load<GameObject>("Art/Models/ReferenceWorld/"+name);
            Assert.That(source,Is.Not.Null);
            var instance=Object.Instantiate(source);
            try
            {
                var renderers=instance.GetComponentsInChildren<Renderer>();
                var bounds=renderers[0].bounds;
                foreach(var renderer in renderers)bounds.Encapsulate(renderer.bounds);
                Assert.That(bounds.size.y,Is.GreaterThan(minimumHeight),name+" lays on the ground");
                Assert.That(bounds.min.y,Is.InRange(-.2f,.15f),name+" root not at ground");
                Assert.That(bounds.center.y,Is.GreaterThan(minimumHeight*.3f));
            }
            finally{Object.DestroyImmediate(instance);}
        }

        [TestCase(0,9600f)]
        [TestCase(1,14400f)]
        [TestCase(2,33600f)]
        public void RouteStreamingStaysBoundedAndNeverDropsRoad(int index,float length)
        {
            var root=new GameObject("Environment test");
            try
            {
                var environment=root.AddComponent<ReferenceRideEnvironment>();
                environment.Initialize(index,length,d=>new ReferenceRideEnvironment.Frame
                {Center=new Vector3(0,0,d),Forward=Vector3.forward,Right=Vector3.right});
                foreach(var d in new[]{0f,79f,80f,160f,310f,480f,length*.25f,length*.75f,length-8})
                {
                    environment.UpdateDistance(d);
                    Assert.That(environment.ActiveChunks,Is.InRange(1,ReferenceRideEnvironment.PoolSize));
                    Assert.That(environment.Chunks.Count(c=>c.GetComponentsInChildren<MeshFilter>().Any(m=>m.name=="Cycleway")),Is.EqualTo(environment.ActiveChunks));
                    foreach(var placed in environment.Chunks.SelectMany(c=>c.GetComponentsInChildren<Transform>()))
                    {
                        if(placed.name=="VillageCottage"||placed.name=="LakesideChalet"||placed.name=="VillageMill"||placed.name=="CanyonTurbine")
                            Assert.That(Vector3.Dot(placed.up,Vector3.up),Is.GreaterThan(.999f),placed.name+" facade correction flipped the vertical axis");
                        if(index==2 && placed.name=="ShoreRock")
                            Assert.That(Mathf.Abs(placed.position.x)-environment.ShoreRockFootprintRadius*placed.lossyScale.x,Is.GreaterThanOrEqualTo(3f),"Stream bank rock invaded road or shoulder");
                    }
                    foreach(var renderer in root.GetComponentsInChildren<Renderer>())
                        foreach(var material in renderer.sharedMaterials)
                        {
                            Assert.That(material,Is.Not.Null);
                            Assert.That(material.shader.name,Is.Not.EqualTo("Hidden/InternalErrorShader"));
                        }
                }
                if(index==1)
                    Assert.That(environment.SurfaceHeight(40f,0f),Is.LessThan(-14f),"Bridge must span a real gorge");
                if(index==2)
                    Assert.That(environment.SurfaceHeight(33f,11f),Is.EqualTo(environment.SurfaceHeight(35f,13f)).Within(.02f),"House platform must be level");
            }
            finally{Object.DestroyImmediate(root);}
        }

        [Test] public void VillageBrookCrossesTrailAndDoesNotRemainAStraightCanal()
        {
            Assert.That(ReferenceRideEnvironment.Brook(0f),Is.LessThan(-8));
            Assert.That(ReferenceRideEnvironment.Brook(164f),Is.GreaterThan(8));
            Assert.That(ReferenceRideEnvironment.Brook(300f),Is.LessThan(-8));
            Assert.That(ReferenceRideEnvironment.Brook(390f),Is.GreaterThan(8));
            Assert.That(ReferenceRideEnvironment.Brook(480f),Is.EqualTo(ReferenceRideEnvironment.Brook(0f)).Within(.01f));
        }

        [Test] public void VillageRocksLeaveClearanceAlongCurvedRoad()
        {
            ReferenceRideEnvironment.Frame Sample(float d)
            {
                var forward=new Vector3(.22f*Mathf.Cos(d*.011f)+.155f*Mathf.Cos(d*.031f),0,1).normalized;
                return new ReferenceRideEnvironment.Frame{Center=new Vector3(20*Mathf.Sin(d*.011f)+5*Mathf.Sin(d*.031f),0,d),Forward=forward,Right=Vector3.Cross(Vector3.up,forward)};
            }
            var root=new GameObject("Curved village rock test");
            try
            {
                var environment=root.AddComponent<ReferenceRideEnvironment>();environment.Initialize(2,33600,Sample);
                Assert.That(environment.ShoreRockFootprintRadius,Is.GreaterThan(.5f));
                foreach(var distance in new[]{0f,160f,390f,1200f,25194f,33582f})
                {
                    environment.UpdateDistance(distance);
                    foreach(var rock in environment.Chunks.SelectMany(c=>c.GetComponentsInChildren<Transform>()).Where(t=>t.name=="ShoreRock"))
                    {
                        var center=rock.position;center.y=0;
                        var minimum=float.MaxValue;
                        for(var d=Mathf.Max(0,center.z-12);d<=center.z+12;d+=.25f)
                            minimum=Mathf.Min(minimum,Vector3.Distance(center,Sample(d).Center));
                        Assert.That(minimum-environment.ShoreRockFootprintRadius*rock.lossyScale.x,Is.GreaterThanOrEqualTo(3f),"Rock overlaps curved village trail");
                    }
                }
            }
            finally{Object.DestroyImmediate(root);}
        }
    }
}
