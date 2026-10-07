using System;

namespace PZAEC.Fishing.Runtime
{
    public sealed class ModApi : IModApi
    {
        static string directory;
        static LocalFishingRuntime runtime;
        static UnityEngine.GameObject view;
        static bool failed;
        public void InitMod(Mod mod)
        {
            directory=mod.Path;
            ModEvents.GameUpdate.RegisterHandler(Update);
            ModEvents.WorldShuttingDown.RegisterHandler(Stopping);
            ModEvents.GameShutdown.RegisterHandler(Stopped);
            Log.Out("[PZAEC.Fishing] v0.2.8 natural-float hand-pole single-player runtime registered; remote clients and dedicated servers disabled.");
        }
        static void Update(ref ModEvents.SGameUpdateData data)
        {
            var world=GameManager.Instance?.World;
            if(world==null||world.IsRemote()||GameManager.IsDedicatedServer){Shutdown();return;}
            if(failed)return;
            try {
                if(runtime==null) {
                    runtime=new LocalFishingRuntime(directory);
                    view=new UnityEngine.GameObject("PZAEC Fishing Runtime");
                    view.AddComponent<FishingRuntimeView>().Runtime=runtime;
                }
                runtime.Tick();
                TestEquipmentDelivery.Tick(world);
            } catch(Exception error) {failed=true;runtime?.Fail(error);Log.Error("[PZAEC.Fishing] Runtime stopped: "+error);}
        }
        static void Stopping(ref ModEvents.SWorldShuttingDownData data){Shutdown();}
        static void Stopped(ref ModEvents.SGameShutdownData data){Shutdown();}
        static void Shutdown()
        {
            try{runtime?.Dispose();}catch(Exception error){Log.Error("[PZAEC.Fishing] Cleanup: "+error);}
            finally{runtime=null;if(view!=null)UnityEngine.Object.Destroy(view);view=null;failed=false;TestEquipmentDelivery.Reset();}
        }
    }
}
