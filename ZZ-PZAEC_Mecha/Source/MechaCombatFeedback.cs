using System;
using System.Collections.Generic;
using UnityEngine;
namespace PZAEC.Mecha
{
    public static class CombatFeedback
    {
        public const byte BeamImpact=17,SwordContact=18;
        sealed class Impact{public float Until;public Vector3 Point,Normal;public byte Kind;public int Vehicle;}
        sealed class Trail{public int Vehicle=-1;public LineRenderer Root,Tip;public int Count;public Vector3[] A=new Vector3[12],B=new Vector3[12];public float At;}
        public sealed class Result{public float At=-100,Damage;public bool Blocked,Heavy;public byte Kind;public Vector3 Point;}
        static readonly Impact[] impacts=new Impact[16];static readonly Trail[] trails=new Trail[4];
        static readonly Dictionary<int,Result> results=new Dictionary<int,Result>();struct EventKey:IEquatable<EventKey>{public int Vehicle,Serial;public byte Kind;public bool Equals(EventKey other){return Vehicle==other.Vehicle&&Serial==other.Serial&&Kind==other.Kind;}public override bool Equals(object o){return o is EventKey&&Equals((EventKey)o);}public override int GetHashCode(){return unchecked((Vehicle*397^Serial)*397^Kind);}}
        static readonly HashSet<EventKey> seen=new HashSet<EventKey>();static readonly Queue<EventKey> order=new Queue<EventKey>();static Material material;
        static GameObject impactRoot;static Mesh impactMesh;
        static readonly List<Vector3> impactVertices=new List<Vector3>(16*19*4);
        static readonly List<Color> impactColors=new List<Color>(16*19*4);
        static readonly List<int> impactIndices=new List<int>(16*19*6);
        public static Result Get(int id){Result r;if(!results.TryGetValue(id,out r)){r=new Result();results[id]=r;}return r;}
        public static bool Fresh(int id){return Time.time-Get(id).At<.25f;}
        public static float Kick(int id){var r=Get(id);return Mathf.Clamp01(1-(Time.time-r.At)/.12f)*Mathf.Sin((Time.time-r.At)*60)*(r.Heavy?1:.5f);}
        static LineRenderer Line(string name){if(material==null)material=new Material(Shader.Find("Sprites/Default"));var l=new GameObject(name).AddComponent<LineRenderer>();l.sharedMaterial=material;l.useWorldSpace=true;l.startWidth=.045f;l.endWidth=.01f;l.shadowCastingMode=UnityEngine.Rendering.ShadowCastingMode.Off;l.receiveShadows=false;return l;}
        static void ImpactBatch()
        {
            if(impactRoot!=null)return;if(material==null)material=new Material(Shader.Find("Sprites/Default"));
            impactRoot=new GameObject("MechaContactSparkPool");impactRoot.hideFlags=HideFlags.DontSave;impactMesh=new Mesh();impactMesh.name="Mecha contact batch";impactMesh.MarkDynamic();impactRoot.AddComponent<MeshFilter>().sharedMesh=impactMesh;var renderer=impactRoot.AddComponent<MeshRenderer>();renderer.sharedMaterial=material;renderer.shadowCastingMode=UnityEngine.Rendering.ShadowCastingMode.Off;renderer.receiveShadows=false;
        }
        static void Stroke(Vector3 a,Vector3 b,Vector3 normal,Vector3 side,Color color,float width)
        {
            var across=Vector3.Cross(normal,b-a).normalized;if(across.sqrMagnitude<.01f)across=side;across*=width*.5f;int start=impactVertices.Count;
            impactVertices.Add(a-across);impactVertices.Add(a+across);impactVertices.Add(b-across);impactVertices.Add(b+across);for(int i=0;i<4;i++)impactColors.Add(color);
            impactIndices.Add(start);impactIndices.Add(start+1);impactIndices.Add(start+2);impactIndices.Add(start+2);impactIndices.Add(start+1);impactIndices.Add(start+3);
        }
        public static void Receive(World world,int vehicle,int serial,byte kind,Vector3 point,Vector3 normal,float damage,float surface)
        {
            if(kind!=BeamImpact&&kind!=SwordContact||!Weapons.Finite(point.x)||!Weapons.Finite(point.y)||!Weapons.Finite(point.z)||!Weapons.Finite(normal.x)||!Weapons.Finite(normal.y)||!Weapons.Finite(normal.z)||!Weapons.Finite(damage)||!Weapons.Finite(surface))return;
            var key=new EventKey{Vehicle=vehicle,Serial=serial,Kind=kind};if(!seen.Add(key))return;order.Enqueue(key);while(order.Count>512)seen.Remove(order.Dequeue());
            var v=world!=null?world.GetEntity(vehicle) as EntityVehicle:null;if(kind==SwordContact&&surface<0&&v!=null&&!Weapons.Server)Samurai.Interrupt(v,Time.time);
            // Sword stamp: integer attack sequence, half-unit for a heavy cut;
            // negative denotes environment blocking. Damage may be mitigated
            // or limited by remaining health, so it cannot identify cut type.
            var r=Get(vehicle);r.At=Time.time;r.Damage=damage;r.Blocked=surface<0;r.Heavy=kind==SwordContact&&Mathf.Abs(surface)%1>=.49f;r.Kind=kind;r.Point=point;
            int index=0;float oldest=float.MaxValue;for(int i=0;i<impacts.Length;i++){if(impacts[i]==null||impacts[i].Until<Time.time){index=i;break;}if(impacts[i].Until<oldest){oldest=impacts[i].Until;index=i;}}
            var p=impacts[index];if(p==null){p=new Impact();impacts[index]=p;}
            p.Point=point;p.Normal=normal.sqrMagnitude>.001f?normal.normalized:Vector3.up;p.Kind=kind;p.Vehicle=vehicle;p.Until=Time.time+(kind==BeamImpact?.28f:.18f);
            if(v!=null)RobotAudio.ContactEvent(v,kind==BeamImpact?"laser-impact":"sword-impact",serial,point,damage>0?.4f:.25f);
        }
        public static void Blade(EntityVehicle v,Model.Rig r)
        {
            if(!Rules.Complete(v))return;var s=Samurai.Get(v);float phase=(Time.time-s.Started)/Samurai.Duration(s);bool active=s.Swing&&!s.Blocked&&SwordMotion.DamagePhase(phase);
            Trail t=null;for(int i=0;i<trails.Length;i++)if(trails[i]!=null&&trails[i].Vehicle==v.entityId){t=trails[i];break;}
            if(!active){if(t!=null){t.Root.gameObject.SetActive(false);t.Tip.gameObject.SetActive(false);t.Count=0;}return;}
            if(t==null){int index=0;float oldest=float.MaxValue;for(int i=0;i<trails.Length;i++){if(trails[i]==null){index=i;break;}if(trails[i].At<oldest){oldest=trails[i].At;index=i;}}t=trails[index];if(t==null){t=new Trail{Root=Line("MechaSwordRootTrail"),Tip=Line("MechaSwordTipTrail")};trails[index]=t;}t.Vehicle=v.entityId;t.Count=0;}
            t.At=Time.time;t.Root.gameObject.SetActive(true);t.Tip.gameObject.SetActive(true);int count=Mathf.Min(t.Count+1,12);for(int i=count-1;i>0;i--){t.A[i]=t.A[i-1];t.B[i]=t.B[i-1];}
            t.A[0]=SwordMotion.Root(r)+Origin.position;t.B[0]=SwordMotion.Tip(r)+Origin.position;t.Count=count;
            t.Root.positionCount=t.Tip.positionCount=count;for(int i=0;i<count;i++){t.Root.SetPosition(i,t.A[i]-Origin.position);t.Tip.SetPosition(i,t.B[i]-Origin.position);}t.Root.startColor=t.Tip.startColor=new Color(.6f,.9f,1,.6f);t.Root.endColor=t.Tip.endColor=new Color(.4f,.7f,1,0);
        }
        public static void Update(World world)
        {
            foreach(var t in trails)if(t!=null&&(world.GetEntity(t.Vehicle)==null||Time.time-t.At>.2f)){t.Root.gameObject.SetActive(false);t.Tip.gameObject.SetActive(false);t.Count=0;}
            impactVertices.Clear();impactColors.Clear();impactIndices.Clear();
            foreach(var p in impacts){if(p==null||Time.time>p.Until)continue;var n=p.Normal;var side=Vector3.Cross(n,Mathf.Abs(n.y)>.9f?Vector3.forward:Vector3.up).normalized;var up=Vector3.Cross(n,side);float age=p.Until-Time.time,radius=p.Kind==BeamImpact?.14f:.23f;var origin=p.Point-Origin.position+n*.025f;
                var color=p.Kind==BeamImpact?new Color(.65f,.98f,1,Mathf.Clamp01(age/.2f)):new Color(1,.8f,.45f,Mathf.Clamp01(age/.15f));
                for(int i=0;i<8;i++){float a=i*Mathf.PI*.25f;var d=side*Mathf.Cos(a)+up*Mathf.Sin(a);Stroke(origin,origin+d*radius+n*(i%2==0?.08f:0),n,side,color,.018f);}
                if(p.Kind==BeamImpact){var previous=origin+side*.04f;for(int i=1;i<=8;i++){float a=i*Mathf.PI*.25f;var point=origin+(side*Mathf.Cos(a)+up*Mathf.Sin(a))*.04f;Stroke(previous,point,n,side,color,.012f);previous=point;}previous=origin+side*.025f;for(int i=1;i<4;i++){var point=origin+n*(i*.04f)+side*((i%2==0?1:-1)*.025f);Stroke(previous,point,n,side,color,.012f);previous=point;}}
            }
            if(impactVertices.Count>0){ImpactBatch();impactRoot.SetActive(true);impactMesh.Clear();impactMesh.SetVertices(impactVertices);impactMesh.SetColors(impactColors);impactMesh.SetTriangles(impactIndices,0);impactMesh.RecalculateBounds();}else if(impactRoot!=null)impactRoot.SetActive(false);
        }
        static void Release(GameObject go){if(go!=null){go.SetActive(false);UnityEngine.Object.Destroy(go);}}
        public static void Clear(){Release(impactRoot);if(impactMesh!=null)UnityEngine.Object.Destroy(impactMesh);impactRoot=null;impactMesh=null;impactVertices.Clear();impactColors.Clear();impactIndices.Clear();foreach(var t in trails)if(t!=null){Release(t.Root.gameObject);Release(t.Tip.gameObject);}System.Array.Clear(impacts,0,16);System.Array.Clear(trails,0,4);seen.Clear();order.Clear();results.Clear();if(material!=null)UnityEngine.Object.Destroy(material);material=null;}
    }
}
