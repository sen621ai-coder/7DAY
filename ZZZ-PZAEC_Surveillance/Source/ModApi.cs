using System;
using HarmonyLib;
using UnityEngine;

namespace PZAEC.Surveillance
{
    public sealed class ModApi : IModApi
    {
        static GameObject cache;
        static Transform screenPrefab;
        static float scanAt,broadcastAt;
        public void InitMod(Mod mod)
        {
            var harmony=new Harmony("pzaec.surveillance");
            harmony.Patch(AccessTools.Method(typeof(BlockShapeModelEntity),"getPrefab"),prefix:new HarmonyMethod(typeof(ModApi),nameof(GetPrefab)));
            harmony.Patch(AccessTools.Method(typeof(GameObjectPool),"DestroyObject",new[]{typeof(GameObject)}),prefix:new HarmonyMethod(typeof(ModApi),nameof(BeforePoolDestroy)));
            ModEvents.GameStartDone.RegisterHandler(Start);
            ModEvents.GameUpdate.RegisterHandler(Update);
            ModEvents.WorldShuttingDown.RegisterHandler(Stopping);
            ModEvents.GameShutdown.RegisterHandler(Stopped);
            Log.Out("[Surveillance] v1.0.0 wireless cameras and 4x3 live monitor loaded.");
        }
        public static bool GetPrefab(BlockShapeModelEntity __instance,ref Transform __result)
        {
            if(__instance.block==null||__instance.block.GetBlockName()!=SurveillanceState.ScreenBlock)return true;
            if(screenPrefab==null)
            {
                cache=new GameObject("SurveillanceScreenCache");cache.SetActive(false);UnityEngine.Object.DontDestroyOnLoad(cache);
                var root=new GameObject("pzaecSurveillanceScreenRuntime");root.transform.SetParent(cache.transform,false);
                try{root.AddComponent<ScreenView>().Build();screenPrefab=root.transform;}
                catch{UnityEngine.Object.Destroy(cache);cache=null;screenPrefab=null;throw;}
            }
            __result=screenPrefab;return false;
        }
        public static void BeforePoolDestroy(GameObject __0)
        {
            var view=__0==null?null:__0.GetComponent<ScreenView>();if(view==null)return;view.Retiring=true;
            foreach(var renderer in __0.GetComponentsInChildren<Renderer>(true))renderer.sharedMaterials=new Material[0];
        }
        static void Start(ref ModEvents.SGameStartDoneData data)
        {
            var world=GameManager.Instance?.World;if(world!=null&&!world.IsRemote())SurveillanceState.Start(world);
        }
        static void Update(ref ModEvents.SGameUpdateData data)
        {
            var world=GameManager.Instance?.World;
            if(world!=null&&!world.IsRemote()&&SurveillanceState.World!=world)SurveillanceState.Start(world);
            SurveillanceState.Tick();SurveillanceRenderService.Tick();
            if(world!=null&&!world.IsRemote()&&Time.realtimeSinceStartup>=scanAt)
            {scanAt=Time.realtimeSinceStartup+2;SurveillanceState.ScanLoaded();}
            if(world!=null&&!world.IsRemote()&&Time.realtimeSinceStartup>=broadcastAt)
            {broadcastAt=Time.realtimeSinceStartup+2;SurveillanceState.Broadcast();}
        }
        static void Stopping(ref ModEvents.SWorldShuttingDownData data){Shutdown();}
        static void Stopped(ref ModEvents.SGameShutdownData data){Shutdown();}
        static void Shutdown(){SurveillanceMenu.Close();SurveillanceRenderService.Clear();SurveillanceClient.Clear();SurveillanceState.Stop();scanAt=broadcastAt=0;}
    }
}

public sealed class BlockPZAEC_SurveillanceCamera : BlockMotionSensor
{
    public override void OnBlockAdded(WorldBase world,Chunk chunk,Vector3i position,BlockValue value,PlatformUserIdentifierAbs addedBy)
    {
        base.OnBlockAdded(world,chunk,position,value,addedBy);
        if(value.ischild)return;
        if(!world.IsRemote()){PZAEC.Surveillance.SurveillanceState.Register(position,GetBlockName(),addedBy?.CombinedString??"");PZAEC.Surveillance.SurveillanceState.Broadcast();}
    }
    public override void OnBlockRemoved(WorldBase world,Chunk chunk,Vector3i position,BlockValue value)
    {
        PZAEC.Surveillance.SurveillanceState.Remove(position,GetBlockName());base.OnBlockRemoved(world,chunk,position,value);
    }
}

public sealed class BlockPZAEC_SurveillanceScreen : BlockPoweredLight
{
    const string Configure="pzaecSurveillanceConfigure";
    public override void OnBlockAdded(WorldBase world,Chunk chunk,Vector3i position,BlockValue value,PlatformUserIdentifierAbs addedBy)
    {
        base.OnBlockAdded(world,chunk,position,value,addedBy);
        if(value.ischild)return;
        if(!world.IsRemote()){PZAEC.Surveillance.SurveillanceState.Register(position,GetBlockName(),addedBy?.CombinedString??"");PZAEC.Surveillance.SurveillanceState.Broadcast();}
    }
    public override void OnBlockRemoved(WorldBase world,Chunk chunk,Vector3i position,BlockValue value)
    {
        Vector3i parent=Parent(position,value);PZAEC.Surveillance.SurveillanceState.Remove(parent,GetBlockName());base.OnBlockRemoved(world,chunk,position,value);
    }
    static Vector3i Parent(Vector3i position,BlockValue value)=>value.ischild?value.Block.multiBlockPos.GetParentPos(position,value):position;
    public override BlockActivationCommand[] GetBlockActivationCommands(WorldBase world,BlockValue value,Vector3i position,EntityAlive focusing)
    {
        var native=base.GetBlockActivationCommands(world,value,position,focusing)??BlockActivationCommand.Empty;
        var result=new BlockActivationCommand[native.Length+1];Array.Copy(native,result,native.Length);
        result[native.Length]=new BlockActivationCommand(Configure,"ui_game_symbol_camera",true);return result;
    }
    public override bool OnBlockActivated(string command,WorldBase world,Vector3i position,BlockValue value,EntityPlayerLocal player)
    {
        if(command==Configure){PZAEC.Surveillance.SurveillanceMenu.Open(Parent(position,value),player);return true;}
        return base.OnBlockActivated(command,world,position,value,player);
    }
    public override void OnBlockEntityTransformAfterActivated(WorldBase world,Vector3i position,BlockValue value,BlockEntityData data)
    {
        base.OnBlockEntityTransformAfterActivated(world,position,value,data);
        if(value.ischild||data?.transform==null||GameManager.IsDedicatedServer)return;
        var view=data.transform.GetComponent<PZAEC.Surveillance.ScreenView>();if(view!=null)view.Bind(world,position);
    }
}
