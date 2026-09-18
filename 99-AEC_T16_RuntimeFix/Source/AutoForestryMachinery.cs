using System;
using System.Collections.Generic;
using UnityEngine;

namespace AECT16RuntimeFix
{
    // Visual components occupy the front work strip, clear of the original building.
    public static class AutoForestryMachinery
    {
        public static GameObject Box(Transform parent,string name,Vector3 pos,Vector3 size,Material mat,bool collide=false)
        {
            var g=GameObject.CreatePrimitive(PrimitiveType.Cube); g.name=name;
            g.transform.SetParent(parent,false); g.transform.localPosition=pos; g.transform.localScale=size;
            g.GetComponent<Renderer>().sharedMaterial=mat;
            if(!collide) UnityEngine.Object.DestroyImmediate(g.GetComponent<Collider>());
            return g;
        }
        static GameObject Cylinder(Transform parent,string name,Vector3 pos,float radius,float length,Material mat,Quaternion rotation)
        {
            var g=GameObject.CreatePrimitive(PrimitiveType.Cylinder); g.name=name;
            g.transform.SetParent(parent,false); g.transform.localPosition=pos;
            g.transform.localRotation=rotation; g.transform.localScale=new Vector3(radius*2,length*.5f,radius*2);
            g.GetComponent<Renderer>().sharedMaterial=mat;
            UnityEngine.Object.DestroyImmediate(g.GetComponent<Collider>()); return g;
        }
        static void Pipe(Transform parent,string name,Vector3 a,Vector3 b,float radius,Material mat)
        { Cylinder(parent,name,(a+b)*.5f,radius,Vector3.Distance(a,b),mat,Quaternion.FromToRotation(Vector3.up,b-a)); }

        public static Mesh SawMesh()
        {
            const int count=96;
            var v=new List<Vector3>(); var uv=new List<Vector2>(); var tris=new List<int>();
            for(int side=0;side<2;side++)
            {
                v.Add(new Vector3(0,0,(side-.5f)*.035f)); uv.Add(new Vector2(.5f,.5f));
                for(int i=0;i<count;i++)
                {
                    float a=i*Mathf.PI*2/count, r=i%3==0?.34f:.313f;
                    var p=new Vector3(Mathf.Cos(a)*r,Mathf.Sin(a)*r,(side-.5f)*.035f);
                    v.Add(p); uv.Add(new Vector2(p.x/.7f+.5f,p.y/.7f+.5f));
                }
                for(int i=0;i<count;i++)
                {
                    int c=side*(count+1),a=c+1+i,b=c+1+(i+1)%count;
                    tris.AddRange(side==0?new[]{c,b,a}:new[]{c,a,b});
                }
            }
            for(int i=0;i<count;i++)
            {
                int a=1+i,b=1+(i+1)%count,c=a+count+1,d=b+count+1;
                tris.AddRange(new[]{a,b,c,b,d,c});
            }
            var mesh=new Mesh{name="Forestry32ToothBlade"}; mesh.SetVertices(v);mesh.SetUVs(0,uv);
            mesh.SetTriangles(tris,0);mesh.RecalculateNormals();mesh.RecalculateBounds();mesh.RecalculateTangents();return mesh;
        }

