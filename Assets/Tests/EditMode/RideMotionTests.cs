using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using WindTraceRide.Bootstrap;

namespace WindTraceRide.Tests
{
    public sealed class RideMotionTests
    {
        [Test] public void UnevenStraightSourceNeverInventsTurnOrSpeedJump()
        {
            var route=new RideRouteSpline(new[]{Vector3.zero,new Vector3(0,0,1),new Vector3(0,0,80),new Vector3(0,0,81),new Vector3(0,0,300)},300);
            for(var d=0f;d<300;d+=.5f)
            {
                Assert.That(Vector3.Angle(route.Forward(d),Vector3.forward),Is.LessThan(.01f));
                Assert.That(Vector3.Distance(route.Position(d+.25f),route.Position(d-.25f))/.5f,Is.EqualTo(1).Within(.01f));
            }
            Assert.That(Vector3.Distance(route.Position(301),route.Position(300)),Is.EqualTo(1).Within(.01f));
        }

        [Test] public void CompleteLakeRouteHasNoHeadingFlipsOrInterpolationSpeedSurges()
        {
            var root=new GameObject("Lake motion regression");
            try
            {
                root.AddComponent<RideWorldController>().SetLevel(0);
                var type=typeof(RideWorldController);
                type.GetMethod("LoadGeoRoute",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,null);
                var sample=type.GetMethod("RouteAt",BindingFlags.Static|BindingFlags.NonPublic);
                var center=sample.ReturnType.GetField("center");var tangent=sample.ReturnType.GetField("tangent");
                Vector3 Position(float d)=>(Vector3)center.GetValue(sample.Invoke(null,new object[]{d}));
                Vector3 Forward(float d)=>(Vector3)tangent.GetValue(sample.Invoke(null,new object[]{d}));
                var minSpeed=float.MaxValue;var maxSpeed=0f;var maxTurn=0f;
                for(var d=1f;d<9590;d+=.5f)
                {
                    var speed=Vector3.Distance(Position(d+.25f),Position(d-.25f))/.5f;
                    minSpeed=Mathf.Min(minSpeed,speed);maxSpeed=Mathf.Max(maxSpeed,speed);
                    maxTurn=Mathf.Max(maxTurn,Vector3.Angle(Forward(d-.25f),Forward(d+.25f))/.5f);
                }
                Assert.That(maxTurn,Is.LessThan(3f),"GIS micro-turn or heading reversal remains");
                Assert.That(minSpeed,Is.GreaterThan(.7f));Assert.That(maxSpeed,Is.LessThan(1.3f));
                Assert.That(maxSpeed-minSpeed,Is.LessThan(.06f),"Arc-length pace is discontinuous");
            }
            finally{UnityEngine.Object.DestroyImmediate(root);}
        }

        [TestCase(0)] [TestCase(1)] [TestCase(2)]
        public void StagedInactiveChunkPreservesRoadMaterialsAndTreeLods(int level)
        {
            var root=new GameObject("Staged streaming regression");
            var chunk=new GameObject("Hidden staged chunk");chunk.transform.SetParent(root.transform);chunk.SetActive(false);
            try
            {
                var environment=root.AddComponent<ReferenceRideEnvironment>();
                environment.Initialize(level,34000,d=>new ReferenceRideEnvironment.Frame{Center=new Vector3(0,0,d),Forward=Vector3.forward,Right=Vector3.right});
                var method=typeof(ReferenceRideEnvironment).GetMethod("BuildChunkSteps",BindingFlags.Instance|BindingFlags.NonPublic);
                var steps=(IEnumerator)method.Invoke(environment,new object[]{chunk,12});var count=0;
                while(steps.MoveNext()){count++;Assert.That(chunk.activeSelf,Is.False);}
                Assert.That(count,Is.GreaterThan(10));chunk.SetActive(true);
                Assert.That(chunk.GetComponentsInChildren<MeshFilter>().Any(f=>f.name=="Cycleway"),Is.True);
                Assert.That(chunk.GetComponentsInChildren<MeshRenderer>().Any(r=>r.name.StartsWith("Batched ",StringComparison.Ordinal)),Is.True);
                foreach(var lod in chunk.GetComponentsInChildren<LODGroup>())
                    foreach(var group in lod.GetLODs())Assert.That(group.renderers.Length,Is.GreaterThan(0));
            }
            finally{UnityEngine.Object.DestroyImmediate(root);}
        }
    }
}
