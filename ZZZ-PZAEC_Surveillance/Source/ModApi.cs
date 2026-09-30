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
        static bool legacyOversizedSkipped;
        public void InitMod(Mod mod)
        {
            var harmony=new Harmony("pzaec.surveillance");
            harmony.Patch(AccessTools.Method(typeof(BlockShapeModelEntity),"getPrefab"),prefix:new HarmonyMethod(typeof(ModApi),nameof(GetPrefab)));
            harmony.Patch(AccessTools.Method(typeof(GameObjectPool),"DestroyObject",new[]{typeof(GameObject)}),prefix:new HarmonyMethod(typeof(ModApi),nameof(BeforePoolDestroy)));
            harmony.Patch(AccessTools.Method(typeof(MultiBlockManager),"TryRegisterOversizedBlock"),prefix:new HarmonyMethod(typeof(ModApi),nameof(SkipLegacyOversizedRegistration)));
            harmony.Patch(AccessTools.Method(typeof(MultiBlockManager),"Initialize"),postfix:new HarmonyMethod(typeof(ModApi),nameof(FinishLegacyStructureMigration)));
            ModEvents.GameStartDone.RegisterHandler(Start);
            ModEvents.GameUpdate.RegisterHandler(Update);
            ModEvents.WorldShuttingDown.RegisterHandler(Stopping);
            ModEvents.GameShutdown.RegisterHandler(Stopped);
            Log.Out("[Surveillance] v1.0.15 wireless cameras and 4x3 live monitor loaded.");
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
        public static bool SkipLegacyOversizedRegistration(MultiBlockManager __instance,BlockValue __1,ref bool __result)
        {
            // Old multiblocks.7dt files remember the oversized flag. The loader
            // rebuilds each tracking category independently; skip only this obsolete
            // category, preserving normal cross-chunk tracking and the world blocks.
            var block=__1.Block;
            if(!(block is BlockPZAEC_SurveillanceScreen)||block.isOversized)return true;
            legacyOversizedSkipped=true;__result=false;
            Log.Out("[Surveillance] Migrated legacy screen oversized tracking to native 4x3 support.");
            return false;
        }
        public static void FinishLegacyStructureMigration(MultiBlockManager __instance)
        {
            // Initialize clears isDirty after reading; mark it only after that reset.
            if(legacyOversizedSkipped){__instance.isDirty=true;legacyOversizedSkipped=false;}
        }
        public static void BeforePoolDestroy(GameObject __0)
        {
            if(__0==null)return;
            // ItemClassBlock.CreateMesh wraps CloneModel in another GameObject. The pool
            // receives that wrapper on tool changes, not necessarily the ScreenView root.
            foreach(var view in __0.GetComponentsInChildren<ScreenView>(true))
            {
                view.Retiring=true;
                foreach(var renderer in view.GetComponentsInChildren<Renderer>(true))renderer.sharedMaterials=new Material[0];
            }
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

public sealed class BlockPZAEC_SurveillanceScreen : BlockPowered
{
    const string Configure="pzaecSurveillanceConfigure";
    const string Toggle="pzaecSurveillanceToggle";
    public override void Init(){base.Init();BlockPlacementHelper=new PZAEC.Surveillance.ScreenPlacement();}
    // Keep the existing tile/power type so previously placed screens retain wiring and switch state.
    public override TileEntityPowered CreateTileEntity(Chunk chunk)=>new TileEntityPoweredBlock(chunk){PowerItemType=PowerItem.PowerItemTypes.ConsumerToggle};
    public override void OnBlockAdded(WorldBase world,Chunk chunk,Vector3i position,BlockValue value,PlatformUserIdentifierAbs addedBy)
    {
        base.OnBlockAdded(world,chunk,position,value,addedBy);
        if(value.ischild)return;
        PZAEC.Surveillance.ScreenLifecycle.Placed(world,position,value);
        if(!world.IsRemote()){PZAEC.Surveillance.SurveillanceState.Register(position,GetBlockName(),addedBy?.CombinedString??"");PZAEC.Surveillance.SurveillanceState.Broadcast();}
    }
    public override void OnBlockRemoved(WorldBase world,Chunk chunk,Vector3i position,BlockValue value)
    {
        PZAEC.Surveillance.ScreenLifecycle.Removed(world,position,value);
        Vector3i parent=Parent(position,value);PZAEC.Surveillance.SurveillanceState.Remove(parent,GetBlockName());base.OnBlockRemoved(world,chunk,position,value);
    }
    public override void OnBlockStartsToFall(WorldBase world,Vector3i position,BlockValue value)
    {
        PZAEC.Surveillance.ScreenLifecycle.Falling(world,position,value);
        base.OnBlockStartsToFall(world,position,value);
    }
    static Vector3i Parent(Vector3i position,BlockValue value)=>value.ischild?value.Block.multiBlockPos.GetParentPos(position,value):position;
    public override BlockActivationCommand[] GetBlockActivationCommands(WorldBase world,BlockValue value,Vector3i position,EntityAlive focusing)
    {
        position=Parent(position,value);value=world.GetBlock(position);
        var native=base.GetBlockActivationCommands(world,value,position,focusing)??BlockActivationCommand.Empty;
        var result=new BlockActivationCommand[native.Length+2];Array.Copy(native,0,result,2,native.Length);
        result[0]=new BlockActivationCommand(Configure,"camera",true);
        result[1]=new BlockActivationCommand(Toggle,"electric_switch",true);return result;
    }
    public override string GetActivationText(WorldBase world,BlockValue value,Vector3i position,EntityAlive focusing)
    {
        var player=focusing as EntityPlayerLocal;string binding="E";
        if(player?.playerInput!=null)binding=XUiUtils.GetBindingXuiMarkupString(player.playerInput.Activate)+XUiUtils.GetBindingXuiMarkupString(player.playerInput.PermanentActions.Activate);
        return "("+binding+") "+Localization.Get(Configure);
    }
    public override bool OnBlockActivated(WorldBase world,Vector3i position,BlockValue value,EntityPlayerLocal player)
    {PZAEC.Surveillance.SurveillanceMenu.Open(Parent(position,value),player);return true;}
    public override bool OnBlockActivated(string command,WorldBase world,Vector3i position,BlockValue value,EntityPlayerLocal player)
    {
        position=Parent(position,value);value=world.GetBlock(position);
        if(command==Configure)return OnBlockActivated(world,position,value,player);
        if(command==Toggle){var tile=world.GetTileEntity(position) as TileEntityPoweredBlock;if(tile!=null)tile.IsToggled=!tile.IsToggled;return true;}
        return base.OnBlockActivated(command,world,position,value,player);
    }
    public override void OnBlockEntityTransformAfterActivated(WorldBase world,Vector3i position,BlockValue value,BlockEntityData data)
    {
        base.OnBlockEntityTransformAfterActivated(world,position,value,data);
        if(value.ischild||data?.transform==null||GameManager.IsDedicatedServer)return;
        var view=data.transform.GetComponent<PZAEC.Surveillance.ScreenView>();if(view!=null)view.Bind(world,position);
    }
}
