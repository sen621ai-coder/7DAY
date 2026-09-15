using System;
using UnityEngine;

namespace SakuraPreview
{
    // Native NPC carrier supplies collision, gravity, save records and entity replication.
    public sealed class EntitySakura : EntityNPC
    {
        readonly SakuraInteractionState interaction=new SakuraInteractionState();
        public int Leader {get=>interaction.Leader;set=>interaction.Leader=value;}
        public float GestureUntil;
        public bool IsGuardian => EntityClass.GetEntityClassName(entityClass)=="mintGuardian";
        public override string LocalizedEntityName => IsGuardian?"Mint":"小樱";
        public override int DamageEntity(DamageSource source,int strength,bool critical,float impulseScale)
        {
            if(SakuraFriendlyProtection.IsFriendlyDamage(this,source))return 0;
            return base.DamageEntity(source,strength,critical,impulseScale);
        }
        public override bool IsSavedToFile() => true;
        public override bool CanBePushed() => true;
        public override bool isEntityStatic() => false;
        public override void PostInit()
        {
            base.PostInit();
            RootMotion = false;
            if (!GameManager.IsDedicatedServer) gameObject.AddComponent<SakuraVisual>().Owner = this;
        }
        public override void InitLocalActivationCommands(Action<EntityActivationCommand> add)
        { add(new EntityActivationCommand("talk", "talk", null, null)); }
        public override bool AllowActivationCommand(ReadOnlySpan<char> command, EntityPlayerLocal player)
        { return !IsDead() && player != null && !player.IsDead() && (position-player.position).sqrMagnitude <= 16; }
        public override void OnEntityActivated(EntityActivationCommand command, EntityPlayerLocal player)
        {
            if (!AllowActivationCommand(command.commandId.AsSpan(), player))return;
            Log.Out("[SakuraPreview] Interaction activated: entity="+entityId+" command="+command.commandId);
            try{SakuraDialog.Open(this, player);}
            catch(Exception ex){Log.Error("[SakuraPreview] Dialogue open failed: "+ex);}
        }
        float nextFollowPath, progressCheck;
        Vector3 lastFollowGoal, lastProgressPosition;
        int pathLeader=-1;
        bool following;
        void StopFollowing()
        {
            if(following)
            {
                GamePath.PathFinderThread.Instance?.RemovePathsFor(entityId);
                navigator?.clearPath();
                moveHelper?.Stop();
            }
            following=false;pathLeader=-1;
            SetMoveForward(0);
        }
        public override void updateTasks()
        {
            if(world.IsRemote())return;
            var player=world.GetEntity(Leader) as EntityPlayer;
            if(IsGuardian || IsDead() || player==null || player.IsDead() || (player.position-position).sqrMagnitude>3600)
            {Leader=-1;StopFollowing();return;}
            var delta=player.position-position;
            if(delta.sqrMagnitude<6.25f)
            {
                StopFollowing();
                SetLookPosition(player.position+Vector3.up);
                if(delta.x*delta.x+delta.z*delta.z>.01f)
                    SeekYaw(Mathf.Atan2(delta.x,delta.z)*Mathf.Rad2Deg,0,15);
                lookHelper?.onUpdateLook();
                return;
            }
            var finder=GamePath.PathFinderThread.Instance;
            if(finder==null || navigator==null || moveHelper==null){SetMoveForward(0);return;}
            float now=Time.realtimeSinceStartup;
            if(!following || pathLeader!=Leader)
            {
                finder.RemovePathsFor(entityId);navigator.clearPath();moveHelper.Stop();
                following=true;pathLeader=Leader;nextFollowPath=0;
                progressCheck=now+3;lastProgressPosition=position;
            }
            // Match EntityAlive's native path-consumption / navigation / move order,
            // without enabling the inherited trader combat tasks.
            var result=finder.GetPath(entityId);
            if(result!=null)
            {
                if(result.path!=null)navigator.SetPath(result,result.speed);
                else {navigator.clearPath();moveHelper.Stop();}
            }
            if(now>=progressCheck)
            {
                var moved=position-lastProgressPosition;moved.y=0;
                if(moved.sqrMagnitude<.16f)
                {
                    navigator.clearPath();moveHelper.Stop();moveHelper.ResetStuckCheck();
                    nextFollowPath=0;
                }
                lastProgressPosition=position;progressCheck=now+3;
            }
            if(now>=nextFollowPath && !finder.IsCalculatingPath(entityId) &&
                (navigator.noPath() || (player.position-lastFollowGoal).sqrMagnitude>2.25f))
            {
                nextFollowPath=now+1;lastFollowGoal=player.position;
                FindPath(lastFollowGoal,GetMoveSpeed(),false,null);
            }
            navigator.UpdateNavigation();
            // Without a route, wait for a new solution instead of walking into a wall.
            if(navigator.noPath()) {moveHelper.Stop();SetMoveForward(0);}
            else moveHelper.UpdateMoveHelper();
            lookHelper?.onUpdateLook();
        }
        public byte Handle(EntityPlayer player, byte action)
        {
            if(player==null)return 255;
            if(IsGuardian && (action==2||action==3))return 6;
            if((action>=16&&action<=23) || (action==2||action==3)&&SakuraMissionServer.Controls(entityId))
                return SakuraMissionServer.Command(this,player,action);
            byte response=interaction.Handle(!world.IsRemote(),!player.IsDead()&&!IsDead(),player.entityId,(player.position-position).sqrMagnitude,action,Time.realtimeSinceStartup);
            if(response>=5)return response;
            GestureUntil=Time.realtimeSinceStartup+3;
            return action;
        }
    }

