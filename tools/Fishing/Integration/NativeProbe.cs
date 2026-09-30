using System;
using System.Linq;
using UnityEngine;
using PZAEC.Fishing.Contracts;
using PZAEC.Fishing.Runtime;

// Only loaded in the disposable world created by Start-NativeProbe.ps1.
public sealed class FishingNativeProbe : IModApi
{
    static World world;static bool waiting,finished;static float deadline;
    static int checks;
    public void InitMod(Mod mod)
    {
        if(!Environment.GetCommandLineArgs().Any(a=>string.Equals(a,"-pzaecFishingM0",StringComparison.OrdinalIgnoreCase)) && GamePrefs.GetString(EnumGamePrefs.GameName)!="FishingM0_Isolated")return;
        ModEvents.GameStartDone.RegisterHandler(Ready);ModEvents.GameUpdate.RegisterHandler(Tick);
        Log.Out("[FishingM0] PROBE LOADED");
    }
    static void Check(bool ok,string message){if(!ok)throw new Exception(message);checks++;Log.Out("[FishingM0] PASS "+message);}
    static void Ready(ref ModEvents.SGameStartDoneData data)
    {
        if(GamePrefs.GetString(EnumGamePrefs.GameName)!="FishingM0_Isolated")return;
        world=GameManager.Instance.World;GameManager.Instance.AddChunkObserver(new Vector3(8,200,8),false,2,-1);
        deadline=Time.realtimeSinceStartup+90;waiting=true;
    }
    static void Tick(ref ModEvents.SGameUpdateData data)
    {
        if(!waiting||finished)return;
        try
        {
            var chunk=world.GetChunkFromWorldPos(new Vector3i(8,200,8)) as Chunk;
            if(chunk==null){if(Time.realtimeSinceStartup>deadline)throw new Exception("chunk load timeout");return;}
            finished=true;
            Check(new WaterValue(19500).HasMass(),"native full water has mass");
            Check(Math.Abs(new WaterValue(19500).GetMassPercent()-1)<.0001,"native water mass normalization");
            var old=new WaterValue[8];for(int y=197;y<=204;y++)old[y-197]=chunk.GetWater(8,y,8);
            try
            {
                for(int y=197;y<=204;y++)chunk.SetWaterRaw(8,y,8,WaterValue.Empty);
                for(int y=200;y<=202;y++)chunk.SetWaterRaw(8,y,8,WaterValue.Full);
                var query=new NativeWaterQuery(world);
                var sample=query.SampleColumn(new Vec3(8.5f,203,8.5f),1,6);
                Check(sample.IsValid,"water query finds real loaded world column");
                Check(Math.Abs(sample.SurfacePoint.Y-203)<.0001&&Math.Abs(sample.DepthMeters-3)<.0001&&sample.BottomKnown,"water column top/depth = 203/3 meters");
                Check(sample.Accuracy==SurfaceAccuracy.VoxelEstimate,"water accuracy explicitly voxel estimate");
                Check(query.SampleColumn(new Vec3(100000,200,100000),1,2).Status==WaterSampleStatus.Unloaded,"unloaded world column fails closed");
                Check(query.SampleColumn(new Vec3(8,201,8),0,2).Status==WaterSampleStatus.OutOfRange,"truncated underwater column rejected");
                Check(query.TraceSolid(new Vec3(8.5f,201,8.5f),new Vec3(8.5f,202,8.5f)).Obstructed==false,"water is not a solid line obstacle");
            }
            finally{for(int y=197;y<=204;y++)chunk.SetWaterRaw(8,y,8,old[y-197]);}

            // Native component fixture: deliberately inactive, so no player Awake/UI is fabricated.
            // This tests lease and native DTO behavior, not a fully spawned interactive player.
            var holder=new GameObject("FishingM0-InactiveLocalPlayerFixture");holder.SetActive(false);
            try
            {
                var player=holder.AddComponent<EntityPlayerLocal>();player.movementInput=new MovementInput();
                player.movementInput.rotation=new Vector3(10,0,0);
                var lease=new NativeControlLease();lease.Acquire(player);lease.BeginFrame();
                using(var hooks=new NativeInputHooks(lease))
                {
                    var info=HarmonyLib.Harmony.GetPatchInfo(HarmonyLib.AccessTools.Method(typeof(EntityPlayerLocal),"MoveByInput"));
                    Check(info!=null&&info.Owners.Contains("pzaec.fishing.input.v1"),"Harmony installs on native MoveByInput");
                }
                lease.Acquire(player);lease.BeginFrame();
                player.movementInput.rotation=new Vector3(50,20,0);
                lease.Request=new MovementRequest{Active=true,ExtraForward=-.8f,AgainstPullScale=.25f,PullDirection=new Vec3(0,0,1)};
                lease.ApplyBeforeMove(player);
                Check(player.movementInput.rotation==new Vector3(10,0,0),"lease restores rod-owned look rotation");
                Check(Math.Abs(player.movementInput.moveForward+.2f)<.0001,"native movement input receives resisted backstep");
                lease.ApplyBeforeMove(player);Check(Math.Abs(player.movementInput.moveForward+.2f)<.0001,"backstep applied only once per native frame");
                lease.Release();Check(!lease.Active&&!lease.Request.Active,"native lease releases without global input lock");
                Check(Math.Abs(player.movementInput.moveForward)<.0001,"lease release restores native movement axes");

                player.Buffs=new EntityBuffs(player);player.bag=new Bag(1);
                var saved=new NativeCatchRecord(player,"isolated");
                var catchId=Guid.NewGuid();
                var result=new CatchResult {SessionId=catchId,SettlementId=catchId,PlayerPersistentId="isolated",
                    FishDefinitionId=FishingContract.FishDefinition,MassKg=3,TerminalTick=1234567890123L};
                saved.Write(result,false);
                Check(saved.Result.SessionId==catchId&&saved.Result.TerminalTick==result.TerminalTick&&!saved.Completed,"native catch record preserves GUID and 64-bit tick");
                using(var bytes=new System.IO.MemoryStream()) {
                    var writer=new System.IO.BinaryWriter(bytes);player.Buffs.Write(writer,false);writer.Flush();bytes.Position=0;
                    player.Buffs=new EntityBuffs(player);player.Buffs.Read(new System.IO.BinaryReader(bytes));
                }
                Check(saved.Result.SessionId==catchId&&saved.Result.MassKg==3&&!saved.Completed,"pending catch survives native buff binary serialization");
                player.bag.AddItem(new ItemStack(ItemClass.GetItem(FishingContract.BaitItem),1));
                Check(NativeCatchInventory.ConsumeBait(player)&&player.bag.GetItemCount(ItemClass.GetItem(FishingContract.BaitItem))==0,"accepted bait removal uses native bag");
                Check(!NativeCatchInventory.ConsumeBait(player),"empty inventory does not consume phantom bait");
                var reward=new RewardSpec {ItemId=FishingContract.FishItem,Count=1,FishMassKg=3};
                var rewards=new NativeCatchInventory(player);
                Check(rewards.TryAdd(reward,catchId),"native fish reward enters isolated bag");
                string receivedId;float receivedMass;
                Check(player.bag.items[0].itemValue.TryGetMetadata("pzaecFishingSettlement",out receivedId)&&receivedId==catchId.ToString("N")&&
                    player.bag.items[0].itemValue.TryGetMetadata("pzaecFishingMassKg",out receivedMass)&&receivedMass==3,"fish carries settlement identity and mass");
                Check(!rewards.TryAdd(reward,Guid.NewGuid()),"full native bag refuses another distinct catch");
                saved.Write(result,true);
                using(var bytes=new System.IO.MemoryStream()) {
                    var writer=new System.IO.BinaryWriter(bytes);player.Buffs.Write(writer,false);writer.Flush();bytes.Position=0;
                    player.Buffs=new EntityBuffs(player);player.Buffs.Read(new System.IO.BinaryReader(bytes));
                }
                Check(saved.Completed&&saved.Result.SessionId==catchId,"completed catch survives native buff binary serialization");
            }
            finally{UnityEngine.Object.Destroy(holder);}

            var visual=new GameObject("FishingM0-LineProbe");
            try
            {
                var line=visual.AddComponent<LineRenderer>();line.positionCount=2;line.useWorldSpace=true;
                line.SetPosition(0,NativeCoordinates.ToScene(new Vec3(8,204,8)));line.SetPosition(1,NativeCoordinates.ToScene(new Vec3(8,203,8)));
                Check(Vector3.Distance(line.GetPosition(0),line.GetPosition(1))>.99f,"Unity line geometry and floating origin conversion");
            }
            finally{UnityEngine.Object.Destroy(visual);}
            var directory=System.IO.Path.GetFullPath(System.IO.Path.Combine(Application.dataPath,"../Mods/ZZZ-PZAEC_Fishing"));
            var localPrefab=LoadManager.LoadAsset<GameObject>("Prefabs/prefabEntityPlayerLocal",null,null,false,true,false);
            try {
                Check(localPrefab.Asset!=null,"native first-person player prefab loads for arm inspection");
                var prefabBones=localPrefab.Asset.GetComponentsInChildren<Transform>(true);
                Log.Out("[FishingM0] Local prefab hierarchy="+string.Join(",",prefabBones.Select(t=>t.name).ToArray()));
                foreach(var rig in prefabBones.Where(t=>t.name.ToLowerInvariant().Contains("arms")||t.name=="baseRigFP")) {
                    var candidates=rig.GetComponentsInChildren<Transform>(true);
                    Log.Out("[FishingM0] Native rig "+rig.name+" bones="+string.Join(",",candidates.Select(t=>t.name).ToArray()));
                    Check(NativeArmPose.FindLowerArm(candidates,true)!=null&&NativeArmPose.FindLowerArm(candidates,false)!=null,"actual "+rig.name+" lower arms resolve");
                }
            } finally {localPrefab.Release();}
            var nativeRig=LoadManager.LoadAsset<GameObject>("@:Entities/Player/Common/BaseRigs/baseRigFPPrefab.prefab",null,null,false,true,false);
            try {
                Check(nativeRig.Asset!=null,"actual SDCS first-person base rig loads");
                var rigBones=nativeRig.Asset.GetComponentsInChildren<Transform>(true);
                Log.Out("[FishingM0] SDCS FP bones="+string.Join(",",rigBones.Select(t=>t.name).ToArray()));
                Check(NativeArmPose.FindLowerArm(rigBones,true)!=null&&NativeArmPose.FindLowerArm(rigBones,false)!=null,"actual SDCS first-person lower arms resolve");
            } finally {nativeRig.Release();}
            var projectionFixture=new GameObject("FishingHandProjectionFixture");
            try {
                var camera=projectionFixture.AddComponent<Camera>();camera.enabled=false;
                camera.aspect=16f/9;camera.fieldOfView=85;
                camera.transform.SetPositionAndRotation(new Vector3(13,7,-21),Quaternion.Euler(12,37,0));
                var hand=camera.transform.TransformPoint(new Vector3(.2f,-.15f,.6f));
                var mapped=NativeHandProjection.ToWorld(camera,55,hand);
                var worldPixel=camera.WorldToViewportPoint(mapped);
                camera.fieldOfView=55;var handPixel=camera.WorldToViewportPoint(hand);
                Check(Vector2.Distance(new Vector2(worldPixel.x,worldPixel.y),new Vector2(handPixel.x,handPixel.y))<.00001f,"FP hand and world rod have matching screen projection across different FOVs");
                Check(Math.Abs(worldPixel.z-handPixel.z)<.00001f,"hand reprojection preserves depth");
                Check(Vector3.Distance(NativeHandProjection.ToWorld(camera,55,hand),hand)<.00001f,"equal FOV leaves hand anchor unchanged");
            } finally {UnityEngine.Object.Destroy(projectionFixture);}
            var spear=ItemClass.GetItem("meleeWpnSpearT0StoneSpear").ItemClass.CloneModel(world,ItemClass.GetItem("meleeWpnSpearT0StoneSpear"),Vector3.zero,null,BlockShape.MeshPurpose.Hold,default(TextureFullArray));
            if(spear!=null) {
                foreach(var renderer in spear.GetComponentsInChildren<Renderer>(true))Log.Out("[FishingM0] Spear renderer="+renderer.name+" type="+renderer.GetType().Name);
                foreach(var animator in spear.GetComponentsInChildren<Animator>(true))Log.Out("[FishingM0] Spear animator="+animator.name+" controller="+animator.runtimeAnimatorController);
                UnityEngine.Object.Destroy(spear.gameObject);
            }
            var handMount=GameObject.CreatePrimitive(PrimitiveType.Cube);handMount.name="FishingNativeHandFixture";handMount.transform.position=new Vector3(1,3,4);
            var armFixture=new GameObject("FishingArmFixture");
            var wristFixture=new GameObject("FishingWristFixture");wristFixture.transform.SetParent(armFixture.transform,false);wristFixture.transform.localPosition=Vector3.forward*.3f;
            using(var pose=new NativeArmPose()) {
                pose.Apply(armFixture.transform,null,Vector3.forward,new Vector3(0,1,1));
                Check(wristFixture.transform.position.y>.1f,"pole pose rotates actual arm hierarchy including hand");
                var firstPose=armFixture.transform.rotation;
                pose.Apply(armFixture.transform,null,Vector3.forward,new Vector3(0,1,1));
                Check(Quaternion.Angle(firstPose,armFixture.transform.rotation)<.001f,"arm pose does not accumulate across frames");
                pose.Restore();Check(Quaternion.Angle(armFixture.transform.rotation,Quaternion.identity)<.001f,"arm pose restores native animation on release");
            }
            UnityEngine.Object.Destroy(armFixture);
            var mountRenderer=handMount.GetComponent<Renderer>();
            using(var held=new NativeHeldRod {ModDirectory=directory}) {
                var config=new PZAEC.Fishing.Content.FishingContent().Load(directory);
                held.Update(handMount.transform,Vector3.forward,config,false);
                Check(held.VisibleGripScene.HasValue&&Vector3.Distance(held.VisibleGripScene.Value,handMount.transform.position)<.0001f&&!mountRenderer.enabled,"idle real rod replaces placeholder and grip matches native mount");
                handMount.transform.position+=new Vector3(2,1,-1);
                held.Update(handMount.transform,new Vector3(.5f,.3f,1),config,false);
                Check(Vector3.Distance(held.VisibleGripScene.Value,handMount.transform.position)<.0001f,"idle grip follows mount translation and rod rotation");
                held.Update(handMount.transform,Vector3.forward,config,true);
                Check(!held.VisibleGripScene.HasValue&&held.HandScene.HasValue&&!mountRenderer.enabled,"fishing hides idle copy while retaining native hand anchor");
            }
            Check(mountRenderer.enabled,"unequipping restores original renderer state");
            using(var view=new PZAEC.Fishing.Presentation.FishingPresentation(directory,options:new PZAEC.Fishing.Presentation.PresentationOptions {AudioEnabled=false,CameraFeedbackEnabled=false,RightHandScene=()=>handMount.transform.position})) {
                var id=Guid.NewGuid();var config=new PZAEC.Fishing.Content.FishingContent().Load(directory);
                view.Begin(new SessionStart {SessionId=id},config);
                var state=new FishingSnapshot {SessionId=id,Phase=FishingPhase.Waiting,LineLengthMeters=8,
                    Rod=new RodPose {Root=new Vec3(8,204,8),Tip=new Vec3(8,204.5f,10),Forward=new Vec3(0,0,1),Right=new Vec3(1,0,0)},
                    FloatPosition=new Vec3(8,203,15),FloatUp=Vec3.Up,FishPosition=new Vec3(8,202,15),FishForward=new Vec3(0,0,1)};
                view.Render(new RenderFrame {Previous=state,Current=state,Alpha=1,RenderOrigin=NativeCoordinates.FromUnity(Origin.position),IsLocalPlayer=true});
                Check(view.IsReady&&view.LastError==null,"shipped presentation bundle instantiates in native game engine");
                Check(view.RightGrip.root.GetComponentsInChildren<Transform>(true).All(t=>t.name!="Crank"&&t.name!="Spool"),"pole presentation has no reel or crank");
                Check(Vector3.Distance(view.RightGrip.position,handMount.transform.position)<.0001f,"fishing rod grip matches native mount instead of estimated body position");
                view.Clear();Check(!view.IsReady,"native presentation clears its objects");
            }
            UnityEngine.Object.Destroy(handMount);
            var item=ItemClass.GetItem("resourceWood");item.SetMetadata("pzaecFishingProbe",3.25f);
            float metadata;Check(item.TryGetMetadata("pzaecFishingProbe",out metadata)&&Math.Abs(metadata-3.25f)<.0001,"native item metadata round trip in memory");
            var bag=new Bag(1);var stack=new ItemStack(ItemClass.GetItem("resourceWood"),1);
            Check(bag.CanTakeItem(stack)&&bag.AddItem(stack),"native isolated bag capacity and insertion");
            Log.Out("[FishingM0] RESULT PASS checks="+checks+"; interactive-player/visual-pixels/full-player-save-roundtrip NOT tested");
        }
        catch(Exception e){finished=true;Log.Error("[FishingM0] RESULT FAIL "+e);}
    }
}
