using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace AECT16RuntimeFix
{
    // Static woodworking corner. The native collector remains the only interaction.
    public static class AutoForestryWoodworking
    {
        static GameObject Box(Transform p,string n,Vector3 at,Vector3 size,Material m,bool collision=false)
        { return AutoForestryMachinery.Box(p,n,at,size,m,collision); }

        static GameObject Rod(Transform p,string name,Vector3 a,Vector3 b,float radius,Material m)
        {
            // Twelve-sided rods retain a round silhouette without primitive-cylinder cost.
            const int sides=12;var v=new List<Vector3>();var uv=new List<Vector2>();var f=new List<int>();
            float length=(b-a).magnitude;
            for(int end=0;end<2;end++)for(int i=0;i<sides;i++)
            {
                float angle=i*Mathf.PI*2/sides;
                v.Add(new Vector3(Mathf.Cos(angle)*radius,(end-.5f)*length,Mathf.Sin(angle)*radius));
                uv.Add(new Vector2(i/(float)sides,end));
            }
            for(int i=0;i<sides;i++)
            {
                int j=(i+1)%sides;f.AddRange(new[]{i,i+sides,j,j,i+sides,j+sides});
                if(i>0&&i<sides-1)f.AddRange(new[]{0,i,j,sides,sides+j,sides+i});
            }
            var g=MeshObject(p,name,v,uv,f,m);g.transform.localPosition=(a+b)*.5f;
            g.transform.localRotation=Quaternion.FromToRotation(Vector3.up,b-a);return g;
        }
        static GameObject MeshObject(Transform p,string name,List<Vector3> v,List<Vector2> uv,List<int> faces,Material m)
        {
            var mesh=ForestryResources.Own(new Mesh{name="Woodwork_"+name});mesh.SetVertices(v);mesh.SetUVs(0,uv);mesh.SetTriangles(faces,0);
            mesh.RecalculateNormals();mesh.RecalculateBounds();mesh.RecalculateTangents();
            var g=new GameObject(name);g.transform.SetParent(p,false);
            g.AddComponent<MeshFilter>().sharedMesh=mesh;g.AddComponent<MeshRenderer>().sharedMaterial=m;return g;
        }
        static void BeveledTop(Transform p,Material wood)
        {
            // Four inset rings give the thick top chamfered edges and corners.
            var v=new List<Vector3>();var uv=new List<Vector2>();var f=new List<int>();
            for(int ring=0;ring<4;ring++)
            {
                float inset=ring==0||ring==3?.012f:0;
                float x=.70f-inset,z=.325f-inset,y=new[]{.83f,.842f,.928f,.94f}[ring],c=.025f;
                foreach(var q in new[]{new Vector2(-x+c,-z),new Vector2(x-c,-z),new Vector2(x,-z+c),new Vector2(x,z-c),
                    new Vector2(x-c,z),new Vector2(-x+c,z),new Vector2(-x,z-c),new Vector2(-x,-z+c)})
                {v.Add(new Vector3(q.x,y,q.y));uv.Add(new Vector2(q.x/1.15f,q.y/1.15f));}
            }
            for(int r=0;r<3;r++)for(int i=0;i<8;i++)
            {int a=r*8+i,b=r*8+(i+1)%8;f.AddRange(new[]{a,a+8,b,b,a+8,b+8});}
            for(int i=1;i<7;i++)f.AddRange(new[]{0,i,i+1,24,24+i+1,24+i});
            var top=MeshObject(p,"WoodworkTop",v,uv,f,wood);
            var collider=top.AddComponent<BoxCollider>();collider.center=new Vector3(0,.885f,0);collider.size=new Vector3(1.4f,.11f,.65f);
        }
        static void HandSaw(Transform p,Material metal,Material wood,Material dark)
        {
            var g=new GameObject("HangingHandSaw");g.transform.SetParent(p,false);
            g.transform.localPosition=new Vector3(.01f,1.31f,.275f);g.transform.localRotation=Quaternion.Euler(0,0,-8);
            var v=new List<Vector3>();var uv=new List<Vector2>();var f=new List<int>();
            // A tapered plate plus real triangular teeth, visible from either side.
            for(int side=0;side<2;side++)
            {
                int o=v.Count;float z=(side-.5f)*.007f;
                foreach(var q in new[]{new Vector2(-.18f,.065f),new Vector2(.24f,.035f),new Vector2(.24f,-.035f),new Vector2(-.18f,-.035f)})
                {v.Add(new Vector3(q.x,q.y,z));uv.Add(q);}
                if(side==0)f.AddRange(new[]{o,o+1,o+2,o,o+2,o+3});else f.AddRange(new[]{o,o+2,o+1,o,o+3,o+2});
                for(int i=0;i<21;i++)
                {
                    int n=v.Count;float x=-.18f+i*.02f;
                    v.Add(new Vector3(x,-.035f,z));v.Add(new Vector3(x+.016f,-.052f,z));v.Add(new Vector3(x+.02f,-.035f,z));
                    uv.Add(Vector2.zero);uv.Add(Vector2.right);uv.Add(Vector2.up);
                    f.AddRange(side==0?new[]{n,n+2,n+1}:new[]{n,n+1,n+2});
                }
            }
            MeshObject(g.transform,"HandSawBlade",v,uv,f,metal);
            // Open handle, with a genuine gap between the four wooden rails.
            Box(g.transform,"SawGripBack",new Vector3(-.28f,0,0),new Vector3(.025f,.15f,.035f),wood);
            Box(g.transform,"SawGripFront",new Vector3(-.19f,0,0),new Vector3(.025f,.15f,.035f),wood);
            foreach(float y in new[]{-.065f,.065f})Box(g.transform,"SawGripRail",new Vector3(-.235f,y,0),new Vector3(.11f,.022f,.035f),wood);
            foreach(float y in new[]{-.035f,.035f})Rod(g.transform,"SawHandleRivet",new Vector3(-.19f,y,-.023f),new Vector3(-.19f,y,.023f),.006f,dark);
        }
        static void Shaving(Transform p,Vector3 at,float radius,float turn,Material wood)
        {
            var v=new List<Vector3>();var uv=new List<Vector2>();var faces=new List<int>();
            const int segments=16;
            for(int i=0;i<=segments;i++)
            {
                float a=i*5.2f/segments, r=radius*(1-.55f*i/segments);
                for(int edge=0;edge<2;edge++)
                {v.Add(new Vector3(Mathf.Cos(a)*r,Mathf.Sin(a)*r,(edge-.5f)*.009f));uv.Add(new Vector2(i/(float)segments,edge));}
                if(i<segments){int n=i*2;faces.AddRange(new[]{n,n+2,n+1,n+1,n+2,n+3,n,n+1,n+2,n+1,n+3,n+2});}
            }
            var g=MeshObject(p,"CurledShaving",v,uv,faces,wood);g.transform.localPosition=at;g.transform.localRotation=Quaternion.Euler(0,turn,0);
        }
        static void Batch(Transform group,string name)
        {
            // Merge only these static props by material; leave the optional packer independent.
            var renderers=group.GetComponentsInChildren<MeshRenderer>(true);
            if(renderers.Length==0)throw new InvalidOperationException("Woodworking batch has no source renderers");
            var batches=new Dictionary<Material,List<CombineInstance>>();
            foreach(var r in renderers)
            {
                var filter=r.GetComponent<MeshFilter>();var material=r.sharedMaterial;
                List<CombineInstance> list;if(!batches.TryGetValue(material,out list))batches[material]=list=new List<CombineInstance>();
                list.Add(new CombineInstance{mesh=filter.sharedMesh,transform=group.worldToLocalMatrix*r.transform.localToWorldMatrix});
            }
            foreach(var pair in batches)
            {
                var mesh=ForestryResources.Own(new Mesh{name=name,indexFormat=IndexFormat.UInt32});mesh.CombineMeshes(pair.Value.ToArray(),true,true);
                var g=new GameObject(name);g.transform.SetParent(group,false);g.AddComponent<MeshFilter>().sharedMesh=mesh;
                g.AddComponent<MeshRenderer>().sharedMaterial=pair.Key;
            }
            foreach(var r in renderers)
            {
                var filter=r.GetComponent<MeshFilter>();var mesh=filter.sharedMesh;
                bool ownedMesh=mesh.name.StartsWith("Woodwork_",StringComparison.Ordinal);
                UnityEngine.Object.DestroyImmediate(r);UnityEngine.Object.DestroyImmediate(filter);
                if(ownedMesh)UnityEngine.Object.DestroyImmediate(mesh);
                // Transform and any collider remain: render LOD never removes physical support.
            }
        }
        public static void Build(Transform root,Material wood,Material steel,Material metal,Material dark)
        {
            var corner=new GameObject("WoodworkingCorner");corner.transform.SetParent(root,false);
            corner.transform.localPosition=new Vector3(1.25f,0,-2.13f);
            var coarse=new GameObject("WoodworkFrame");coarse.transform.SetParent(corner.transform,false);var p=coarse.transform;
            var details=new GameObject("WoodworkDetails");details.transform.SetParent(corner.transform,false);var d=details.transform;
            var fresh=ForestryResources.Own(new Material(wood){name="ForestryPlanedWood",color=new Color(1f,.87f,.64f)});
            var pencil=ForestryResources.Own(new Material(dark){name="ForestryPencilMark",color=new Color(.10f,.075f,.05f)});
            BeveledTop(p,wood);
            foreach(float x in new[]{-.56f,.56f})foreach(float z in new[]{-.24f,.24f})
                Box(p,"WoodworkLeg",new Vector3(x,.46f,z),new Vector3(.09f,.74f,.09f),wood,true);
            Box(p,"WoodworkShelf",new Vector3(0,.285f,0),new Vector3(1.23f,.065f,.53f),wood);
            foreach(float z in new[]{-.24f,.24f})Box(p,"WoodworkStretcher",new Vector3(0,.21f,z),new Vector3(1.20f,.075f,.055f),wood);
            foreach(float x in new[]{-.59f,.59f})Box(p,"RackPost",new Vector3(x,1.19f,.285f),new Vector3(.04f,.67f,.04f),wood);
            foreach(float y in new[]{1.12f,1.45f})Box(p,"ToolRackRail",new Vector3(0,y,.285f),new Vector3(1.22f,.065f,.055f),wood);
            // Working board, visibly rough on one side and planed on the other.
            Box(d,"RoughBoard",new Vector3(-.07f,.965f,-.055f),new Vector3(.83f,.05f,.19f),wood);
            Box(d,"PlanedBoardFace",new Vector3(-.23f,.991f,-.055f),new Vector3(.49f,.004f,.18f),fresh);
            for(int i=0;i<5;i++)Box(d,"RoughEndRidge",new Vector3(.25f,.993f,-.12f+i*.031f),new Vector3(.14f,.006f,.003f),dark);
            // Front vise: fixed/moving wooden jaws, guide bars, screw and sliding handle.
            foreach(float z in new[]{-.30f,-.43f})Box(d,"ViseJaw",new Vector3(-.35f,.84f,z),new Vector3(.30f,.16f,.055f),wood);
            foreach(float x in new[]{-.45f,-.25f})Rod(d,"ViseGuide",new Vector3(x,.81f,-.26f),new Vector3(x,.81f,-.48f),.012f,metal);
            Rod(d,"ViseScrew",new Vector3(-.35f,.83f,-.27f),new Vector3(-.35f,.83f,-.52f),.016f,steel);
            for(int i=0;i<7;i++)Rod(d,"ScrewThread",new Vector3(-.35f,.83f,-.465f-i*.007f),new Vector3(-.35f,.83f,-.468f-i*.007f),.021f,metal);
            Rod(d,"ViseHandle",new Vector3(-.45f,.83f,-.525f),new Vector3(-.25f,.83f,-.525f),.009f,wood);
            Box(d,"ViseHeldBoard",new Vector3(-.35f,.93f,-.365f),new Vector3(.19f,.31f,.07f),fresh);
            foreach(float x in new[]{-.43f,.30f})
            {
                Rod(d,"ClampBar",new Vector3(x,.83f,.06f),new Vector3(x,1.065f,.06f),.008f,metal);
                Box(d,"ClampLowerJaw",new Vector3(x,.819f,.005f),new Vector3(.028f,.022f,.13f),steel);
                Box(d,"ClampArm",new Vector3(x,1.045f,.005f),new Vector3(.028f,.025f,.13f),steel);
                Rod(d,"ClampScrew",new Vector3(x,.995f,-.045f),new Vector3(x,1.09f,-.045f),.005f,metal);
                Rod(d,"ClampHandle",new Vector3(x-.025f,1.08f,-.045f),new Vector3(x+.025f,1.08f,-.045f),.008f,wood);
            }
            // Wooden hand plane: sole, split body/mouth, angled iron, front knob and rear grip.
            Box(d,"PlaneSole",new Vector3(-.10f,1.004f,-.055f),new Vector3(.27f,.015f,.075f),metal);
            Box(d,"PlaneFront",new Vector3(-.19f,1.037f,-.055f),new Vector3(.09f,.05f,.075f),fresh);
            Box(d,"PlaneRear",new Vector3(-.045f,1.037f,-.055f),new Vector3(.13f,.05f,.075f),fresh);
            var iron=Box(d,"PlaneIron",new Vector3(-.122f,1.065f,-.055f),new Vector3(.012f,.09f,.055f),metal);iron.transform.localRotation=Quaternion.Euler(0,0,-27);
            Rod(d,"PlaneKnob",new Vector3(-.19f,1.06f,-.055f),new Vector3(-.19f,1.095f,-.055f),.018f,wood);
            Rod(d,"PlaneGrip",new Vector3(-.025f,1.06f,-.055f),new Vector3(.0f,1.13f,-.055f),.017f,wood);
            HandSaw(d,metal,wood,dark);
            for(int i=0;i<3;i++)
            {
                float x=-.49f+i*.09f;
                Rod(d,"ChiselHandle",new Vector3(x,1.30f,.235f),new Vector3(x,1.43f,.235f),.015f,wood);
                Box(d,"ChiselBlade",new Vector3(x,1.20f,.235f),new Vector3(.012f+i*.007f,.14f,.004f),metal);
                Rod(d,"ChiselFerrule",new Vector3(x,1.295f,.235f),new Vector3(x,1.31f,.235f),.016f,metal);
                Rod(d,"ToolPeg",new Vector3(x,1.43f,.29f),new Vector3(x,1.43f,.20f),.005f,metal);
            }
            Rod(d,"SawHook",new Vector3(-.225f,1.44f,.29f),new Vector3(-.225f,1.44f,.245f),.005f,metal);
            Rod(d,"SawHookDrop",new Vector3(-.225f,1.44f,.245f),new Vector3(-.225f,1.375f,.245f),.005f,metal);
            Rod(d,"SawHookTip",new Vector3(-.225f,1.375f,.245f),new Vector3(-.225f,1.375f,.285f),.005f,metal);
            Rod(d,"MalletHandle",new Vector3(.43f,1.11f,.20f),new Vector3(.43f,1.40f,.20f),.017f,wood);
            Box(d,"MalletHead",new Vector3(.43f,1.39f,.20f),new Vector3(.19f,.075f,.075f),fresh);
            foreach(float x in new[]{.395f,.465f})
            {
                Rod(d,"MalletHookStem",new Vector3(x,1.44f,.29f),new Vector3(x,1.355f,.29f),.006f,metal);
                Rod(d,"MalletPeg",new Vector3(x,1.355f,.29f),new Vector3(x,1.355f,.17f),.006f,metal);
            }
            Box(d,"TrySquareStock",new Vector3(.49f,.955f,-.15f),new Vector3(.035f,.028f,.19f),wood);
            Box(d,"TrySquareBlade",new Vector3(.385f,.95f,-.23f),new Vector3(.23f,.008f,.023f),metal);
            for(int i=0;i<9;i++)Box(d,"SquareGraduation",new Vector3(.285f+i*.023f,.955f,-.23f),new Vector3(.002f,.001f,i%2==0?.017f:.009f),pencil);
            Box(d,"BoardPencilLine",new Vector3(.18f,.992f,-.055f),new Vector3(.002f,.001f,.17f),pencil);
            Rod(d,"CarpenterPencil",new Vector3(.43f,.949f,.07f),new Vector3(.57f,.949f,.10f),.004f,pencil);
            for(int i=0;i<7;i++)
            {
                var scratch=Box(d,"BenchWear",new Vector3(-.55f+i*.15f,.941f,-.27f),new Vector3(.035f+(i%3)*.018f,.001f,.0015f),dark);
                scratch.transform.localRotation=Quaternion.Euler(0,i*17-40,0);
            }
            for(int i=0;i<4;i++)Box(d,"ShelfOffcut",new Vector3(-.42f,.334f+i*.035f,.01f),new Vector3(.29f+(i%2)*.07f,.03f,.18f),wood);
            // Small open offcut bin sits between bench and output pallet, clear of the front.
            Box(p,"OffcutBinBase",new Vector3(.88f,.13f,.16f),new Vector3(.30f,.045f,.29f),wood);
            foreach(float x in new[]{.735f,1.025f})Box(p,"OffcutBinSide",new Vector3(x,.26f,.16f),new Vector3(.025f,.26f,.29f),wood);
            foreach(float z in new[]{.025f,.295f})Box(p,"OffcutBinSide",new Vector3(.88f,.26f,z),new Vector3(.29f,.26f,.025f),wood);
            for(int i=0;i<4;i++)
            {
                var offcut=Box(d,"BinOffcut",new Vector3(.79f+i*.055f,.31f,.16f),new Vector3(.035f,.29f,.06f),fresh);
                offcut.transform.localRotation=Quaternion.Euler(i*6,0,i*9-15);
            }
            for(int i=0;i<5;i++)Shaving(d,new Vector3(-.38f+i*.037f,1.015f,-.16f),.017f,25+i*37,fresh);
            for(int i=0;i<7;i++)Shaving(d,new Vector3(-.48f+i*.09f,.113f,-.40f+(i%3)*.035f),.019f,17+i*53,fresh);
            Batch(p,"WoodworkCoarse");Batch(d,"WoodworkFine");
        }
    }
}
