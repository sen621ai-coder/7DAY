using System;
using System.Collections.Generic;
using System.IO;
using PZAEC.Fishing.Contracts;
using UnityEngine;
using UnityEngine.Rendering;
using Object=UnityEngine.Object;

namespace PZAEC.Fishing.Presentation
{
    public sealed class PresentationOptions
    {
        public bool AudioEnabled=true, CameraFeedbackEnabled=true;
        public float AudioVolume=.55f, LineWidthMeters=.0012f, CameraMaxDegrees=.35f;
        // Optional local native hand mount, in scene coordinates. Other players use snapshots.
        public Func<Vector3?> RightHandScene;
    }

    /// <summary>Main-thread only. Construction and Begin do not touch any Unity objects.</summary>
    public sealed class FishingPresentation : IFishingPresentation
    {
        readonly string modDirectory;
        readonly bool dedicated;
        readonly Action<Vector3> cameraFeedback;
        readonly Action<string> log;
        readonly PresentationOptions options;
        readonly PresentationEventGate gate=new PresentationEventGate();
        readonly Queue<FishingEvent> pending=new Queue<FishingEvent>(32);
        readonly Vec3[] linePoints=new Vec3[33];
        readonly Vector3[] linePositions=new Vector3[33];
        readonly Vec3[] leaderPoints=new Vec3[17];
        readonly Vector3[] leaderPositions=new Vector3[17],guidePositions=new Vector3[8];
        readonly Transform[] guides=new Transform[6],pectoral=new Transform[2],gills=new Transform[2];
        readonly PresentationMotion motion=new PresentationMotion();
        readonly Transform[] rodBones=new Transform[13], fishBones=new Transform[6];
        readonly Transform[] grips=new Transform[2];
        readonly Ripple[] ripples=new Ripple[8];
        readonly AudioSource[] shots=new AudioSource[4];
        readonly List<GameObject> audioTails=new List<GameObject>(4);
        Guid session;
        bool disposed, failed, feedbackActive, debug;
        FishingConfig config;
        PresentationAssets assetLease;
        AssetBundle bundle => assetLease==null?null:assetLease.Bundle;
        GameObject root, rod, bobber, fish;
        Transform lineExit, crank, fishMouth,floatEye,spool,bail,reelHand,mouthVisual;
        LineRenderer line,leader,guideLine;
        Material lineMaterial, rippleMaterial;
        AudioClip dragClip, splashClip, snapClip;
        AudioSource dragAudio;
        FishingDebugOverlay overlay;
        double visualTime, slipUntil=-1;
        float cameraKick;
        int nextRipple,nextShot;
        Vec3 renderOrigin;
        FishingSnapshot lastSnapshot;
        public bool IsReady => root!=null&&!failed;
        public string LastError {get;private set;}
        public Transform RightGrip => grips[0];
        public Transform LeftGrip => grips[1];
        public Transform ReelHandTarget => reelHand;
        public float LineRouteExcessMeters {get;private set;}
        public float RodChordExcessMeters {get;private set;}
        bool routeWarning;
        sealed class Ripple {public LineRenderer Line;public LineRenderer[] Drops=new LineRenderer[6];public Vec3 Absolute;public double Start;public float Strength;public bool Active,Spray;public Vector3[] Points=new Vector3[25];}

        public FishingPresentation(string modDirectory,bool dedicatedServer=false,PresentationOptions options=null,Action<Vector3> cameraFeedback=null,Action<string> diagnosticLog=null)
        {this.modDirectory=modDirectory??throw new ArgumentNullException(nameof(modDirectory));dedicated=dedicatedServer;this.options=options??new PresentationOptions();this.cameraFeedback=cameraFeedback;log=diagnosticLog;}

