using System;
using System.Collections.Generic;
using UnityEngine;
namespace PZAEC.Mecha
{
    // Separate protocol: no overload of Motion flags/charge/vertical input.
    public sealed class TraverseWire
    {
        public int Vehicle,Actor,Action,Tick;public byte Mode;public Traversal.Stage Phase;public float Age;
        public Traversal.Plan Plan;
        public const int Bytes=160;
        public TraverseWire Setup(EntityVehicle v,Traversal.State s,byte mode)
        {Vehicle=v.entityId;Actor=s.Actor;Action=s.Current!=null?s.Current.Id:s.NextId;Tick=++s.Tick;Mode=mode;Phase=s.Phase;Age=s.Age;Plan=s.Current;return this;}
        static void Vector(PooledBinaryWriter w,Vector3 p){w.Write(p.x);w.Write(p.y);w.Write(p.z);}
        static Vector3 Vector(PooledBinaryReader r){return new Vector3(r.ReadSingle(),r.ReadSingle(),r.ReadSingle());}
        public void Write(PooledBinaryWriter w)
        {
            w.Write(Vehicle);w.Write(Actor);w.Write(Action);w.Write(Tick);w.Write(Mode);w.Write((byte)Phase);w.Write(Age);
            var p=Plan??new Traversal.Plan();w.Write((byte)p.Type);w.Write((byte)p.Front);w.Write(p.Rotation.eulerAngles.y);
            Vector(w,p.Root);Vector(w,p.End);for(int i=0;i<2;i++){Vector(w,p.Start[i]);Vector(w,p.StartNormal[i]);Vector(w,p.Land[i].Point);Vector(w,p.Land[i].Normal);}
            w.Write(p.Duration);w.Write(p.Height);w.Write(p.Width);
        }
        public void Read(PooledBinaryReader r)
        {
            Vehicle=r.ReadInt32();Actor=r.ReadInt32();Action=r.ReadInt32();Tick=r.ReadInt32();Mode=r.ReadByte();Phase=(Traversal.Stage)r.ReadByte();Age=r.ReadSingle();
            var p=new Traversal.Plan{Id=Action,Actor=Actor,Type=(Traversal.Kind)r.ReadByte(),Front=r.ReadByte()};
            p.Rotation=Quaternion.Euler(0,r.ReadSingle(),0);p.Root=Vector(r);p.End=Vector(r);
            for(int i=0;i<2;i++){p.Start[i]=Vector(r);p.StartNormal[i]=Vector(r);p.Land[i]=new GroundSupport.Pad{Point=Vector(r),Normal=Vector(r)};}
            p.Duration=r.ReadSingle();p.Height=r.ReadSingle();p.Width=r.ReadSingle();Plan=p;
        }
        static bool Finite(Vector3 p){return Weapons.Finite(p.x)&&Weapons.Finite(p.y)&&Weapons.Finite(p.z);}
        public bool Valid()
        {
            var p=Plan;if(Mode>3||Phase>Traversal.Stage.Exit||Action<=0||Tick<=0||!Weapons.Finite(Age)||Age<0)return false;
            if(Mode>=2)return true;
            if(p==null||p.Front>1||p.Type>Traversal.Kind.Gap||!Finite(p.Root)||!Finite(p.End)||!Weapons.Finite(p.Rotation.eulerAngles.y)||!Weapons.Finite(p.Duration)||p.Duration<.75f||p.Duration>1.1f||Age>p.Duration+.02f||!Weapons.Finite(p.Height)||Mathf.Abs(p.Height)>Rules.ActiveStepHeight+.02f||!Weapons.Finite(p.Width)||p.Width<0||p.Width>Rules.ActiveGapWidth+.02f)return false;
            if(!Weapons.Finite(p.Rotation.x)||!Weapons.Finite(p.Rotation.y)||!Weapons.Finite(p.Rotation.z)||!Weapons.Finite(p.Rotation.w)||Vector3.Distance(p.Root,p.End)>2.5f)return false;
            for(int i=0;i<2;i++)if(!Finite(p.StartNormal[i])||Mathf.Abs(p.StartNormal[i].magnitude-1)>.02f||p.StartNormal[i].y<.707f||p.Land[i].Normal.y<.707f||!Finite(p.Start[i])||!Finite(p.Land[i].Point)||!Finite(p.Land[i].Normal)||(p.Start[i]-p.Root).sqrMagnitude>6||(p.Land[i].Point-p.End).sqrMagnitude>6||Mathf.Abs(p.Land[i].Normal.magnitude-1)>.02f)return false;
            return Phase==p.Phase(Age)||(Phase==Traversal.Stage.Settle&&Age>=p.Duration);
        }
    }
    public sealed class NetPackagePZAECMechaTraverseIntent : NetPackage
    {
        public TraverseWire Data=new TraverseWire();
        public override NetPackageDirection PackageDirection {get{return NetPackageDirection.ToServer;}}
        public NetPackagePZAECMechaTraverseIntent Setup(TraverseWire data){Data=data;return this;}
        public override int GetLength(){return TraverseWire.Bytes;}
        public override void read(PooledBinaryReader r){Data=new TraverseWire();Data.Read(r);}
        public override void write(PooledBinaryWriter w){base.write(w);Data.Write(w);}
        public override void ProcessPackage(World world,GameManager callbacks){if(Weapons.Server&&world!=null&&Sender!=null&&Sender.entityId==Data.Actor)TraversalNet.ServerReceive(world,Sender.entityId,Data);}
    }
    public sealed class NetPackagePZAECMechaTraverseEvent : NetPackage
    {
        public TraverseWire Data=new TraverseWire();
        public override NetPackageDirection PackageDirection {get{return NetPackageDirection.ToClient;}}
        public NetPackagePZAECMechaTraverseEvent Setup(TraverseWire data){Data=data;return this;}
        public override int GetLength(){return TraverseWire.Bytes;}
        public override void read(PooledBinaryReader r){Data=new TraverseWire();Data.Read(r);}
        public override void write(PooledBinaryWriter w){base.write(w);Data.Write(w);}
        public override void ProcessPackage(World world,GameManager callbacks){if(!Weapons.Server&&world!=null)TraversalNet.ClientReceive(world,Data);}
    }
    public static class TraversalNet
    {
        sealed class Session {public int Actor,Id,Tick;public float Started,Seen,Age;public Traversal.Stage Phase;public Traversal.Plan Plan;}
        static readonly Dictionary<int,Session> sessions=new Dictionary<int,Session>();
        static readonly Dictionary<int,float> searches=new Dictionary<int,float>();
        public static void Start(EntityVehicle v,Traversal.State s,GroundSupport.State support)
        {
            s.LastPacket=Time.time;var data=new TraverseWire().Setup(v,s,0);
            if(Weapons.Server)ServerReceive(GameManager.Instance.World,s.Actor,data);
            else ConnectionManager.Instance.SendToServer(NetPackageManager.GetPackage<NetPackagePZAECMechaTraverseIntent>().Setup(data));
        }
        public static void Publish(EntityVehicle v,Traversal.State s,bool edge)
        {
            if(s.Remote||(!edge&&Time.time-s.LastSend<.1f))return;s.LastSend=Time.time;
            var data=new TraverseWire().Setup(v,s,s.Current==null?(byte)2:(byte)1);
            if(Weapons.Server)ServerReceive(GameManager.Instance.World,s.Actor,data);
            else ConnectionManager.Instance.SendToServer(NetPackageManager.GetPackage<NetPackagePZAECMechaTraverseIntent>().Setup(data));
        }
        static bool Matches(Traversal.Plan a,Traversal.Plan b)
        {
            if(a==null||b==null||a.Type!=b.Type||a.Front!=b.Front||Mathf.Abs(a.Duration-b.Duration)>.001f||Mathf.Abs(a.Height-b.Height)>.001f||Mathf.Abs(a.Width-b.Width)>.001f||Quaternion.Angle(a.Rotation,b.Rotation)>.01f||Vector3.Distance(a.Root,b.Root)>.001f||Vector3.Distance(a.End,b.End)>.001f)return false;
            for(int i=0;i<2;i++)if(Vector3.Distance(a.StartNormal[i],b.StartNormal[i])>.001f||Vector3.Distance(a.Start[i],b.Start[i])>.001f||Vector3.Distance(a.Land[i].Point,b.Land[i].Point)>.001f||Vector3.Distance(a.Land[i].Normal,b.Land[i].Normal)>.001f)return false;
            return true;
        }
        public static bool ServerReceive(World world,int actor,TraverseWire data)
        {
            if(!data.Valid()||data.Actor!=actor)return false;
            var v=world.GetEntity(data.Vehicle) as EntityVehicle;
            if(!Weapons.IsMecha(v)||v.GetAttached(0)==null||v.GetAttached(0).entityId!=actor||!Locomotion.Powered(v)||Boarding.Active(v)||v.IsDead())return false;
            Session session;sessions.TryGetValue(v.entityId,out session);
            if(session!=null&&session.Actor==actor&&(data.Action<session.Id||(data.Action==session.Id&&data.Tick<=session.Tick)))return false;
            if(data.Mode==0){
                if(data.Age!=0||data.Phase!=Traversal.Stage.Prepare)return false;
                if(session!=null&&session.Actor==actor&&(data.Action<=session.Id||Time.time-session.Started<.1f))return false;
                float searched;if(searches.TryGetValue(v.entityId,out searched)&&Time.time-searched<.1f)return false;searches[v.entityId]=Time.time;
                var support=GroundSupport.Observe(v);string reason="";
                if(support!=null&&v.isEntityRemote){
                    // Validate the driver's real planted anchors, which need not equal neutral home positions.
                    for(int i=0;i<2;i++){GroundSupport.Pad pad;
                        if(!GroundSupport.PadAt(v,data.Plan.Start[i],data.Plan.Rotation,.45f,.45f,out pad)||Vector3.Distance(pad.Point,data.Plan.Start[i])>.05f||!support.Shape.Reach(v.vehicleRB.position+Origin.position,data.Plan.Rotation,i,pad.Point,pad.Normal))return false;
                        GroundSupport.Plant(support,i,pad,false);
                    }support.Grounded=true;
                }
                var plan=support!=null?Traversal.Search(v,support,data.Plan.Front,out reason):null;
                bool ok=plan!=null&&!Traversal.Charging(v)&&!Flight.AirPose(Locomotion.Get(v))&&!Locomotion.Get(v).HoverOn&&Vector3.ProjectOnPlane(v.vehicleRB.velocity,Vector3.up).magnitude<=Rules.TraverseSafeSpeed+.2f;
                if(ok){
                    ok=Vector3.Distance(plan.Root,data.Plan.Root)<=.15f&&Vector3.Distance(plan.End,data.Plan.End)<=.15f&&plan.Type==data.Plan.Type&&Mathf.Abs(plan.Duration-data.Plan.Duration)<.001f&&Mathf.Abs(plan.Height-data.Plan.Height)<.02f&&Mathf.Abs(plan.Width-data.Plan.Width)<.025f;
                    for(int i=0;i<2;i++)ok&=Vector3.Distance(plan.Start[i],data.Plan.Start[i])<=.08f&&Vector3.Distance(plan.Land[i].Point,data.Plan.Land[i].Point)<=.05f&&Vector3.Distance(plan.StartNormal[i],data.Plan.StartNormal[i])<=.02f&&Vector3.Distance(plan.Land[i].Normal,data.Plan.Land[i].Normal)<=.02f;
                    if(ok)ok=Traversal.ValidatePath(v,support,data.Plan,out reason);
                }
                if(!ok){data.Mode=3;Broadcast(data);if(!v.isEntityRemote)Traversal.Cancel(v,string.IsNullOrEmpty(reason)?"服务器拒绝越障":reason);return false;}
                session=new Session{Actor=actor,Id=data.Action,Tick=data.Tick,Started=Time.time,Seen=Time.time,Plan=data.Plan,Phase=data.Phase};sessions[v.entityId]=session;
                var local=Traversal.Get(v);if(!v.isEntityRemote){local.Accepted=true;local.LastPacket=Time.time;}
            }
            else {
                if(session==null||session.Actor!=actor||session.Id!=data.Action||Time.time-session.Seen>1)return false;
                if(data.Mode==1&&(!Matches(session.Plan,data.Plan)||data.Age<session.Age||data.Age>Time.time-session.Started+.5f||data.Phase<session.Phase))return false;
                if(data.Mode==1){string reason;var support=GroundSupport.Get(v);
                    if(!Traversal.ValidateSegment(v,support,session.Plan,session.Age,data.Age,out reason)){data.Mode=3;session.Plan=null;Broadcast(data);if(!v.isEntityRemote)Traversal.Cancel(v,reason);return false;}}
                session.Tick=data.Tick;session.Seen=Time.time;session.Age=data.Age;session.Phase=data.Phase;
                if(data.Mode==2)session.Plan=null;
            }
            if(v.isEntityRemote)Replay(v,data);
            Broadcast(data);return true;
        }
        static void Broadcast(TraverseWire data)
        {if(ConnectionManager.Instance!=null)ConnectionManager.Instance.SendPackage(NetPackageManager.GetPackage<NetPackagePZAECMechaTraverseEvent>().Setup(data),false,-1,-1,data.Vehicle,null,512);}
        public static bool ClientReceive(World world,TraverseWire data)
        {
            if(!data.Valid())return false;var v=world.GetEntity(data.Vehicle) as EntityVehicle;if(!Weapons.IsMecha(v))return false;
            var driver=v.GetAttached(0);if(driver==null||driver.entityId!=data.Actor)return false;
            var s=Traversal.Get(v);
            if(s.Actor==data.Actor&&(data.Action<s.ReceivedId||(data.Action==s.ReceivedId&&data.Tick<=s.ReceivedTick)))return false;
            s.ReceivedId=data.Action;s.ReceivedTick=data.Tick;s.LastPacket=Time.time;
            if(!v.isEntityRemote){if(s.Current==null||s.Current.Id!=data.Action)return false;if(data.Mode>=2){Traversal.Cancel(v,data.Mode==3?"服务器拒绝越障":"服务器结束越障");}else s.Accepted=true;return true;}
            Replay(v,data);return true;
        }
        static void Replay(EntityVehicle v,TraverseWire data)
        {
            var s=Traversal.Get(v);bool same=s.Actor==data.Actor&&s.Current!=null&&s.Current.Id==data.Action;var previous=s.DeliveredPhase;
            s.Actor=data.Actor;s.Remote=true;s.LastPacket=Time.time;s.Accepted=true;
            s.NextId=Math.Max(s.NextId,data.Action);s.Tick=Math.Max(s.Tick,data.Tick);s.DeliveredPhase=data.Phase;
            if(data.Mode>=2){s.Current=null;s.Phase=Traversal.Stage.Exit;return;}
            s.Current=data.Plan;s.Age=data.Age;s.Phase=data.Phase;s.Current.FrameInto(s.Age,s.FrameFeet,out s.TargetRoot);
            Locomotion.Get(v).Grounded=true;
            // A mid-action join is silent; only newly observed transitions emit contacts.
            if(same&&previous<data.Phase){
                int side=data.Phase>=Traversal.Stage.Settle?1-data.Plan.Front:data.Phase>=Traversal.Stage.Transfer?data.Plan.Front:-1;
                if(side>=0)RobotAudio.ContactEvent(v,side==0?"step-left":"step-right",RobotAudio.NextPresentationSerial(),data.Plan.Land[side].Point,.85f);
            }
        }
        public static void Tick(World world)
        {
            foreach(var pair in sessions){var s=pair.Value;if(s.Plan==null)continue;var v=world.GetEntity(pair.Key) as EntityVehicle;
                if(v==null||v.GetAttached(0)==null||v.GetAttached(0).entityId!=s.Actor||Time.time-s.Seen>1||v.IsDead()){
                    s.Plan=null;Broadcast(new TraverseWire{Vehicle=pair.Key,Actor=s.Actor,Action=s.Id,Tick=++s.Tick,Mode=2,Phase=Traversal.Stage.Exit,Age=s.Age});if(v!=null){var state=Traversal.Get(v);if(state.Current!=null)Traversal.Cancel(v,"联机越障中断");}
                }
            }
        }
        public static void Forget(EntityVehicle v){sessions.Remove(v.entityId);searches.Remove(v.entityId);}
        public static void Clear(){sessions.Clear();searches.Clear();}
    }
}
