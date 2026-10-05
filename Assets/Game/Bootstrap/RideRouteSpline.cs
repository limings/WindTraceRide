using System;
using System.Collections.Generic;
using UnityEngine;

namespace WindTraceRide.Bootstrap
{
    /// <summary>Spatially filtered, arc-length-resampled route shared by road and rider.</summary>
    public sealed class RideRouteSpline
    {
        private readonly Vector3[] points;
        private readonly float step;
        public float Length { get; }

        public RideRouteSpline(IReadOnlyList<Vector3> source,float length)
        {
            if(source==null||source.Count<2)throw new ArgumentException("Route requires two points");
            Length=length;
            var distances=new float[source.Count];
            for(var i=1;i<source.Count;i++)distances[i]=distances[i-1]+Vector3.Distance(source[i-1],source[i]);
            var sourceLength=distances[distances.Length-1];
            if(sourceLength<1f||length<1f)throw new ArgumentException("Route length must be positive");
            Vector3 SourceAt(float d)
            {
                if(d<=0)return source[0]+(source[1]-source[0]).normalized*d;
                if(d>=sourceLength)return source[source.Count-1]+(source[source.Count-1]-source[source.Count-2]).normalized*(d-sourceLength);
                var high=Array.BinarySearch(distances,d);if(high<0)high=~high;
                high=Mathf.Clamp(high,1,source.Count-1);
                return Vector3.Lerp(source[high-1],source[high],(d-distances[high-1])/Mathf.Max(.001f,distances[high]-distances[high-1]));
            }
            var controlCount=Mathf.CeilToInt(sourceLength/8f);
            var controlStep=sourceLength/controlCount;
            var controls=new Vector3[controlCount+1];
            for(var i=0;i<=controlCount;i++)
            {
                var sum=Vector3.zero;var weight=0f;
                // Fixed world-space filtering removes short GIS zigzags without
                // adding artificial turns on straight roads. Endpoint samples
                // extrapolate instead of flattening the start/end tangent.
                for(var offset=-48f;offset<=48;offset+=4)
                {
                    var w=Mathf.Exp(-offset*offset/(2*16f*16f));sum+=SourceAt(i*controlStep+offset)*w;weight+=w;
                }
                controls[i]=sum/weight;
            }
            var dense=new Vector3[controlCount*4+1];var arc=new float[dense.Length];
            for(var i=0;i<dense.Length;i++)
            {
                dense[i]=Interpolate(controls,i/4f);
                if(i>0)arc[i]=arc[i-1]+Vector3.Distance(dense[i-1],dense[i]);
            }
            var finalCount=Mathf.CeilToInt(length/2f);step=length/finalCount;points=new Vector3[finalCount+1];
            for(var i=0;i<=finalCount;i++)
            {
                var d=arc[arc.Length-1]*i/finalCount;
                var high=Array.BinarySearch(arc,d);if(high<0)high=~high;high=Mathf.Clamp(high,1,arc.Length-1);
                points[i]=Vector3.Lerp(dense[high-1],dense[high],(d-arc[high-1])/Mathf.Max(.001f,arc[high]-arc[high-1]));
            }
        }

        public Vector3 Position(float distance)
        {
            if(distance<0)return points[0]+Forward(0)*distance;
            if(distance>Length)return points[points.Length-1]+Forward(Length)*(distance-Length);
            return Interpolate(points,distance/step);
        }

        public Vector3 Forward(float distance)
        {
            var before=Interpolate(points,Mathf.Max(0,distance-2f)/step);
            var after=Interpolate(points,Mathf.Min(Length,distance+2f)/step);
            var forward=after-before;forward.y=0;
            return forward.sqrMagnitude>.0001f?forward.normalized:Vector3.forward;
        }

        private static Vector3 Interpolate(Vector3[] values,float sample)
        {
            var index=Mathf.Clamp(Mathf.FloorToInt(sample),0,values.Length-2);var t=Mathf.Clamp01(sample-index);
            var p1=values[index];var p2=values[index+1];
            var p0=index>0?values[index-1]:2*p1-p2;
            var p3=index+2<values.Length?values[index+2]:2*p2-p1;
            return .5f*((2*p1)+(-p0+p2)*t+(2*p0-5*p1+4*p2-p3)*t*t+(-p0+3*p1-3*p2+p3)*t*t*t);
        }
    }
}
