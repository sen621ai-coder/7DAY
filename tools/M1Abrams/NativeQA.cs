using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
using PZAEC.M1;

// Opt-in isolated native fixture, never installed into the live Mods directory.
public sealed class M1NativeQA:IModApi
{
    static readonly List<string> results=new List<string>();
    static readonly List<Entity> entities=new List<Entity>();static int serial;
    static void Check(bool ok,string label){if(!ok)throw new Exception(label);results.Add("PASS "+label);}
    public void InitMod(Mod mod){if(Environment.GetCommandLineArgs().Contains("-m1NativeQA"))ModEvents.GameStartDone.RegisterHandler(Run);}
    static T Spawn<T>(World world,string name,Vector3 position) where T:Entity
    {var e=EntityFactory.CreateEntity(EntityClass.FromString(name),position) as T;if(e==null)throw new Exception("factory "+name);entities.Add(e);world.SpawnEntityInWorld(e);return e;}
    static void Intent(World w,EntityVehicle vehicle,EntityPlayer player,byte mode,bool fire,bool zoom,Vector3 point)
    {
        var origin=vehicle.position+Vector3.up*3;
        Secondary.Request(w,player.entityId,new NetPackageM1SecondaryIntent{Vehicle=vehicle.entityId,Serial=++serial,Select=mode,Flags=(byte)((fire?1:0)|(zoom?2:0)),Origin=origin,Direction=(point-origin).normalized});
    }
    static void Run(ref ModEvents.SGameStartDoneData data)
    {
        if(GamePrefs.GetString(EnumGamePrefs.GameName)!="M1QA_Isolated")return;
        var world=GameManager.Instance.World;string report=Path.Combine(GameIO.GetSaveGameDir(),"m1-native-report.txt");
        try{
            Check(Weapons.Server,"native server authority");
            var apachePath=Environment.GetCommandLineArgs().FirstOrDefault(a=>a.StartsWith("-m1ApacheDll="));
            if(apachePath!=null){
                var assembly=System.Reflection.Assembly.LoadFrom(apachePath.Substring("-m1ApacheDll=".Length));var type=assembly.GetType("AECT16RuntimeFix.ApacheArmor",true);
                new Harmony("pzaec.apache.armor.v1").Patch(AccessTools.Method(typeof(EntityVehicle),"ApplyDamage"),transpiler:new HarmonyMethod(AccessTools.Method(type,"ThresholdTranspiler")));
                Check(Harmony.GetPatchInfo(AccessTools.Method(typeof(EntityVehicle),"ApplyDamage")).Transpilers.Count>=2,"real Apache and M1 threshold transpilers coexist");
            }
            var prefab=(Transform)AccessTools.Method(typeof(Model),"GetPrefab").Invoke(null,null);
            Check(prefab!=null,"native jeep-derived M1 prefab loads");
            var root=SecondaryModel.Find(prefab,"M1Visual");var lod=root.GetComponent<LODGroup>();Check(lod.GetLODs().Length==3,"three native LOD levels");
            var curb=new GameObject("M1QACurbProbe");var curbBox=curb.AddComponent<BoxCollider>();curbBox.size=new Vector3(4,.2f,.2f);
            try{
                foreach(string side in new[]{"L","R"}){
                    var sourceTrack=SecondaryModel.Find(prefab,"M1TrackContact"+side).GetComponent<MeshCollider>();
                    Check(sourceTrack.enabled&&sourceTrack.convex&&!sourceTrack.isTrigger,"solid convex track support "+side);
                    // The cached vehicle is inactive. Cook an active collider
                    // from its exact mesh before querying native penetration.
                    var probe=new GameObject("M1QAActiveTrack"+side);probe.transform.SetParent(curb.transform,false);
                    var track=probe.AddComponent<MeshCollider>();track.sharedMesh=sourceTrack.sharedMesh;track.convex=true;Physics.SyncTransforms();
                    Vector3 direction;float distance;
                    Check(Physics.ComputePenetration(track,Vector3.zero,Quaternion.identity,curbBox,new Vector3(0,.1f,0),Quaternion.identity,out direction,out distance)&&distance>.01f,"narrow curb contacts middle of track "+side);
                    Check(!Physics.ComputePenetration(track,Vector3.zero,Quaternion.identity,curbBox,new Vector3(0,-.1f,0),Quaternion.identity,out direction,out distance),"track support clears level ground "+side);
                    Check(!Physics.ComputePenetration(track,Vector3.zero,Quaternion.identity,curbBox,new Vector3(0,.1f,3.2f),Quaternion.identity,out direction,out distance),"front track bevel clears low approach "+side);
                    curbBox.size=new Vector3(.4f,.2f,.2f);
                    Check(!Physics.ComputePenetration(track,Vector3.zero,Quaternion.identity,curbBox,new Vector3(0,.1f,0),Quaternion.identity,out direction,out distance),"central belly gap remains open "+side);
                    curbBox.size=new Vector3(4,.2f,.2f);
                }
            }finally{UnityEngine.Object.Destroy(curb);}
            foreach(var level in lod.GetLODs())Check(level.renderers.All(r=>r!=null&&r.sharedMaterial!=null&&r.sharedMaterial.mainTexture!=null),"native meshes retain materials and textures");
            var preview=(Transform)AccessTools.Method(typeof(Model),"BuildPreview").Invoke(null,null);
            try{
                Check(preview.Find("Physics")==null,"placement preview has no physics subtree for native cleanup");
                Vehicle.SetupPreview(preview);
                Check(preview.GetComponentsInChildren<Renderer>(true).Any(r=>r.enabled&&r.sharedMaterial!=null),"native placement preview retains visible M1 geometry");
            }finally{UnityEngine.Object.Destroy(preview.gameObject);}
            foreach(string name in new[]{"RoofMGBase","RoofMGYaw","RoofMGPitch","RoofMGMuzzle","RoofMGSight","AAMount","AAPitch","AAMuzzleL","AAMuzzleR"})Check(SecondaryModel.Find(root,name)!=null,"native anchor "+name);
            var geometry=new SecondaryModel.Geometry(root);var pivot=SecondaryModel.Find(root,"RoofMGPitch");var muzzle=SecondaryModel.Find(root,"RoofMGMuzzle");
            Check(geometry.Clear(pivot.position+Origin.position,muzzle.position+Origin.position+muzzle.forward*.15f,SecondaryRules.MG),"neutral MG clears original tank triangles");
            var aa=SecondaryModel.Find(root,"AAPitch");muzzle=SecondaryModel.Find(root,"AAMuzzleL");
            Check(geometry.Clear(aa.position+Origin.position,muzzle.position+Origin.position+muzzle.forward*2,SecondaryRules.AA),"neutral AA clears original tank triangles");
            var vehicle=Spawn<EntityVehicle>(world,"vehicleM1Abrams",new Vector3(8,150,8));results.Add("INFO entity="+vehicle.EntityClass.entityClassName+" vehicle="+vehicle.vehicle.GetName()+" nativeId="+EntityClass.FromString("vehicleM1Abrams"));vehicle.vehicle.SetItemValue(ItemClass.GetItem("vehicleM1AbramsPlaceable",false));
            Check(Weapons.IsTank(vehicle),"native vehicle name resolves to an M1 tier: "+vehicle.vehicle.GetName());
            int initialHealth=vehicle.Health;vehicle.ApplyDamage(100000);Check(!vehicle.IsDead()&&vehicle.Health==initialHealth-100000,"native six-digit damage does not trigger vehicle instant destruction");
            Check(Combat.CombinedThreshold(123456,null)==123456,"non-M1 threshold preserves prior mod result");
            var main=Weapons.Register(vehicle);var state=Secondary.Get(main);Check(state.Muzzle!=null&&state.Left!=null&&state.Right!=null,"spawned server vehicle secondary rig");
            state.Gun.Rounds=37;state.Gun.Heat=66;state.Gun.Overheated=true;state.LeftAt=Time.time+8;state.RightAt=Time.time+3;Secondary.Save(state);
            var item=vehicle.vehicle.GetUpdatedItemValue().Clone();Check(item.TryGetMetadata("m1swRounds",out int rounds)&&rounds==37,"native vehicle ItemValue retains partial belt");
            using(var stream=new MemoryStream()){item.Write(new BinaryWriter(stream));stream.Position=0;var restored=new ItemValue();restored.Read(new BinaryReader(stream));Check(restored.TryGetMetadata("m1swRounds",out int savedRounds)&&savedRounds==37&&restored.TryGetMetadata("m1swHeat",out int heat)&&heat==66000,"native ItemValue binary save/read retains ammunition and heat");Check(restored.TryGetMetadata("m1swLeft",out int left)&&left>=7900&&restored.TryGetMetadata("m1swRight",out int right)&&right>=2900,"native binary save/read retains independent cooldowns");}
            Secondary.States.Remove(vehicle.entityId);state=Secondary.Get(main);Check(state.Gun.Rounds==37&&Math.Abs(state.Gun.Heat-66)<.001f&&state.Gun.Overheated,"reload state restores heat and ammunition");Check(state.LeftAt>Time.time+7.9f&&state.RightAt>Time.time+2.9f,"independent tube cooldown restored");
            for(int tier=1;tier<4;tier++){var other=Spawn<EntityVehicle>(world,"vehicleM1AbramsT"+(16+tier),vehicle.position+Vector3.right*(tier*20));Check(Weapons.Tier(other)==tier,"native lowercase tier "+(16+tier));Check(other.vehicle.GetMaxHealth()==Rules.Specs[tier].Health,"native tier health "+(16+tier));Check(Secondary.Get(Weapons.Register(other)).Left!=null,"native tier AA rig "+(16+tier));}
            var player=Spawn<EntityPlayer>(world,"playerMale",vehicle.position);player.MinEventContext.ItemValue=ItemValue.None;vehicle.AttachEntityToSelf(player,0);Check(Weapons.Seat(vehicle,player.entityId)==0,"native driver attachment");
            var targetPoint=state.Muzzle.position+Origin.position+state.Muzzle.forward*100;
            Intent(world,vehicle,player,1,false,false,targetPoint);state.Inputs[0].SwitchUntil=Time.time-1;
            state.Gun.Rounds=10;state.Gun.Heat=0;state.Gun.Overheated=false;state.Gun.NextShot=0;
            Intent(world,vehicle,player,1,true,false,targetPoint);Physics.SyncTransforms();Secondary.Update(main,.1f);
            results.Add("INFO MG reason="+state.MGReason+" rounds="+state.Gun.Rounds);
            Check(state.Gun.Rounds==9,"actual native MG firing consumes exactly one loaded round");
            Secondary.Update(main,0);Check(state.Gun.Rounds==9,"same-time update cannot duplicate MG shot");Check(main.LastWeaponActivity==Time.time&&main.LastShot<0,"MG activity does not animate cannon recoil");
            var gunner=Spawn<EntityPlayer>(world,"playerMale",vehicle.position);gunner.MinEventContext.ItemValue=ItemValue.None;vehicle.AttachEntityToSelf(gunner,1);Check(Weapons.Seat(vehicle,gunner.entityId)==1,"native gunner attachment");
            Intent(world,vehicle,player,0,false,false,targetPoint);Check(state.Inputs[0].Mode==1,"driver cannot steal cannon with gunner present");
            Intent(world,vehicle,gunner,1,false,false,targetPoint);Check(state.Inputs[1].Mode==0,"gunner cannot steal driver MG");
            var bird=Spawn<EntityVulture>(world,"animalZombieVulture",vehicle.position+new Vector3(0,45,100));bird.Stats.Health.BaseMax=100000000;bird.Health=100000000;Physics.SyncTransforms();
            vehicle.bag.AddItem(new ItemStack(ItemClass.GetItem(SecondaryRules.Missile,false),2));state.LeftAt=state.RightAt=state.GlobalAt=0;
            Intent(world,vehicle,gunner,2,false,true,bird.position+Vector3.up*.8f);state.Inputs[1].SwitchUntil=Time.time-1;
            Secondary.Update(main,1.5f);results.Add("INFO AA reason="+state.AAReason+" target="+state.Target+" lock="+state.Lock);
            Check(state.Target==bird.entityId&&state.Lock>=1.5f,"native AA acquires live flying enemy");
            Intent(world,vehicle,gunner,2,true,true,bird.position+Vector3.up*.8f);Secondary.Update(main,.1f);
            Check(vehicle.bag.GetItemCount(ItemClass.GetItem(SecondaryRules.Missile,false))==1,"native AA shot consumes one missile");Check(state.LeftAt>Time.time+11.9f&&state.RightAt<=Time.time,"only fired tube begins cooldown");
            Intent(world,vehicle,gunner,0,false,false,bird.position);int hp=bird.Health;for(int i=0;i<100;i++)Secondary.Advance(.01f);
            Check(bird.Health<hp,"native guided missile still hits after switching to cannon");
            int playerHealth=player.Health;Combat.SecondaryHit(player,gunner.entityId,5000000,true,Vector3.forward,player.position);Check(player.Health==playerHealth,"secondary damage refuses player target");
            results.Add("FINISHED failures=0");
        }catch(Exception e){results.Add("FAIL "+e);}
        finally{File.WriteAllLines(report,results);foreach(var line in results)Log.Out("[M1NativeQA] "+line);foreach(var e in entities)if(world.GetEntity(e.entityId)!=null)world.RemoveEntity(e.entityId,EnumRemoveEntityReason.Despawned);Application.Quit();}
    }
}
