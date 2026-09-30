using System;
using System.Linq;
using UnityEngine;

// Disposable bootstrap, never installed in the live mod directory.
public sealed class FishingTestSaveSeed : IModApi
{
    static bool ready,done;static float saveAt;
    public void InitMod(Mod mod)
    {
        if(!Environment.GetCommandLineArgs().Contains("-pzaecCreateFishingTest"))return;
        ModEvents.GameStartDone.RegisterHandler(Ready);
        ModEvents.GameUpdate.RegisterHandler(Update);
    }
    static void Ready(ref ModEvents.SGameStartDoneData data)
    {if(GamePrefs.GetString(EnumGamePrefs.GameName)=="FishingTest"){ready=true;saveAt=Time.realtimeSinceStartup+5;}}
    static void Update(ref ModEvents.SGameUpdateData data)
    {
        if(!ready||done||Time.realtimeSinceStartup<saveAt)return;done=true;
        GameManager.Instance.SaveWorld();
        Log.Out("[FishingTestSeed] SAVED "+GameIO.GetSaveGameDir());
        Application.Quit(); // Normal game shutdown flushes native world data.
    }
}
