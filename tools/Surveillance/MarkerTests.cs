using System;
using PZAEC.Surveillance;

public static class SurveillanceMarkerTests
{
    static void Check(bool value,string message){if(!value)throw new Exception(message);}
    static MarkerPoint[] Box(float x0,float y0,float x1,float y1,float z)
    {
        var result=new MarkerPoint[8];for(int i=0;i<8;i++)result[i]=new MarkerPoint((i&1)==0?x0:x1,(i&2)==0?y0:y1,z);return result;
    }
    public static void Run()
    {
        Check(TargetMarkerRules.IsTarget(false,false,true,false),"Living zombie missing");
        Check(TargetMarkerRules.IsTarget(false,false,false,true),"Living animal missing");
        Check(!TargetMarkerRules.IsTarget(true,false,true,true),"Player classified as target");
        Check(!TargetMarkerRules.IsTarget(false,true,true,true),"Corpse classified as target");
        Check(!TargetMarkerRules.IsTarget(false,false,false,false),"Non-animal NPC classified as target");
        MarkerRect rect;
        Check(TargetMarkerRules.TryRect(Box(.1f,.2f,.4f,.7f,10),.05f,80,768,576,out rect)&&rect.Left==.1f&&rect.Top==.7f,"Visible target projection");
        Check(TargetMarkerRules.TryRect(Box(-.1f,.2f,.4f,1.2f,10),.05f,80,768,576,out rect)&&rect.Left==0&&rect.Top==1,"Edge clipping");
        Check(!TargetMarkerRules.TryRect(Box(1.1f,.2f,1.4f,.7f,10),.05f,80,768,576,out rect),"Offscreen target");
        Check(!TargetMarkerRules.TryRect(Box(.1f,.2f,.4f,.7f,-10),.05f,80,768,576,out rect),"Behind-camera target");
        Check(!TargetMarkerRules.TryRect(Box(.1f,.2f,.4f,.7f,90),.05f,80,768,576,out rect),"Beyond far clip");
        var near=Box(.1f,.2f,.4f,.7f,2);near[0].Z=.01f;
        Check(!TargetMarkerRules.TryRect(near,.05f,80,768,576,out rect),"Near-plane crossing must be conservative");
        Check(!TargetMarkerRules.TryRect(Box(.1f,.2f,.101f,.201f,10),.05f,80,768,576,out rect),"Subpixel noise");
        var invalid=Box(.1f,.2f,.4f,.7f,2);invalid[2].X=float.NaN;
        Check(!TargetMarkerRules.TryRect(invalid,.05f,80,768,576,out rect),"Invalid coordinates");
        Console.WriteLine("PASS target classification, corpses/players/NPC exclusion, viewport clipping, near-plane and invalid projections");
    }
}
