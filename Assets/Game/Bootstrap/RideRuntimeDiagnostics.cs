using System;
using UnityEngine;
using UnityEngine.Profiling;

namespace WindTraceRide.Bootstrap
{
    public sealed class RideRuntimeDiagnostics : MonoBehaviour
    {
        private readonly float[] samples=new float[240];
        private int count;
        private float started;
        private ReferenceRideEnvironment environment;
        private double maxStreamMs;
        private void Awake(){enabled=Debug.isDebugBuild;started=Time.unscaledTime;}
        private void Update()
        {
            if(Time.unscaledTime-started<4f)return;
            if(environment==null)environment=GetComponentInChildren<ReferenceRideEnvironment>();
            if(environment!=null)maxStreamMs=Math.Max(maxStreamMs,environment.LastStreamSliceMilliseconds);
            samples[count++]=Time.unscaledDeltaTime*1000;
            if(count<samples.Length)return;
            var sum=0f;foreach(var value in samples)sum+=value;
            Array.Sort(samples);
            Debug.Log($"RIDE_PERF fps={240000f/sum:0.0} p95ms={samples[228]:0.0} maxms={samples[239]:0.0} allocatedMB={Profiler.GetTotalAllocatedMemoryLong()/1048576f:0.0} streamSliceMs={maxStreamMs:0.00}");
            count=0;maxStreamMs=0;
        }
    }
}
