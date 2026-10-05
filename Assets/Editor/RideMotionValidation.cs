using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEngine;
using WindTraceRide.Bootstrap;

namespace WindTraceRide.Editor
{
    public static class RideMotionValidation
    {
        public static void Audit()
        {
            var root=new GameObject("Ride motion audit");
            try
            {
                root.AddComponent<RideWorldController>().SetLevel(0);
                var type=typeof(RideWorldController);
                type.GetMethod("LoadGeoRoute",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,null);
                var sample=type.GetMethod("RouteAt",BindingFlags.Static|BindingFlags.NonPublic);
                var frameType=sample.ReturnType;
                var center=frameType.GetField("center");var tangent=frameType.GetField("tangent");
                Vector3 Position(float d)=>(Vector3)center.GetValue(sample.Invoke(null,new object[]{d}));
                Vector3 Forward(float d)=>(Vector3)tangent.GetValue(sample.Invoke(null,new object[]{d}));
                var turns=new List<(float d,float turn,float speed,float mismatch)>();
                for(var d=1f;d<9590f;d+=.5f)
                {
                    var chord=Position(d+.25f)-Position(d-.25f);chord.y=0;
                    turns.Add((d,Vector3.Angle(Forward(d-.25f),Forward(d+.25f))/.5f,chord.magnitude/.5f,Vector3.Angle(Forward(d),chord)));
                }
                var report=$"LAKE_ROUTE samples={turns.Count} maxTurnDegPerM={turns.Max(t=>t.turn):0.000} minSpeedRatio={turns.Min(t=>t.speed):0.000} maxSpeedRatio={turns.Max(t=>t.speed):0.000} maxHeadingMismatch={turns.Max(t=>t.mismatch):0.000}\n";
                foreach(var t in turns.OrderByDescending(t=>t.turn).Take(12))report+=$"distance={t.d:0.0} turnDegPerM={t.turn:0.000} speedRatio={t.speed:0.000} headingMismatch={t.mismatch:0.000}\n";
                var environment=root.AddComponent<ReferenceRideEnvironment>();
                environment.Initialize(0,9920,d=>
                {
                    var f=Forward(d);
                    return new ReferenceRideEnvironment.Frame{Center=Position(d),Forward=f,Right=Vector3.Cross(Vector3.up,f).normalized};
                });
                var timings=new List<double>();
                for(var d=80f;d<=1600;d+=80)
                {
                    var clock=Stopwatch.StartNew();environment.UpdateDistance(d);clock.Stop();timings.Add(clock.Elapsed.TotalMilliseconds);
                }
                report+=$"STREAM synchronousTransitions={timings.Count} meanMs={timings.Average():0.00} maxMs={timings.Max():0.00}\n";
                Directory.CreateDirectory("TestArtifacts/reference-world");File.WriteAllText("TestArtifacts/reference-world/motion-audit.txt",report);
                UnityEngine.Debug.Log(report);
            }
            finally{UnityEngine.Object.DestroyImmediate(root);}
        }
    }
}