    // Request has no claimed player id. The server uses the authenticated connection.
    public sealed class NetPackageSakuraRequest : NetPackage
    {
        int entity; byte action;
        public NetPackageSakuraRequest Setup(int id, byte choice) {entity=id;action=choice;return this;}
        public override void read(PooledBinaryReader r) {entity=r.ReadInt32();action=r.ReadByte();}
        public override void write(PooledBinaryWriter w) {base.write(w);w.Write(entity);w.Write(action);}
        public override int GetLength() => 7;
        public override void ProcessPackage(World world, GameManager manager)
        {
            if (world==null || world.IsRemote() || Sender==null || !Sender.bAttachedToEntity) return;
            var npc=world.GetEntity(entity) as EntitySakura;
            if (npc==null) return;
            byte reply=npc.Handle(world.GetEntity(Sender.entityId) as EntityPlayer,action);
            if(reply==255)return;
            Sender.SendPackage(NetPackageManager.GetPackage<NetPackageSakuraReply>().Setup(entity,reply,false));
            if(reply<5 || reply==16) ConnectionManager.Instance.SendPackage(NetPackageManager.GetPackage<NetPackageSakuraReply>().Setup(entity,reply,true),_entitiesInRangeOfEntity:entity);
        }
    }
    public sealed class NetPackageSakuraReply : NetPackage
    {
        int entity; byte reply; bool gesture;
        public NetPackageSakuraReply Setup(int id,byte response,bool wave){entity=id;reply=response;gesture=wave;return this;}
        public override void read(PooledBinaryReader r){entity=r.ReadInt32();reply=r.ReadByte();gesture=r.ReadBoolean();}
        public override void write(PooledBinaryWriter w){base.write(w);w.Write(entity);w.Write(reply);w.Write(gesture);}
        public override int GetLength()=>8;
        public override void ProcessPackage(World world,GameManager manager)
        {
            if(world==null || !world.IsRemote())return;
            var npc=world.GetEntity(entity) as EntitySakura;
            if(npc==null)return;
            if(gesture)npc.GestureUntil=Time.realtimeSinceStartup+3;
            else SakuraDialog.Receive(entity,reply);
        }
    }
}


