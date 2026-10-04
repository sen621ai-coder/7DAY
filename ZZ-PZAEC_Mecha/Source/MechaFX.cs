using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace PZAEC.Mecha
{
    // Client-side beam tracers, missile bodies and impact effects fed by the
    // authoritative event stream. Purely cosmetic; no damage or state here.
    public static class MechaFX
    {
        sealed class Projectile
        {
            public GameObject Object; public Vector3 Position, Velocity; public float Life;
        }
        sealed class Tracer
        {
            public LineRenderer Line,Glow;public Vector3 A,B;public float Age,Life;
            public int BornFrame,PresentedFrames,LastPresentedFrame=-1;
            public void Presented(int frame){if(frame==LastPresentedFrame)return;LastPresentedFrame=frame;PresentedFrames++;}
        }
        sealed class Landing
        {
            public GameObject Root;public LineRenderer Ring;public Mesh Dust;
            public Vector3 Point,Normal;public float Age,Radius,Strength;public int BornFrame;public bool Active;
            public readonly Vector3[] Vertices=new Vector3[40];public readonly Color[] Colors=new Color[40];
        }
        static readonly Landing[] landings=new Landing[8];
        static readonly HashSet<long> seenLandingSerials=new HashSet<long>();static readonly Queue<long> seenLandingOrder=new Queue<long>();
        static readonly Dictionary<int,float> lastLanding=new Dictionary<int,float>();
        static Material dustMaterial;static Texture2D dustTexture;
        public static bool LandingRecently(int vehicle,float seconds=.8f){float at;return lastLanding.TryGetValue(vehicle,out at)&&Time.time-at<seconds;}
        public sealed class Status
        {
            public float Time = -100, Heat, LockProgress;
            public float HullFraction = 1, RepairProgress, BattleRepairWait;
            public int BeamAmmo, MissileAmmo, Flags; public float MissileWait;
            public bool MeleeMode;
            public Vector3 Direction;
        }
        static readonly Dictionary<int, float> meleeCooldownUntil = new Dictionary<int, float>();
        static readonly Dictionary<string, float> soundGate = new Dictionary<string, float>();
        // Beam events dedup by (vehicle, serial): replays can never double the
        // tracer or the sound, whatever the transport does.
        static readonly HashSet<long> seenBeamSerials = new HashSet<long>();
        static readonly Queue<long> seenBeamOrder = new Queue<long>();

        // Same-name sounds cannot retrigger faster than this; a stuck event
        // stream must not turn into a continuous buzz.
        public static void Play(EntityVehicle vehicle, string name)
        {
            float last;
            var key=vehicle.entityId+"/"+name;
            if (soundGate.TryGetValue(key, out last) && Time.time - last < .12f) return;
            soundGate[key] = Time.time;
            Audio.Manager.Play(vehicle, name, 1, false);
        }

        public static float MeleeCooldownRemaining(int vehicleId)
        {
            float until;
            return meleeCooldownUntil.TryGetValue(vehicleId, out until) ? Mathf.Max(0, until - Time.time) : 0;
        }
        static readonly Dictionary<int, Projectile> projectiles = new Dictionary<int, Projectile>();
        static readonly Dictionary<int, Status> statuses = new Dictionary<int, Status>();
        static readonly List<Tracer> tracers = new List<Tracer>();
        static readonly Stack<Tracer> pool = new Stack<Tracer>();
        static readonly List<int> remove = new List<int>();
        static Material beamMaterial, bodyMaterial;static bool missingBeamShader;

        static Material Material(bool beam)
        {
            var current = beam ? beamMaterial : bodyMaterial;
            if (current != null) return current;
            Shader shader=null;
            foreach(var name in beam?new[]{"Sprites/Default","Legacy Shaders/Particles/Additive","Hidden/Internal-Colored"}:new[]{"Standard","Unlit/Color"})
            {var candidate=Shader.Find(name);if(candidate!=null&&candidate.isSupported){shader=candidate;break;}}
            if (shader == null)
            {if(beam&&!missingBeamShader){missingBeamShader=true;Log.Warning("[Mecha] No supported vertex-color beam shader; beam presentation unavailable.");}return null;}
            var material = new Material(shader) { name=beam?"Mecha beam vertex color":"Mecha missile body",color = beam ? Color.white : new Color(.2f, .24f, .28f) };
            if(beam)
            {
                if(material.HasProperty("_MainTex"))material.SetTexture("_MainTex",Texture2D.whiteTexture);
                if(material.HasProperty("_SrcBlend"))material.SetInt("_SrcBlend",(int)BlendMode.SrcAlpha);
                if(material.HasProperty("_DstBlend"))material.SetInt("_DstBlend",(int)BlendMode.OneMinusSrcAlpha);
                if(material.HasProperty("_Cull"))material.SetInt("_Cull",(int)CullMode.Off);
                if(material.HasProperty("_ZWrite"))material.SetInt("_ZWrite",0);
                if(material.HasProperty("_ZTest"))material.SetInt("_ZTest",(int)CompareFunction.LessEqual);
                material.renderQueue=3000;
            }
            if (!beam && material.HasProperty("_Metallic")) material.SetFloat("_Metallic", .6f);
            if (beam) beamMaterial = material; else bodyMaterial = material;
            return material;
        }

        public static Status GetStatus(int vehicleId)
        {
            Status status;
            if (!statuses.TryGetValue(vehicleId, out status)) { status = new Status(); statuses[vehicleId] = status; }
            return status;
        }

        public static void Receive(World world, int vehicleId, int id, byte kind, Vector3 a, Vector3 b, float value, float c)
        {
            if (world == null || world.GetPrimaryPlayer() == null) return;
            if(kind==CombatFeedback.BeamImpact||kind==CombatFeedback.SwordContact){CombatFeedback.Receive(world,vehicleId,id,kind,a,b,value,c);return;}
            if(kind==Samurai.CancelAck){var v=world.GetEntity(vehicleId) as EntityVehicle;if(v!=null)Samurai.ReceiveCancelAck(v,id,a);return;}
            if(kind==Samurai.Snapshot){var v=world.GetEntity(vehicleId) as EntityVehicle;if(v!=null)Samurai.Receive(v,id,a,b,value,c);return;}
            if(kind==9) { var v=world.GetEntity(vehicleId) as EntityVehicle; if(v!=null && v.isEntityRemote) Locomotion.Receive(v,(int)value,id,a); return; }
            if(kind==Boarding.BoardEvent) { Boarding.ReceiveSnapshot(world,vehicleId,id,a,b,value,c); return; }
            if(kind==Weapons.LandingEvent){LandingContact(world,vehicleId,id,a,b,value,c);return;}
            if (kind == Weapons.StatusEvent)
            {
                var status = GetStatus(vehicleId);
                status.Time = Time.time; status.Flags = id; status.Heat = b.x;
                status.HullFraction = Mathf.Clamp01(b.y);
                status.RepairProgress = Mathf.Clamp01(b.z);
                status.BattleRepairWait = Mathf.Max(0, c);
                status.BeamAmmo = Mathf.Max(0, Mathf.RoundToInt(a.x));
                status.MissileAmmo = Mathf.Max(0, Mathf.RoundToInt(a.y));
                status.MissileWait = Mathf.Max(0, a.z);
                status.MeleeMode = false;
                status.Direction = Vector3.zero;
                status.LockProgress = Mathf.Clamp01(value);
                return;
            }
            if (kind == Weapons.MeleeModeEvent || kind == Weapons.MeleeSweepEvent || kind == Weapons.MeleeHeavyEvent) return; // Retired protocol events.
            if (kind == Weapons.MeleeModeEvent)
            {
                // id carries the new mode; animate the blade deploy/stow.
                SetBladeMode(world, vehicleId, id != 0);
                var vehicle = world.GetEntity(vehicleId) as EntityVehicle;
                if (vehicle != null) Play(vehicle, id != 0 ? "electric_fence_on" : "electric_fence_off");
                return;
            }
            if (kind == Weapons.MeleeSweepEvent || kind == Weapons.MeleeHeavyEvent)
            {
                meleeCooldownUntil[vehicleId] = Time.time + Mathf.Max(0, c);
                PlaySwing(world, vehicleId, kind == Weapons.MeleeHeavyEvent, a, b);
                var vehicle = world.GetEntity(vehicleId) as EntityVehicle;
                // Retired melee path is retained only for protocol compatibility.
                if (vehicle != null) Play(vehicle, "electric_fence_impact");
                return;
            }
            if (kind == Weapons.BeamEvent)
            {
                BeamTrace(world,vehicleId,id,a,b);
                return;
            }
            if (kind == Weapons.MissileSpawnEvent)
            {
                if (projectiles.ContainsKey(id)) return;
                var obj = GameObject.CreatePrimitive(PrimitiveType.Capsule);
                obj.name = "MechaMissile"; obj.hideFlags = HideFlags.DontSave;
                var collider = obj.GetComponent<Collider>();
                if (collider != null) { collider.enabled = false; UnityEngine.Object.Destroy(collider); }
                var renderer = obj.GetComponent<Renderer>();
                renderer.sharedMaterial = Material(false);
                renderer.shadowCastingMode = ShadowCastingMode.Off;
                obj.transform.localScale = new Vector3(.09f, .38f, .09f);
                obj.transform.position = a - Origin.position;
                obj.transform.rotation = Quaternion.FromToRotation(Vector3.up, b.normalized);
                var trail = obj.AddComponent<TrailRenderer>();
                trail.sharedMaterial = Material(true);
                trail.time = .5f; trail.startWidth = .1f; trail.endWidth = .015f;
                trail.startColor = new Color(1f, .45f, .12f); trail.endColor = new Color(.35f, .3f, .25f, 0);
                projectiles[id] = new Projectile { Object = obj, Position = a, Velocity = b, Life = Rules.MissileLifetime + .5f };
                var vehicle = world.GetEntity(vehicleId) as EntityVehicle;
                if (vehicle != null) RobotAudio.Event(vehicle,"missile-release",id,.62f);
                return;
            }
            if (kind == Weapons.MissileMoveEvent)
            {
                Projectile projectile;
                if (projectiles.TryGetValue(id, out projectile)) { projectile.Position = a; projectile.Velocity = b; }
                return;
            }
            if (kind == Weapons.ImpactEvent)
            {
                Projectile projectile;
                if (projectiles.TryGetValue(id, out projectile))
                {
                    if (projectile.Object != null) UnityEngine.Object.Destroy(projectile.Object);
                    projectiles.Remove(id);
                }
            }
        }

        static void BeamTrace(World world,int vehicleId,int id,Vector3 a,Vector3 b)
        {
            long key=(long)vehicleId<<32|(uint)id;if(!seenBeamSerials.Add(key))return;Gait.Recoil(vehicleId);seenBeamOrder.Enqueue(key);while(seenBeamOrder.Count>64)seenBeamSerials.Remove(seenBeamOrder.Dequeue());
            var v=world.GetEntity(vehicleId) as EntityVehicle;if(v!=null)RobotAudio.Event(v,Rules.Complete(v)?"head-laser":"palm-laser",id,Rules.Complete(v)?.55f:.45f);
            var material=Material(true);if(material==null)return;
            var tracer=pool.Count>0?pool.Pop():new Tracer();
            if(tracer.Line==null||tracer.Glow==null)
            {
                if(tracer.Line!=null)Release(tracer.Line.gameObject);
                tracer.Line=BeamLine("MechaBeam",null,material);
                tracer.Glow=BeamLine("MechaBeamGlow",tracer.Line.transform,material);
                tracer.Line.gameObject.AddComponent<MechaBeamPresentation>().Presented=tracer.Presented;
            }
            tracer.Line.gameObject.SetActive(true);tracer.A=a;tracer.B=b;tracer.Age=0;tracer.Life=Rules.Complete(v)?.32f:.22f;
            tracer.BornFrame=Time.frameCount;tracer.PresentedFrames=0;tracer.LastPresentedFrame=-1;
            SetBeam(tracer,1);tracers.Add(tracer);
        }
        static LineRenderer BeamLine(string name,Transform parent,Material material)
        {
            var go=new GameObject(name);go.hideFlags=HideFlags.DontSave;go.layer=0;if(parent!=null)go.transform.SetParent(parent,false);
            var line=go.AddComponent<LineRenderer>();line.sharedMaterial=material;line.positionCount=2;line.useWorldSpace=true;
            line.alignment=LineAlignment.View;line.textureMode=LineTextureMode.Stretch;line.numCapVertices=2;line.numCornerVertices=2;
            line.shadowCastingMode=ShadowCastingMode.Off;line.receiveShadows=false;line.lightProbeUsage=LightProbeUsage.Off;line.reflectionProbeUsage=ReflectionProbeUsage.Off;
            return line;
        }
        static void SetBeam(Tracer tracer,float alpha)
        {
            tracer.Line.startWidth=.055f;tracer.Line.endWidth=.04f;tracer.Line.startColor=new Color(.92f,1,1,alpha);tracer.Line.endColor=new Color(.8f,1,1,alpha*.9f);
            tracer.Glow.startWidth=.18f;tracer.Glow.endWidth=.12f;tracer.Glow.startColor=new Color(.15f,.8f,1,alpha*.35f);tracer.Glow.endColor=new Color(.15f,.8f,1,alpha*.25f);
            var a=tracer.A-Origin.position;var b=tracer.B-Origin.position;
            tracer.Line.SetPosition(0,a);tracer.Line.SetPosition(1,b);tracer.Glow.SetPosition(0,a);tracer.Glow.SetPosition(1,b);
        }
        // Two actual camera presentation frames, with a bounded off-screen lease.
        // This policy is shared with native QA's low-frame-rate probes.
        public static bool BeamExpired(float age,float lifetime,int presentedFrames)
        {return age>=lifetime&&presentedFrames>=2||age>=Mathf.Max(.8f,lifetime*3);}
        public static string BeamDiagnostics()
        {
            string result="active="+tracers.Count+" pooled="+pool.Count+" shader="+(beamMaterial!=null?beamMaterial.shader.name:"not created");
            foreach(var t in tracers)result+=" | age="+t.Age+" life="+t.Life+" bornFrame="+t.BornFrame+" presented="+t.PresentedFrames+" layer="+t.Line.gameObject.layer+" enabled="+t.Line.enabled+" a="+t.A+" b="+t.B;
            return result;
        }

        static void LandingContact(World world,int vehicle,int serial,Vector3 point,Vector3 normal,float strength,float radius)
        {
            if(!Weapons.Finite(point.x)||!Weapons.Finite(point.y)||!Weapons.Finite(point.z)||!Weapons.Finite(normal.x)||!Weapons.Finite(normal.y)||!Weapons.Finite(normal.z)||!Weapons.Finite(strength)||!Weapons.Finite(radius))return;
            var v=world.GetEntity(vehicle) as EntityVehicle;if(!Weapons.IsMecha(v))return;
            long key=(long)vehicle<<32|(uint)serial;if(!seenLandingSerials.Add(key))return;seenLandingOrder.Enqueue(key);while(seenLandingOrder.Count>512)seenLandingSerials.Remove(seenLandingOrder.Dequeue());
            lastLanding[vehicle]=Time.time;RobotAudio.LandCue(v,point,strength,serial);
            var material=Material(true);if(material==null)return;
            int index=0;float oldest=-1;for(int i=0;i<landings.Length;i++){if(landings[i]==null||!landings[i].Active){index=i;break;}if(landings[i].Age>oldest){oldest=landings[i].Age;index=i;}}
            var p=landings[index];if(p==null)
            {
                p=new Landing();landings[index]=p;p.Ring=BeamLine("MechaLandingMechanicalWave",null,material);p.Root=p.Ring.gameObject;p.Ring.positionCount=33;p.Dust=new Mesh{name="Mecha landing dust batch"};p.Dust.MarkDynamic();
                var dust=new GameObject("MechaGroundDust");dust.hideFlags=HideFlags.DontSave;dust.transform.SetParent(p.Root.transform,false);dust.AddComponent<MeshFilter>().sharedMesh=p.Dust;
                var renderer=dust.AddComponent<MeshRenderer>();renderer.sharedMaterial=DustMaterial();renderer.shadowCastingMode=ShadowCastingMode.Off;renderer.receiveShadows=false;
                var uv=new Vector2[40];var triangles=new int[60];for(int i=0;i<10;i++){int q=i*4,t=i*6;uv[q]=Vector2.zero;uv[q+1]=Vector2.right;uv[q+2]=Vector2.up;uv[q+3]=Vector2.one;triangles[t]=q;triangles[t+1]=q+2;triangles[t+2]=q+1;triangles[t+3]=q+1;triangles[t+4]=q+2;triangles[t+5]=q+3;}p.Dust.vertices=p.Vertices;p.Dust.uv=uv;p.Dust.triangles=triangles;
            }
            p.Point=point;p.Normal=normal.sqrMagnitude>.001f?normal.normalized:Vector3.up;p.Strength=Mathf.Clamp01(strength);p.Radius=Mathf.Clamp(radius*Mathf.Lerp(.2f,1,p.Strength),.5f,8);p.Age=0;p.BornFrame=Time.frameCount;p.Active=true;p.Root.SetActive(true);SetLanding(p);
        }
        static Material DustMaterial()
        {
            if(dustMaterial!=null)return dustMaterial;
            dustTexture=new Texture2D(32,32,TextureFormat.RGBA32,false);dustTexture.name="Mecha gray ground dust mask";dustTexture.wrapMode=TextureWrapMode.Clamp;
            var pixels=new Color[32*32];for(int y=0;y<32;y++)for(int x=0;x<32;x++){float dx=(x-15.5f)/15.5f,dy=(y-15.5f)/15.5f,r=dx*dx+dy*dy;pixels[y*32+x]=new Color(1,1,1,Mathf.Pow(Mathf.Clamp01(1-r),2));}dustTexture.SetPixels(pixels);dustTexture.Apply();
            dustMaterial=new Material(Material(true));dustMaterial.name="Mecha gray ground dust";dustMaterial.SetTexture("_MainTex",dustTexture);return dustMaterial;
        }
        static void SetLanding(Landing p)
        {
            float t=Mathf.Clamp01(p.Age/.72f),fade=(1-t)*(1-t);var side=Vector3.Cross(p.Normal,Mathf.Abs(p.Normal.y)>.9f?Vector3.forward:Vector3.up).normalized;var across=Vector3.Cross(p.Normal,side);
            var origin=p.Point-Origin.position+p.Normal*.045f;float radius=Mathf.Lerp(.25f,p.Radius,1-Mathf.Pow(1-t,2));
            p.Ring.startWidth=p.Ring.endWidth=Mathf.Lerp(.09f,.025f,t);p.Ring.startColor=p.Ring.endColor=new Color(.43f,.41f,.38f,fade*.55f*Mathf.Lerp(.45f,1,p.Strength));
            for(int i=0;i<=32;i++){float a=i*Mathf.PI/16;p.Ring.SetPosition(i,origin+(side*Mathf.Cos(a)+across*Mathf.Sin(a))*radius);}
            var camera=Camera.main;var right=camera!=null?camera.transform.right:side;var up=camera!=null?camera.transform.up:Vector3.up;
            for(int i=0;i<10;i++)
            {
                float angle=(i+.25f)*Mathf.PI*.2f;var radial=side*Mathf.Cos(angle)+across*Mathf.Sin(angle);
                var center=origin+radial*Mathf.Lerp(.25f,p.Radius*.55f,t)+p.Normal*(.08f+t*(.35f+(i%3)*.1f));float size=(.20f+t*.50f)*(.7f+p.Strength*.3f);var r=right*size;var u=up*size;
                int q=i*4;p.Vertices[q]=center-r-u;p.Vertices[q+1]=center+r-u;p.Vertices[q+2]=center-r+u;p.Vertices[q+3]=center+r+u;
                var color=new Color(.42f,.40f,.37f,fade*.46f*Mathf.Lerp(.45f,1,p.Strength));for(int j=0;j<4;j++)p.Colors[q+j]=color;
            }
            p.Dust.vertices=p.Vertices;p.Dust.colors=p.Colors;p.Dust.RecalculateBounds();
        }
        public static string LandingDiagnostics(){int active=0;foreach(var p in landings)if(p!=null&&p.Active)active++;return "accepted="+seenLandingOrder.Count+" active="+active+" grayDust="+(dustMaterial!=null)+" bounded=8";}

        public static void Update(float dt)
        {
            foreach(var p in landings)if(p!=null&&p.Active){if(Time.frameCount!=p.BornFrame)p.Age+=Mathf.Max(0,dt);if(p.Age>=.72f){p.Active=false;p.Root.SetActive(false);}else SetLanding(p);}
            remove.Clear();
            foreach (var pair in projectiles)
            {
                var projectile = pair.Value;
                projectile.Position += projectile.Velocity * Mathf.Min(Mathf.Max(0, dt), projectile.Life);
                projectile.Life -= dt;
                if (projectile.Life <= 0 || projectile.Object == null)
                { if (projectile.Object != null) UnityEngine.Object.Destroy(projectile.Object); remove.Add(pair.Key); }
                else
                {
                    projectile.Object.transform.position = projectile.Position - Origin.position;
                    if (projectile.Velocity.sqrMagnitude > .01f)
                        projectile.Object.transform.rotation = Quaternion.FromToRotation(Vector3.up, projectile.Velocity.normalized);
                }
            }
            foreach (int id in remove) projectiles.Remove(id);
            for (int i = tracers.Count - 1; i >= 0; i--)
            {
                var tracer = tracers[i];
                // Broadcast may create this tracer earlier in this same Update.
                // Spending the already-elapsed frame time made 90ms beams vanish
                // before their very first render at the user's 9–13 FPS.
                if(Time.frameCount!=tracer.BornFrame)tracer.Age+=Mathf.Max(0,dt);
                if (BeamExpired(tracer.Age,tracer.Life,tracer.PresentedFrames) || tracer.Line == null || tracer.Glow == null)
                {
                    if (tracer.Line != null)
                    {
                        if (tracer.Glow!=null&&pool.Count < 32) { tracer.Line.gameObject.SetActive(false); pool.Push(tracer); }
                        else UnityEngine.Object.Destroy(tracer.Line.gameObject);
                    }
                    tracers.RemoveAt(i);
                }
                else
                {
                    float alpha=Mathf.Clamp01((tracer.Life-tracer.Age)/(tracer.Life*.4f));
                    if(tracer.PresentedFrames<2)alpha=Mathf.Max(.65f,alpha);
                    SetBeam(tracer,alpha);
                }
            }
            var world = GameManager.Instance != null ? GameManager.Instance.World : null;
            if (world != null) UpdateBlades(world, dt);
        }

        // ---------------- energy blades ----------------

        sealed class Blade
        {
            public EntityVehicle Vehicle;
            public Transform Root, Edge;
            public TrailRenderer Trail;
            public Vector3 EdgeScale;
            public bool DeployTarget;
            public float DeployState;
            public float Charge;
            public int Swing; public float SwingStart;
        }
        static readonly Dictionary<int, Blade[]> blades = new Dictionary<int, Blade[]>();
        static readonly List<int> removeBlades = new List<int>();
        static Material bladeMaterial;

        static Material BladeMaterial()
        {
            if (bladeMaterial != null) return bladeMaterial;
            var shader = Shader.Find("Standard");
            if (shader == null) shader = Shader.Find("Sprites/Default");
            if (shader == null) return null;
            bladeMaterial = new Material(shader) { name = "MechaBlade" };
            if (bladeMaterial.HasProperty("_EmissionColor"))
            { bladeMaterial.EnableKeyword("_EMISSION"); bladeMaterial.SetColor("_EmissionColor", new Color(.2f, .9f, 1f) * 1.5f); }
            if (bladeMaterial.HasProperty("_Metallic")) bladeMaterial.SetFloat("_Metallic", .8f);
            return bladeMaterial;
        }

        // Two blades mount under the upper panels; they exist only as runtime
        // geometry so the GLB stays untouched.
        static Blade[] GetBlades(World world, EntityVehicle vehicle)
        {
            Blade[] pair;
            if (blades.TryGetValue(vehicle.entityId, out pair) && pair[0].Root != null) return pair;
            if (pair != null) foreach (var blade in pair) if (blade.Root != null) UnityEngine.Object.Destroy(blade.Root.gameObject);
            var rig = Model.GetRig(vehicle);
            if (rig == null) return null;
            var material = BladeMaterial();
            pair = new Blade[2];
            for (int side = 0; side < 2; side++)
            {
                // Hand-held energy blades mount at the runtime palm anchors.
                var mount = side == 0 ? rig.HandL != null ? rig.HandL : rig.Torso : rig.HandR != null ? rig.HandR : rig.Torso;
                if (mount == null) return null;
                var root = new GameObject("MechaBlade").transform;
                root.SetParent(mount, false);
                root.localPosition = new Vector3(0, -.05f, .09f);
                root.localRotation = Quaternion.Euler(12, 0, 0);
                root.gameObject.hideFlags = HideFlags.DontSave;
                var edge = GameObject.CreatePrimitive(PrimitiveType.Cube);
                edge.name = "MechaBladeEdge"; edge.hideFlags = HideFlags.DontSave;
                var collider = edge.GetComponent<Collider>();
                if (collider != null) { collider.enabled = false; UnityEngine.Object.Destroy(collider); }
                var renderer = edge.GetComponent<Renderer>();
                if (material != null) renderer.sharedMaterial = material;
                renderer.shadowCastingMode = ShadowCastingMode.Off;
                edge.transform.SetParent(root, false);
                edge.transform.localPosition = new Vector3(0, -.72f, 0);
                edge.transform.localScale = new Vector3(.05f, 1.4f, .1f);
                var tip = new GameObject("MechaBladeTip").transform;
                tip.SetParent(edge.transform, false);
                tip.localPosition = new Vector3(0, -.7f, 0);
                var trail = tip.gameObject.AddComponent<TrailRenderer>();
                trail.sharedMaterial = Material(true);
                trail.time = .22f; trail.startWidth = .09f; trail.endWidth = .005f;
                trail.startColor = new Color(.5f, 1f, 1f, .9f); trail.endColor = new Color(.2f, .7f, 1f, 0);
                trail.shadowCastingMode = ShadowCastingMode.Off;
                trail.enabled = false;
                pair[side] = new Blade { Vehicle = vehicle, Root = root, Edge = edge.transform, Trail = trail, EdgeScale = edge.transform.localScale };
                pair[side].Edge.localScale = new Vector3(pair[side].EdgeScale.x, .03f, pair[side].EdgeScale.z);
            }
            blades[vehicle.entityId] = pair;
            return pair;
        }

        public static void SetCharge(int vehicleId, float level)
        {
            Blade[] pair;
            if (!blades.TryGetValue(vehicleId, out pair)) return;
            foreach (var blade in pair) blade.Charge = level;
        }

        static void SetBladeMode(World world, int vehicleId, bool on)
        {
            var vehicle = world.GetEntity(vehicleId) as EntityVehicle;
            var pair = vehicle != null ? GetBlades(world, vehicle) : null;
            if (pair != null) foreach (var blade in pair) blade.DeployTarget = on;
        }

        static void PlaySwing(World world, int vehicleId, bool heavy, Vector3 origin, Vector3 forward)
        {
            var vehicle = world.GetEntity(vehicleId) as EntityVehicle;
            var pair = vehicle != null ? GetBlades(world, vehicle) : null;
            if (pair == null) return;
            for (int side = 0; side < pair.Length; side++)
            {
                pair[side].Swing = heavy ? 2 : 1;
                pair[side].SwingStart = Time.time;
                pair[side].DeployTarget = true;
                if (pair[side].Trail != null) pair[side].Trail.enabled = true;
            }
        }

        static void UpdateBlades(World world, float dt)
        {
            if (blades.Count == 0) return;
            removeBlades.Clear();
            foreach (var entry in blades)
            {
                var pair = entry.Value;
                var vehicle = world.GetEntity(entry.Key) as EntityVehicle;
                if (vehicle == null || pair[0].Vehicle != vehicle)
                { foreach (var blade in pair) if (blade.Root != null) UnityEngine.Object.Destroy(blade.Root.gameObject); removeBlades.Add(entry.Key); continue; }
                var status = GetStatus(entry.Key);
                for (int side = 0; side < pair.Length; side++)
                {
                    var blade = pair[side];
                    if (blade.Root == null) continue;
                    blade.DeployTarget = status.Time > -90 && status.MeleeMode;
                    blade.DeployState = Mathf.MoveTowards(blade.DeployState, blade.DeployTarget ? 1 : 0, dt / .3f);
                    if (blade.Edge != null)
                        blade.Edge.localScale = new Vector3(blade.EdgeScale.x, Mathf.Max(.03f, blade.EdgeScale.y * blade.DeployState), blade.EdgeScale.z);
                    var rotation = Quaternion.Euler(12, 0, 0);
                    float swingTime = .35f;
                    float t = blade.Swing != 0 ? (Time.time - blade.SwingStart) / swingTime : -1;
                    if (t >= 0 && t <= 1)
                    {
                        float ease = 1 - Mathf.Pow(1 - t, 3);
                        rotation *= blade.Swing == 1
                            ? Quaternion.Euler(0, (side == 0 ? 1f : -1f) * 175f * ease, 0)
                            : Quaternion.Euler(-95f * ease + 55f * ease * ease, 0, 0);
                    }
                    else if (blade.Swing != 0)
                    {
                        blade.Swing = 0;
                        if (blade.Trail != null) blade.Trail.enabled = false;
                    }
                    blade.Root.localRotation = rotation;
                    var material = bladeMaterial;
                    if (material != null && material.HasProperty("_EmissionColor"))
                        material.SetColor("_EmissionColor", new Color(.2f, .9f, 1f) * (1.5f + blade.Charge * 3.5f));
                }
            }
            foreach (int id in removeBlades) blades.Remove(id);
        }

        public static void Clear()
        {
            foreach(var p in landings)if(p!=null){Release(p.Root);if(p.Dust!=null)UnityEngine.Object.Destroy(p.Dust);}Array.Clear(landings,0,landings.Length);seenLandingSerials.Clear();seenLandingOrder.Clear();lastLanding.Clear();
            if(dustMaterial!=null)UnityEngine.Object.Destroy(dustMaterial);if(dustTexture!=null)UnityEngine.Object.Destroy(dustTexture);dustMaterial=null;dustTexture=null;
            foreach (var projectile in projectiles.Values) Release(projectile.Object);
            foreach (var tracer in tracers) if (tracer.Line != null) Release(tracer.Line.gameObject);
            foreach (var tracer in pool) if (tracer.Line != null) Release(tracer.Line.gameObject);
            foreach (var pair in blades.Values) foreach (var blade in pair)
                if (blade.Root != null) Release(blade.Root.gameObject);
            projectiles.Clear(); tracers.Clear(); pool.Clear(); statuses.Clear(); blades.Clear(); meleeCooldownUntil.Clear();
            seenBeamSerials.Clear(); seenBeamOrder.Clear();
            soundGate.Clear();
            if (beamMaterial != null) UnityEngine.Object.Destroy(beamMaterial);
            if (bodyMaterial != null) UnityEngine.Object.Destroy(bodyMaterial);
            if (bladeMaterial != null) UnityEngine.Object.Destroy(bladeMaterial);
            beamMaterial = bodyMaterial = bladeMaterial = null;missingBeamShader=false;
        }
        static void Release(GameObject go){if(go!=null){go.SetActive(false);UnityEngine.Object.Destroy(go);}}
    }
    // LineRenderer invokes this only when a game camera actually presents it.
    // Reflection cameras and repeated draws in one game frame cannot burn the lease.
    public sealed class MechaBeamPresentation:MonoBehaviour
    {
        public Action<int> Presented;
        void OnWillRenderObject()
        {
            var camera=Camera.current;if(camera==null||camera.cameraType!=CameraType.Game)return;
            var world=GameManager.Instance!=null?GameManager.Instance.World:null;var player=world!=null?world.GetPrimaryPlayer():null;
            if(player!=null&&camera!=player.playerCamera)return;
            if(Presented!=null)Presented(Time.frameCount);
        }
    }
}