        public void Begin(SessionStart start,FishingConfig config)
        {
            if(disposed)throw new ObjectDisposedException(nameof(FishingPresentation));
            if(start.SessionId==Guid.Empty||config==null||config.Rod==null||config.Line==null||config.Float==null||config.Controls==null)throw new ArgumentException("Invalid presentation session");
            Clear();this.config=config;session=start.SessionId;gate.Reset(session);failed=false;LastError=null;
        }
        public void OnEvent(FishingEvent value)
        {
            if(disposed||dedicated||!gate.Accept(value))return;
            // The driver clears a resolved session in the same tick. Let a bounded terminal
            // one-shot finish outside the gameplay root; Dispose always removes it immediately.
            if(root!=null && (value.Kind==FishingEventKind.LineBroken||value.Kind==FishingEventKind.Landed))
            {PlayTerminal(value);return;}
            // Bounded queue; presentation never stalls gameplay when render frames are skipped.
            if(pending.Count==32)pending.Dequeue();pending.Enqueue(value);
        }
        public void Render(RenderFrame frame)
        {
            if(disposed||dedicated)return;
            if(frame.IsDedicatedServer){Clear();return;}
            if(session==Guid.Empty||failed||frame.Current.SessionId!=session)return;
            if(!frame.RenderOrigin.IsFinite||!PresentationMath.Valid(frame.Current)) {Hide();return;}
            if(frame.Current.Phase==FishingPhase.Idle||frame.Current.Phase==FishingPhase.Cancelled){Hide();return;}
            try
            {
                if(root==null)CreateObjects();
                root.SetActive(true);
                bool interp=PresentationMath.CanInterpolate(frame)&&PresentationMath.Valid(frame.Previous);
                var a=interp?frame.Previous:frame.Current;var b=frame.Current;float t=interp?PresentationMath.Saturate(frame.Alpha):1;
                renderOrigin=frame.RenderOrigin;lastSnapshot=b;
                double oldTime=visualTime;visualTime=a.TimeSeconds+(b.TimeSeconds-a.TimeSeconds)*t;
                float dt=(float)Math.Max(0,Math.Min(.1,visualTime-oldTime));
                float shownLength=Mathf.Lerp(a.LineLengthMeters,b.LineLengthMeters,t);
                float speed=U(Vec3.Lerp(a.FishVelocity,b.FishVelocity,t)).magnitude;
                bool burst=b.FishBehavior==FishBehavior.Sprinting||b.FishBehavior==FishBehavior.LandingSurge;
                motion.Step(visualTime,speed,burst,shownLength);
                Vec3 basePos=Vec3.Lerp(a.Rod.Root,b.Rod.Root,t),tip=Vec3.Lerp(a.Rod.Tip,b.Rod.Tip,t),forward=Vec3.Lerp(PresentationMath.RodAim(a.Rod),PresentationMath.RodAim(b.Rod),t).Normalized;
                Vector3 up=Vector3.Cross(U(forward),U(b.Rod.Right));if(up.sqrMagnitude<.001f)up=Vector3.up;
                rod.transform.position=Scene(basePos);rod.transform.rotation=Look(U(forward),up);
                float rodScale=Safe(config.Rod.LengthMeters,2.4f,.2f,8)/2.7f;rod.transform.localScale=Vector3.one*rodScale;
                var hand=frame.IsLocalPlayer?options.RightHandScene?.Invoke():null;
                if(hand.HasValue) {
                    Vector3 correction=hand.Value-grips[0].position;
                    rod.transform.position+=correction;basePos=V(rod.transform.position)+renderOrigin;
                    // Keep the authoritative tip; fit the bend from the actual hand-aligned butt.
                }
                RodChordExcessMeters=Mathf.Max(0,(tip-basePos).Length-config.Rod.LengthMeters);
                // Fit actual simulated endpoints, with no invented rod-force response.
                for(int i=0;i<rodBones.Length;i++)
                {
                    float f=(.24f+i*2.46f/12)/2.7f;
                    Vec3 p=PresentationMath.RodPoint(basePos,tip,forward,config.Rod.LengthMeters,f);
                    Vec3 prev=PresentationMath.RodPoint(basePos,tip,forward,config.Rod.LengthMeters,Math.Max(0,f-.01f));
                    Vec3 next=PresentationMath.RodPoint(basePos,tip,forward,config.Rod.LengthMeters,Math.Min(1,f+.01f));
                    rodBones[i].position=Scene(p);rodBones[i].rotation=Look(U(next-prev),up);
                }
                Vec3 floatPos=Vec3.Lerp(a.FloatPosition,b.FloatPosition,t),fishPos=Vec3.Lerp(a.FishPosition,b.FishPosition,t);
                bobber.transform.position=Scene(floatPos);Vector3 floatUp=U(Vec3.Lerp(a.FloatUp,b.FloatUp,t));bobber.transform.rotation=floatUp.sqrMagnitude>.001f?Quaternion.FromToRotation(Vector3.up,floatUp.normalized):Quaternion.identity;
                bobber.transform.localScale=Vector3.one*(Safe(config.Float.HeightMeters,.18f,.04f,.5f)/.27f);
                // FloatPosition is authoritative: do not add a second sine-wave "bite" animation.
                fish.transform.position=Scene(fishPos);fish.transform.rotation=Look(U(Vec3.Lerp(a.FishForward,b.FishForward,t)),Vector3.up);
                float mass=Safe(b.FishMassKg,3,.02f,80);fish.transform.localScale=Vector3.one*Mathf.Clamp(Mathf.Pow(mass/3,1f/3f),.3f,3);
                float amplitude=motion.SwimAmplitudeDegrees,phase=motion.SwimPhase;
                for(int i=1;i<fishBones.Length;i++)fishBones[i].localRotation=Quaternion.Euler(0,Mathf.Sin(phase-i*.7f)*amplitude*(i/5f),0);
                for(int i=0;i<2;i++)
                {
                    float side=i==0?-1:1;
                    pectoral[i].localRotation=Quaternion.Euler(0,side*Mathf.Clamp(speed*3,0,18),side*Mathf.Sin(phase*.5f)*3);
                    gills[i].localScale=new Vector3(1+.045f*(.5f+.5f*Mathf.Sin(phase*.65f)),1,1);
                }
                mouthVisual.localScale=new Vector3(1,.65f+.12f*Mathf.Sin(phase*.65f),1);
                bool fighting=b.Phase==FishingPhase.Hooked||b.Phase==FishingPhase.Fighting||b.Phase==FishingPhase.Landing||b.Phase==FishingPhase.Resolved;
                fish.SetActive(fighting);bobber.SetActive(b.Phase!=FishingPhase.Resolved);
                Vec3 end=V(fishMouth.position),eye=V(floatEye.position);
                // Line exits the visible guide; its location includes bone pose and guide offset.
                Vec3 start=V(lineExit.position);
                float budget=shownLength+Safe(Mathf.Lerp(a.LineExtensionMeters,b.LineExtensionMeters,t),0,0,20);
                LineRouteExcessMeters=PresentationMath.FillRoutedLine(start,eye,end,budget,linePoints,leaderPoints);
                if(LineRouteExcessMeters>.03f&&!routeWarning){routeWarning=true;log?.Invoke("Fishing line route exceeds authoritative length by "+LineRouteExcessMeters.ToString("F3")+"m; A/B route constraint requires reconciliation.");}
                for(int i=0;i<linePoints.Length;i++)linePositions[i]=U(linePoints[i]);line.SetPositions(linePositions);
                for(int i=0;i<leaderPoints.Length;i++)leaderPositions[i]=U(leaderPoints[i]);leader.SetPositions(leaderPositions);
                line.enabled=b.Phase!=FishingPhase.LineBroken&&b.Phase!=FishingPhase.HookLost&&b.Phase!=FishingPhase.Resolved;
                leader.enabled=line.enabled;
                crank.localRotation=Quaternion.Euler(motion.HandleDegrees,0,0);
                spool.localRotation=Quaternion.Euler(0,0,motion.SpoolDegrees);
                bail.localRotation=Quaternion.Euler(0,0,-motion.HandleDegrees*5.2f);
                guidePositions[0]=spool.TransformPoint(new Vector3(0,0,.043f));
                for(int i=0;i<6;i++)guidePositions[i+1]=guides[i].position;guidePositions[7]=lineExit.position;
                guideLine.SetPositions(guidePositions);guideLine.enabled=true;
                while(pending.Count>0)HandleEvent(pending.Dequeue());
                if(fighting&&b.LineLengthMeters>a.LineLengthMeters+.0005f)slipUntil=visualTime+.15;
                DrawRipples();UpdateAudio(b);UpdateCamera(frame.IsLocalPlayer,b,dt);
                debug=frame.ShowDebug&&frame.IsLocalPlayer;overlay.enabled=debug;
                if(debug)overlay.Set(b,LineRouteExcessMeters,RodChordExcessMeters);
            }
            catch(Exception ex)
            {
                LastError=ex.GetType().Name+": "+ex.Message;failed=true;Hide();log?.Invoke("Fishing presentation unavailable: "+LastError);
                // Visual failures must not break authoritative state or write inventory.
            }
        }
        static float Safe(float value,float fallback,float min,float max)=>Scalar.IsFinite(value)?Mathf.Clamp(value,min,max):fallback;
        static Vector3 U(Vec3 value)=>new Vector3(value.X,value.Y,value.Z);
        static Vec3 V(Vector3 value)=>new Vec3(value.x,value.y,value.z);
        Vector3 Scene(Vec3 value)=>U(value-renderOrigin);
        static Quaternion Look(Vector3 direction,Vector3 up)
        {
            if(direction.sqrMagnitude<.00001f)return Quaternion.identity;
            if(up.sqrMagnitude<.00001f||Vector3.Cross(direction,up).sqrMagnitude<.00001f)up=Mathf.Abs(direction.normalized.y)>.95f?Vector3.forward:Vector3.up;
            return Quaternion.LookRotation(direction,up);
        }
        T Asset<T>(string name) where T:Object
        {var asset=bundle.LoadAsset<T>("assets/fishing/"+name);if(asset==null)throw new InvalidDataException("Missing fishing asset: "+name);return asset;}
        GameObject Model(string name)
        {var g=Object.Instantiate(Asset<GameObject>(name),root.transform);g.name=Path.GetFileNameWithoutExtension(name);return g;}
        static Transform Find(Transform parent,string name)
        {foreach(var t in parent.GetComponentsInChildren<Transform>(true))if(t.name==name)return t;throw new InvalidDataException("Missing fishing rig anchor: "+name);}
        void CreateObjects()
        {
            if(assetLease==null)assetLease=new PresentationAssets(modDirectory);
            root=new GameObject("PZAEC.Fishing.Presentation");
            rod=Model("fishingrod.prefab");bobber=Model("fishingfloat.prefab");fish=Model("fishingfish.prefab");
            for(int i=0;i<13;i++)rodBones[i]=Find(rod.transform,"RodBone"+i.ToString("00"));
            for(int i=0;i<6;i++)fishBones[i]=Find(fish.transform,"FishBone"+i.ToString("00"));
            lineExit=Find(rod.transform,"LineExit");crank=Find(rod.transform,"Crank");grips[0]=Find(rod.transform,"GripRight");grips[1]=Find(rod.transform,"GripLeft");
            fishMouth=Find(fish.transform,"Mouth");
            floatEye=Find(bobber.transform,"LineAttachment");spool=Find(rod.transform,"Spool");bail=Find(rod.transform,"Bail");reelHand=Find(rod.transform,"ReelHandTarget");mouthVisual=Find(fish.transform,"MouthVisual");
            for(int i=0;i<6;i++)guides[i]=Find(rod.transform,"GuideLine"+(2+i*2));
            pectoral[0]=Find(fish.transform,"PectoralPivotL");pectoral[1]=Find(fish.transform,"PectoralPivotR");gills[0]=Find(fish.transform,"GillPivotL");gills[1]=Find(fish.transform,"GillPivotR");
            lineMaterial=Object.Instantiate(Asset<Material>("line.mat"));rippleMaterial=Object.Instantiate(Asset<Material>("foam.mat"));
            line=MakeLine("FishingLine",linePoints.Length,lineMaterial,Safe(options.LineWidthMeters,.0012f,.0005f,.003f));
            leader=MakeLine("FishingLeader",leaderPoints.Length,lineMaterial,Safe(options.LineWidthMeters*.8f,.001f,.0005f,.0025f));
            guideLine=MakeLine("GuideThread",guidePositions.Length,lineMaterial,.00065f);
            for(int i=0;i<ripples.Length;i++)
            {ripples[i]=new Ripple{Line=MakeLine("Ripple"+i,25,rippleMaterial,.007f)};for(int j=0;j<6;j++)ripples[i].Drops[j]=MakeLine("Drop"+i+"_"+j,2,rippleMaterial,.004f);}
            dragClip=Asset<AudioClip>("reeldrag.wav");splashClip=Asset<AudioClip>("watersplash.wav");snapClip=Asset<AudioClip>("linesnap.wav");
            dragAudio=MakeAudio("DragAudio");dragAudio.loop=true;dragAudio.clip=dragClip;
            for(int i=0;i<shots.Length;i++)shots[i]=MakeAudio("ShotAudio"+i);
            overlay=root.AddComponent<FishingDebugOverlay>();overlay.enabled=false;
        }
        LineRenderer MakeLine(string name,int count,Material mat,float width)
        {
            var g=new GameObject(name);g.transform.SetParent(root.transform,false);var lr=g.AddComponent<LineRenderer>();lr.sharedMaterial=mat;lr.useWorldSpace=true;lr.positionCount=count;lr.widthMultiplier=width;lr.numCapVertices=2;lr.shadowCastingMode=ShadowCastingMode.Off;lr.receiveShadows=false;lr.enabled=false;return lr;
        }
        AudioSource MakeAudio(string name)
        {var g=new GameObject(name);g.transform.SetParent(root.transform,false);var s=g.AddComponent<AudioSource>();s.playOnAwake=false;s.spatialBlend=1;s.minDistance=1;s.maxDistance=24;s.rolloffMode=AudioRolloffMode.Logarithmic;return s;}
        void HandleEvent(FishingEvent value)
        {
            float power=PresentationMath.Saturate(value.Intensity01);
            if(value.Kind==FishingEventKind.DragSlip){slipUntil=visualTime+.2;return;}
            if(value.Kind==FishingEventKind.WaterContact||value.Kind==FishingEventKind.Nibble||value.Kind==FishingEventKind.BaitTaken)
            {var surface=FloatSurface();AddRipple(surface,power,value.Kind==FishingEventKind.WaterContact);if(value.Kind==FishingEventKind.WaterContact)Play(splashClip,surface,power);}
            if(value.Kind==FishingEventKind.Sprint)
            {
                cameraKick=Mathf.Max(cameraKick,power);
                // Sprint events may occur underwater: never invent surface foam at the fish depth.
                if(lastSnapshot.FloatSubmerged01<.95f)AddRipple(FloatSurface(),power*.5f);
            }
            if(value.Kind==FishingEventKind.Hooked)cameraKick=Mathf.Max(cameraKick,power*.5f);
            if(value.Kind==FishingEventKind.LineBroken)Play(snapClip,lastSnapshot.Rod.Root,Mathf.Max(.4f,power));
            if(value.Kind==FishingEventKind.Cancelled||value.Kind==FishingEventKind.HookLost)slipUntil=-1;
        }
        void AddRipple(Vec3 absolute,float strength,bool spray=false)
        {var r= ripples[nextRipple++%ripples.Length];r.Absolute=absolute;r.Start=visualTime;r.Strength=Mathf.Max(.1f,strength);r.Active=true;r.Spray=spray;}
        Vec3 FloatSurface()
        {
            // FloatPosition is the float center. For partial immersion v1's submerged fraction
            // reconstructs the logic surface; at 0/1 this is a bounded estimate, not GPU waves.
            var p=lastSnapshot.FloatPosition;p.Y+=(PresentationMath.Saturate(lastSnapshot.FloatSubmerged01)-.5f)*Safe(config.Float.HeightMeters,.18f,.04f,.5f);return p;
        }
        void DrawRipples()
        {
            foreach(var r in ripples)
            {
                double age=visualTime-r.Start;bool show=r.Active&&age>=0&&age<.85;r.Line.enabled=show;if(!show){r.Active=false;foreach(var drop in r.Drops)drop.enabled=false;continue;}
                float t=(float)(age/.85),radius=.015f+t*(.15f+.35f*r.Strength);Vector3 center=Scene(r.Absolute)+Vector3.up*.004f;
                for(int i=0;i<r.Points.Length;i++){float a=i*2*Mathf.PI/(r.Points.Length-1);r.Points[i]=center+new Vector3(Mathf.Cos(a)*radius,0,Mathf.Sin(a)*radius);}
                r.Line.SetPositions(r.Points);var color=new Color(.8f,.91f,.92f,(1-t)*r.Strength*.65f);r.Line.startColor=r.Line.endColor=color;
                for(int i=0;i<r.Drops.Length;i++)
                {
                    float seconds=(float)age,velocity=.6f+r.Strength*.8f,height=velocity*seconds-4.9f*seconds*seconds;
                    var drop=r.Drops[i];drop.enabled=r.Spray&&height>0;
                    if(!drop.enabled)continue;float angle=i*Mathf.PI/3;var p=center+new Vector3(Mathf.Cos(angle)*seconds*.6f,height,Mathf.Sin(angle)*seconds*.6f);
                    drop.SetPosition(0,p);drop.SetPosition(1,p+Vector3.up*.012f);drop.startColor=drop.endColor=color;
                }
            }
        }
        void Play(AudioClip clip,Vec3 location,float power)
        {if(!options.AudioEnabled)return;var s=shots[nextShot++%shots.Length];s.transform.position=Scene(location);s.volume=PresentationMath.Saturate(options.AudioVolume)*Mathf.Clamp(power,.12f,1);s.clip=clip;s.Play();}
        void PlayTerminal(FishingEvent value)
        {
            if(!options.AudioEnabled)return;
            for(int i=audioTails.Count-1;i>=0;i--)if(audioTails[i]==null)audioTails.RemoveAt(i);
            if(audioTails.Count>=4){DestroyOwned(audioTails[0]);audioTails.RemoveAt(0);}
            var g=new GameObject("PZAEC.Fishing.TerminalAudio");g.transform.position=Scene(value.Kind==FishingEventKind.LineBroken?lastSnapshot.Rod.Root:value.Position);
            var s=g.AddComponent<AudioSource>();s.playOnAwake=false;s.spatialBlend=1;s.minDistance=1;s.maxDistance=24;
            s.clip=value.Kind==FishingEventKind.LineBroken?snapClip:splashClip;s.volume=PresentationMath.Saturate(options.AudioVolume)*Mathf.Max(.4f,PresentationMath.Saturate(value.Intensity01));s.Play();audioTails.Add(g);
            if(Application.isPlaying)Object.Destroy(g,s.clip.length+.05f);
            else DestroyOwned(g); // Editor verification has no frame-driven destruction timer.
        }
        void UpdateAudio(FishingSnapshot value)
        {
            dragAudio.transform.position=rod.transform.position;
            bool play=options.AudioEnabled&&visualTime<=slipUntil&&!value.IsTerminal;
            if(play){dragAudio.volume=PresentationMath.Saturate(options.AudioVolume)*Mathf.Lerp(.15f,.55f,Mathf.Clamp01(motion.PayoutMetersPerSecond/3));dragAudio.pitch=Mathf.Clamp(.7f+motion.PayoutMetersPerSecond*.3f,.7f,1.9f);if(!dragAudio.isPlaying)dragAudio.Play();}
            else if(dragAudio.isPlaying)dragAudio.Stop();
        }
        void UpdateCamera(bool local,FishingSnapshot value,float dt)
        {
            cameraKick*=Mathf.Exp(-dt*9);
            if(!local||!options.CameraFeedbackEnabled||cameraFeedback==null){ResetCamera();return;}
            float amount=PresentationMath.Saturate(config.Controls.FeedbackIntensity01)*Safe(options.CameraMaxDegrees,.35f,0,1)*cameraKick;
            var offset=new Vector3(Mathf.Sin((float)visualTime*27)*amount,0,Mathf.Sin((float)visualTime*19)*amount*.25f);
            cameraFeedback(offset);feedbackActive=true;
        }
        void ResetCamera(){if(feedbackActive){feedbackActive=false;cameraFeedback?.Invoke(Vector3.zero);}}
        void Hide()
        {if(root!=null)root.SetActive(false);if(dragAudio!=null)dragAudio.Stop();foreach(var s in shots)if(s!=null)s.Stop();ResetCamera();}
        public void Clear()
        {
            session=Guid.Empty;gate.Reset(Guid.Empty);pending.Clear();visualTime=0;slipUntil=-1;cameraKick=0;nextRipple=nextShot=0;debug=false;
            motion.Reset();LineRouteExcessMeters=RodChordExcessMeters=0;routeWarning=false;
            Hide();if(root!=null)DestroyOwned(root);root=null;rod=bobber=fish=null;lineExit=crank=fishMouth=floatEye=spool=bail=reelHand=mouthVisual=null;line=leader=guideLine=null;dragAudio=null;overlay=null;
            if(lineMaterial!=null)DestroyOwned(lineMaterial);if(rippleMaterial!=null)DestroyOwned(rippleMaterial);lineMaterial=rippleMaterial=null;
            Array.Clear(grips,0,grips.Length);Array.Clear(rodBones,0,rodBones.Length);Array.Clear(fishBones,0,fishBones.Length);Array.Clear(shots,0,shots.Length);Array.Clear(ripples,0,ripples.Length);
            Array.Clear(guides,0,guides.Length);Array.Clear(pectoral,0,pectoral.Length);Array.Clear(gills,0,gills.Length);
        }
        public void Dispose()
        {if(disposed)return;Clear();foreach(var tail in audioTails)if(tail!=null)DestroyOwned(tail);audioTails.Clear();if(assetLease!=null)assetLease.Dispose();assetLease=null;disposed=true;}
        static void DestroyOwned(Object value){if(Application.isPlaying)Object.Destroy(value);else Object.DestroyImmediate(value);}
    }

    public sealed class FishingDebugOverlay : MonoBehaviour
    {
        string text="";long tick=long.MinValue;
        public void Set(FishingSnapshot s,float routeExcess=0,float rodExcess=0)
        {if(tick==s.Tick)return;tick=s.Tick;text=string.Format("Fishing debug | {0} | {1}\nTension {2:F1} N  Line {3:F2} m\nStamina {4:P0}  Burst {5}\nFloat submerged {6:P0}\nRoute deficit {7:F3}m / Rod excess {8:F3}m",s.Phase,s.FishBehavior,s.LineTensionNewtons,s.LineLengthMeters,s.FishStamina01,s.BurstIndex,s.FloatSubmerged01,routeExcess,rodExcess);}
        void OnGUI(){GUI.Box(new Rect(20,20,370,120),text);}
    }
}
