using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace PZAEC.Surveillance
{
    // A separate screen-space mesh preserves the camera texture shared by screens with different toggles.
    public sealed class ScreenTargetOverlay : IDisposable
    {
        readonly Mesh mesh;
        readonly GameObject holder;
        readonly List<Vector3> vertices=new List<Vector3>(TargetMarkerRules.MaxBoxes*16);
        readonly List<Color32> colors=new List<Color32>(TargetMarkerRules.MaxBoxes*16);
        readonly List<int> triangles=new List<int>(TargetMarkerRules.MaxBoxes*48);
        readonly Material material;
        Vector3 origin,uAxis,vAxis;
        int revision=-1;
        object source;
        public Renderer Renderer {get;private set;}
        public ScreenTargetOverlay(Renderer surface)
        {
            var shader=Shader.Find("Unlit/Color");if(shader==null||!shader.isSupported)shader=Shader.Find("Sprites/Default");
            if(shader==null||!shader.isSupported)throw new InvalidOperationException("Target marker shader unavailable");
            // Use the same outward +Z face as the opaque video material.
            var baseMesh=surface.GetComponent<MeshFilter>().sharedMesh;
            var positions=baseMesh.vertices;var normals=baseMesh.normals;var uv=baseMesh.uv;var indices=baseMesh.triangles;
            bool mapped=false;
            for(int i=0;i<indices.Length;i+=3)
            {
                int a=indices[i],b=indices[i+1],c=indices[i+2];
                if(normals[a].z<.9f||normals[b].z<.9f||normals[c].z<.9f)continue;
                var ab=uv[b]-uv[a];var ac=uv[c]-uv[a];float determinant=ab.x*ac.y-ab.y*ac.x;if(Mathf.Abs(determinant)<.00001f)continue;
                uAxis=((positions[b]-positions[a])*ac.y-(positions[c]-positions[a])*ab.y)/determinant;
                vAxis=((positions[c]-positions[a])*ab.x-(positions[b]-positions[a])*ac.x)/determinant;
                origin=positions[a]-uAxis*uv[a].x-vAxis*uv[a].y;
                // Draw just outside the physical +Z face.
                origin.z=baseMesh.bounds.max.z+.04f;mapped=true;break;
            }
            if(!mapped)throw new InvalidOperationException("Monitor front-face UV mapping unavailable");
            material=new Material(shader){name="Surveillance target red",color=new Color(1,.05f,.04f,1),renderQueue=3001};
            if(material.HasProperty("_ZWrite"))material.SetInt("_ZWrite",0);
            if(material.HasProperty("_MainTex"))material.mainTexture=Texture2D.whiteTexture;
            holder=new GameObject("SurveillanceTargetBoxes");holder.layer=surface.gameObject.layer;holder.transform.SetParent(surface.transform,false);
            mesh=new Mesh{name="Surveillance target outlines"};mesh.MarkDynamic();holder.AddComponent<MeshFilter>().sharedMesh=mesh;
            var renderer=holder.AddComponent<MeshRenderer>();renderer.sharedMaterial=material;renderer.shadowCastingMode=ShadowCastingMode.Off;renderer.receiveShadows=false;
            Renderer=renderer;Renderer.enabled=false;
        }
        void Quad(float left,float bottom,float right,float top)
        {
            int start=vertices.Count;
            vertices.Add(origin+uAxis*left+vAxis*bottom);vertices.Add(origin+uAxis*right+vAxis*bottom);
            vertices.Add(origin+uAxis*right+vAxis*top);vertices.Add(origin+uAxis*left+vAxis*top);
            colors.Add(new Color32(255,255,255,255));colors.Add(new Color32(255,255,255,255));
            colors.Add(new Color32(255,255,255,255));colors.Add(new Color32(255,255,255,255));
            triangles.Add(start);triangles.Add(start+1);triangles.Add(start+2);triangles.Add(start);triangles.Add(start+2);triangles.Add(start+3);
            // Front-face UVs may be mirrored; double winding works with both fallback shaders.
            triangles.Add(start+2);triangles.Add(start+1);triangles.Add(start);triangles.Add(start+3);triangles.Add(start+2);triangles.Add(start);
        }
        public void Hide(){if(Renderer!=null)Renderer.enabled=false;}
        public void Show(TargetMarkerDetector detector,int width,int height)
        {
            if(source!=detector||revision!=detector.Revision)
            {
                source=detector;revision=detector.Revision;vertices.Clear();colors.Clear();triangles.Clear();
                foreach(var box in detector.Boxes)
                {
                    float dx=Mathf.Min(2f/width,(box.Right-box.Left)*.25f),dy=Mathf.Min(2f/height,(box.Top-box.Bottom)*.25f);
                    Quad(box.Left,box.Bottom,box.Right,box.Bottom+dy);Quad(box.Left,box.Top-dy,box.Right,box.Top);
                    Quad(box.Left,box.Bottom+dy,box.Left+dx,box.Top-dy);Quad(box.Right-dx,box.Bottom+dy,box.Right,box.Top-dy);
                }
                mesh.Clear();mesh.SetVertices(vertices);mesh.SetColors(colors);mesh.SetTriangles(triangles,0);mesh.RecalculateBounds();
            }
            Renderer.enabled=detector.Boxes.Count>0;
        }
        public void Dispose()
        {Hide();UnityEngine.Object.Destroy(holder);UnityEngine.Object.Destroy(mesh);UnityEngine.Object.Destroy(material);}
    }
}