        public static void Build(Transform root,Material frame,Material metal,Material dark,Material wood,Material amber)
        {
            var group=new GameObject("MachineryNear");group.transform.SetParent(root,false);var p=group.transform;
            // Two deck halves leave a real .07 m longitudinal slot for the .035 m blade.
            foreach(float z in new[]{-2.3925f,-1.9275f})
                Box(p,"SawTable",new Vector3(-2.8f,.71f,z),new Vector3(2.9f,.13f,.395f),frame,true);
            foreach(float x in new[]{-4f,-1.6f}) foreach(float z in new[]{-2.48f,-1.84f})
                Box(p,"TableLeg",new Vector3(x,.39f,z),new Vector3(.09f,.62f,.09f),frame);
            foreach(float z in new[]{-2.57f,-1.75f})
                Box(p,"FeedGuide",new Vector3(-2.8f,.89f,z),new Vector3(2.94f,.06f,.045f),amber);
            foreach(float z in new[]{-2.38f,-1.94f})
                Box(p,"SawBridge",new Vector3(-2.7f,.825f,z),new Vector3(.94f,.10f,.10f),frame);
            foreach(float x in new[]{-4.05f,-3.78f,-3.51f,-3.24f,-2.16f,-1.89f,-1.62f})
                Cylinder(p,"FeedRoller"+x,new Vector3(x,.81f,-2.16f),.065f,.72f,metal,Quaternion.Euler(90,0,0));
            var saw=new GameObject("SawBlade");saw.transform.SetParent(p,false);saw.transform.localPosition=new Vector3(-2.7f,1.05f,-2.16f);
            saw.AddComponent<MeshFilter>().sharedMesh=SawMesh();saw.AddComponent<MeshRenderer>().sharedMaterial=metal;
            Box(p,"BladeGuard",new Vector3(-2.7f,1.39f,-2.16f),new Vector3(.48f,.09f,.15f),amber);
            Pipe(p,"SawAxle",new Vector3(-2.7f,1.05f,-2.55f),new Vector3(-2.7f,1.05f,-1.8f),.018f,metal);
            Box(p,"GuardSupport",new Vector3(-2.7f,1.05f,-1.8f),new Vector3(.06f,.6f,.07f),frame);
            Cylinder(p,"Motor",new Vector3(-1.45f,.43f,-2.16f),.19f,.52f,frame,Quaternion.Euler(90,0,0));
            for(int i=0;i<7;i++)
                Cylinder(p,"MotorCoolingRing",new Vector3(-1.45f,.43f,-2.38f+i*.07f),.21f,.022f,metal,Quaternion.Euler(90,0,0));
            var sawPulley=Cylinder(p,"SawPulley",new Vector3(-2.7f,1.05f,-2.52f),.14f,.04f,metal,Quaternion.Euler(90,0,0));
            var drivePulley=Cylinder(p,"DrivePulley",new Vector3(-1.45f,.43f,-2.52f),.11f,.04f,metal,Quaternion.Euler(90,0,0));
            foreach(var pulley in new[]{sawPulley,drivePulley})
                Box(pulley.transform,"PulleySpoke",new Vector3(0,1.03f,0),new Vector3(.80f,.08f,.075f),dark);
            var c1=new Vector3(-2.7f,1.05f,-2.52f);var c2=new Vector3(-1.45f,.43f,-2.52f);
            var axis=(c2-c1).normalized;var perpendicular=new Vector3(-axis.y,axis.x,0);
            float slope=(.14f-.11f)/Vector3.Distance(c1,c2);
            foreach(float side in new[]{-1f,1f})
            {
                var normal=axis*slope+perpendicular*(side*Mathf.Sqrt(1-slope*slope));
                Pipe(p,"DriveBelt",c1+normal*.14f,c2+normal*.11f,.012f,dark);
            }
            Pipe(p,"DustExtractionA",new Vector3(-2.7f,.59f,-1.8f),new Vector3(-1.1f,.59f,-1.8f),.075f,metal);
            Pipe(p,"DustExtractionB",new Vector3(-1.1f,.59f,-1.8f),new Vector3(-1.1f,1.3f,-1.8f),.075f,metal);
            Cylinder(p,"SawdustBin",new Vector3(-.75f,.40f,-1.85f),.24f,.62f,frame,Quaternion.identity);
            Pipe(p,"MotorCable",new Vector3(-1.4f,.25f,-2.3f),new Vector3(-.3f,.15f,-2.6f),.018f,dark);
            // Boards enter/leave inside covers; no visible teleport at the cycle boundary.
            foreach(float x in new[]{-3.94f,-1.67f})
            {
                Box(p,"FeedCover",new Vector3(x,1.12f,-2.16f),new Vector3(.66f,.07f,.62f),frame);
                foreach(float z in new[]{-2.455f,-1.865f})
                    Box(p,"FeedCoverSide",new Vector3(x,.95f,z),new Vector3(.66f,.30f,.03f),frame);
            }
            for(int i=0;i<2;i++)
            {
                for(int part=0;part<3;part++)
                {
                    var slice=ForestryMotion.Slice(0,i,part);
                    var board=Box(p,(part==0?"FeedTimber":part==1?"CutTimberL":"CutTimberR")+i,
                        new Vector3(slice.x,.935f,slice.z),new Vector3(Mathf.Max(.0001f,slice.length),.12f,part==0?.23f:.095f),wood);
                    board.SetActive(slice.length>.0001f);
                }
            }
            Box(p,"OutputPallet",new Vector3(3.6f,.14f,-2.18f),new Vector3(2.45f,.12f,1.1f),wood,true);
            // Racks remain visible when inventory is empty.
            foreach(float x in new[]{2.42f,4.78f})
                Box(p,"RackUpright",new Vector3(x,.60f,-2.0f),new Vector3(.07f,1f,.07f),frame);
        }
    }
}
