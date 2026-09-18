using System;
using System.Collections.Generic;
using UnityEngine;
namespace PZAEC.M1
{
    // Advect the original textured tread geometry along a closed profile. Keeps
    // atlas UVs fixed on each moving link; never scrolls the whole source atlas.
    public sealed class TrackMotion
    {
        public readonly bool Left;
        readonly Mesh mesh,original;readonly MeshFilter filter;readonly Renderer renderer;
        readonly Vector3[] vertices,normals,output,normalOutput;readonly float[] arcs,normalOffset,tangentOffset,nx,nt,nn;
        readonly Vector2[] points,tangents;readonly float[] lengths,starts;readonly float total;
        double previous=double.NaN;
        public TrackMotion(Transform root,bool left)
        {
            Left=left;filter=root.GetComponent<MeshFilter>();renderer=root.GetComponent<Renderer>();original=filter.sharedMesh;mesh=UnityEngine.Object.Instantiate(original);mesh.name=original.name+"_instance";mesh.MarkDynamic();filter.sharedMesh=mesh;
            vertices=mesh.vertices;normals=mesh.normals;output=new Vector3[vertices.Length];normalOutput=new Vector3[vertices.Length];
            // Same profile for all LODs and both sides; inferred from the original
            // tread outline, not a generic ellipse around the road wheels.
            var control=new[]{new Vector2(-2.25f,.1027f),new Vector2(1.98f,.1027f),new Vector2(2.62f,.34f),new Vector2(3.14f,.61f),new Vector2(3.28f,.83f),new Vector2(3.12f,1.05f),new Vector2(2.63f,1.1246f),new Vector2(-2.43f,1.1246f),new Vector2(-2.96f,1.04f),new Vector2(-3.24f,.87f),new Vector2(-3.28f,.68f),new Vector2(-3.08f,.43f),new Vector2(-2.70f,.23f)};
            // Subdivide the contour before transporting links, avoiding large
            // tangent jumps at the front and rear wheel bends.
            var smooth=new List<Vector2>();for(int i=0;i<control.Length;i++)for(int j=0;j<8;j++){
                float t=j/8f;var a=control[(i+control.Length-1)%control.Length];var b=control[i];var c=control[(i+1)%control.Length];var d=control[(i+2)%control.Length];
                smooth.Add(.5f*((2*b)+(-a+c)*t+(2*a-5*b+4*c-d)*t*t+(-a+3*b-3*c+d)*t*t*t));
            }points=smooth.ToArray();
            tangents=new Vector2[points.Length];lengths=new float[points.Length];starts=new float[points.Length];float length=0;
            for(int i=0;i<points.Length;i++){var d=points[(i+1)%points.Length]-points[i];starts[i]=length;lengths[i]=d.magnitude;tangents[i]=d.normalized;length+=lengths[i];}total=length;
            arcs=new float[vertices.Length];normalOffset=new float[vertices.Length];tangentOffset=new float[vertices.Length];nx=new float[vertices.Length];nt=new float[vertices.Length];nn=new float[vertices.Length];
            for(int v=0;v<vertices.Length;v++){
                var p=new Vector2(vertices[v].z,vertices[v].y);float best=float.PositiveInfinity;int edge=0;float along=0;
                for(int i=0;i<points.Length;i++){float d=Mathf.Clamp(Vector2.Dot(p-points[i],tangents[i]),0,lengths[i]);float error=(p-points[i]-tangents[i]*d).sqrMagnitude;if(error<best){best=error;edge=i;along=d;}}
                var tangent=tangents[edge];var normal=new Vector2(-tangent.y,tangent.x);var residual=p-points[edge]-tangent*along;arcs[v]=starts[edge]+along;
                normalOffset[v]=Vector2.Dot(residual,normal);tangentOffset[v]=Vector2.Dot(residual,tangent);var n=new Vector2(normals[v].z,normals[v].y);nx[v]=normals[v].x;nt[v]=Vector2.Dot(n,tangent);nn[v]=Vector2.Dot(n,normal);
            }
        }
        public void Move(double distance)
        {
            if(!renderer.isVisible||Math.Abs(distance-previous)<.001)return;previous=distance;float offset=(float)(-distance%total);
            for(int v=0;v<vertices.Length;v++){
                float arc=Mathf.Repeat(arcs[v]+offset,total);int edge=points.Length-1;for(int i=0;i<points.Length-1;i++)if(arc<starts[i+1]){edge=i;break;}
                var t=tangents[edge];var n=new Vector2(-t.y,t.x);var p=points[edge]+t*(arc-starts[edge]+tangentOffset[v])+n*normalOffset[v];var normal=t*nt[v]+n*nn[v];
                output[v]=new Vector3(vertices[v].x,p.y,p.x);normalOutput[v]=new Vector3(nx[v],normal.y,normal.x);
            }
            mesh.vertices=output;mesh.normals=normalOutput;mesh.RecalculateTangents();
            // Preserve the enclosing source bounds; the closed path stays inside them.
        }
        public void Dispose(){if(filter!=null)filter.sharedMesh=original;if(mesh!=null)UnityEngine.Object.Destroy(mesh);}
    }
}
