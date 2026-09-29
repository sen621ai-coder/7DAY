using System;
using System.Linq;
using PZAEC.Surveillance;
using UnityEngine;

public static class ScreenRepairHarness
{
    static void Check(bool ok,string message){if(!ok)throw new Exception(message);}
    public static void Run()
    {
        var block=new BlockPZAEC_SurveillanceScreen();block.Init();
        Check(block.BlockPlacementHelper is ScreenPlacement,"Wall placement helper not installed");
        Check(block.CreateTileEntity(null).PowerItemType==PowerItem.PowerItemTypes.ConsumerToggle,"Existing powered tile compatibility broken");
        var world=new WorldBase();var parent=new Vector3i(10,20,30);var child=new Vector3i(11,22,30);
        var value=new BlockValue{Block=block,rotation=0};world.Value=value;
        var childValue=value;childValue.ischild=true;block.multiBlockPos.Parent=parent;
        var player=new EntityPlayerLocal();
        var commands=block.GetBlockActivationCommands(world,childValue,child,player);
        Check(commands[0].text=="pzaecSurveillanceConfigure"&&commands[0].enabled,"Default action is not configuration");
        Check("ui_game_symbol_"+commands[0].icon=="ui_game_symbol_camera","Native radial icon prefix was duplicated");
        Check(commands.Any(c=>c.text=="take"),"Native pickup action missing");
        Check(world.LastLookup.Equals(parent),"Child commands did not resolve parent");
        Check(block.GetActivationText(world,value,parent,player).Contains("pzaecSurveillanceConfigure"),"Incorrect activation prompt");
        block.OnBlockActivated(world,child,childValue,player);
        Check(SurveillanceMenu.Opened.Equals(parent),"Direct activation opened wrong screen");
        SurveillanceMenu.Opened=default(Vector3i);
        block.OnBlockActivated("pzaecSurveillanceConfigure",world,child,childValue,player);
        Check(SurveillanceMenu.Opened.Equals(parent),"Command activation opened wrong screen");
        block.OnBlockActivated("pzaecSurveillanceToggle",world,child,childValue,player);
        Check(world.Tile.IsToggled,"Toggle failed on child");
        block.OnBlockActivated("take",world,child,childValue,player);
        Check(world.NativeTake.Equals(parent),"Native pickup did not use parent");
        Console.WriteLine("PASS first-command/direct/child configuration, toggle, native pickup and compatible power tile");

        var faces=new[]{BlockFace.North,BlockFace.East,BlockFace.South,BlockFace.West};
        var normals=new[]{Vector3.forward,Vector3.right,Vector3.back,Vector3.left};
        for(int i=0;i<faces.Length;i++)foreach(var mode in new[]{BlockPlacement.EnumRotationMode.Auto,BlockPlacement.EnumRotationMode.ToFace})
        {
            var hit=new HitInfoDetails{blockFace=faces[i]};
            var result=block.BlockPlacementHelper.OnPlaceBlock(0,mode,3,world,value,null,hit,new Vector3(99,0,-99));
            var q=BlockShapeNew.GetRotationStatic(result.blockValue.rotation);
            Check(Vector3.Dot(q*Vector3.forward,normals[i])>.99f,"Screen faces across/into the wall");
            Check(Vector3.Dot(q*Vector3.up,Vector3.up)>.99f,"Screen is not upright");
            // Native GetRotatedOffset adds rotated(-.5,-.5,0), plus world Y=.5.
            var origin=new Vector3(.5f,0,.5f)+q*new Vector3(.5f,0,0)+q*new Vector3(-.5f,-.5f,0)+new Vector3(0,.5f,0);
            for(int x=-1;x<=1;x+=2)for(int y=-1;y<=1;y+=2)for(int z=-1;z<=1;z+=2)
            {
                var local=new Vector3(ScreenLayout.X+x*ScreenLayout.Width/2,ScreenLayout.Y+y*ScreenLayout.Height/2,ScreenLayout.Z+z*ScreenLayout.Depth/2);
                var point=origin+q*local;bool inside=false;
                for(int cx=-2;cx<=1;cx++)for(int cy=0;cy<=2;cy++)
                {
                    var center=new Vector3(.5f,.5f,.5f)+q*new Vector3(cx,cy,0);
                    if(Math.Abs(point.x-center.x)<=.5001&&Math.Abs(point.y-center.y)<=.5001&&Math.Abs(point.z-center.z)<=.5001)inside=true;
                }
                Check(inside,"Model protrudes beyond its rotated 4x3 occupied cells");
            }
            Check(Math.Abs(ScreenLayout.Z-ScreenLayout.Depth/2+.5f-.01f)<.0001,"Rear wall gap is not 1 cm");
        }
        value.rotation=2;
        var manual=block.BlockPlacementHelper.OnPlaceBlock(0,BlockPlacement.EnumRotationMode.Simple,0,world,value,null,new HitInfoDetails{blockFace=BlockFace.East},Vector3.forward);
        Check(manual.blockValue.rotation==2,"Explicit manual rotation was overridden");
        Console.WriteLine("PASS four wall normals, auto/to-face modes, upright geometry within 12 occupied cells, 1 cm wall gap, manual rotation");
    }
}

// API doubles: no graphics/client execution is claimed by this harness.
namespace UnityEngine
{
    public struct Vector3
    {
        public float x,y,z;public Vector3(float x,float y,float z){this.x=x;this.y=y;this.z=z;}
        public static Vector3 forward=>new Vector3(0,0,1);public static Vector3 back=>new Vector3(0,0,-1);
        public static Vector3 right=>new Vector3(1,0,0);public static Vector3 left=>new Vector3(-1,0,0);public static Vector3 up=>new Vector3(0,1,0);
        public static Vector3 operator+(Vector3 a,Vector3 b)=>new Vector3(a.x+b.x,a.y+b.y,a.z+b.z);
        public static float Dot(Vector3 a,Vector3 b)=>a.x*b.x+a.y*b.y+a.z*b.z;
    }
    public struct Quaternion
    {
        public int R;
        public static Vector3 operator*(Quaternion q,Vector3 v){switch(q.R%4){case 1:return new Vector3(v.z,v.y,-v.x);case 2:return new Vector3(-v.x,v.y,-v.z);case 3:return new Vector3(-v.z,v.y,v.x);default:return v;}}
    }
    public class Transform{public T GetComponent<T>()where T:new()=>new T();}
}
public struct Vector3i{public int x,y,z;public Vector3i(int x,int y,int z){this.x=x;this.y=y;this.z=z;}}
public enum BlockFace{Top,Bottom,North,West,South,East}
public struct HitInfoDetails{public BlockFace blockFace;}
public class PropTransform{}
public class BlockPlacement
{
    public enum EnumPlacement{Block}public enum EnumRotationMode{ToFace,Simple,Advanced,Auto}
    public struct Result{public BlockValue blockValue;}
    public virtual Result OnPlaceBlock(EnumPlacement p,EnumRotationMode m,int r,WorldBase w,BlockValue v,PropTransform t,HitInfoDetails h,Vector3 e)=>new Result{blockValue=v};
}
public class BlockPlacementTowardsPlacerInverted:BlockPlacement{}
public static class BlockShapeNew{public static Quaternion GetRotationStatic(int r)=>new Quaternion{R=r};}
public struct BlockValue{public bool ischild;public byte rotation;public BlockPowered Block;}
public class Chunk{}
public class PlatformUserIdentifierAbs{public string CombinedString="";}
public class EntityAlive{}
public class EntityPlayerLocal:EntityAlive{public Actions playerInput=new Actions();}
public class Actions{public object Activate;public Actions PermanentActions=>this;}
public static class XUiUtils{public static string GetBindingXuiMarkupString(object x)=>"E";}
public static class Localization{public static string Get(string s)=>s;}
public class PowerItem{public enum PowerItemTypes{ConsumerToggle}}
public class TileEntityPowered{public PowerItem.PowerItemTypes PowerItemType;}
public class TileEntityPoweredBlock:TileEntityPowered{public bool IsToggled;public TileEntityPoweredBlock(Chunk c){}}
public class BlockEntityData{public Transform transform;}
public static class GameManager{public static bool IsDedicatedServer;}
public struct BlockActivationCommand
{
    public static BlockActivationCommand[] Empty=new BlockActivationCommand[0];public string text,icon;public bool enabled;
    public BlockActivationCommand(string text,string icon,bool enabled){this.text=text;this.icon=icon;this.enabled=enabled;}
}
public class WorldBase
{
    public BlockValue Value;public Vector3i LastLookup,NativeTake;public TileEntityPoweredBlock Tile=new TileEntityPoweredBlock(null);
    public bool IsRemote()=>false;public BlockValue GetBlock(Vector3i p){LastLookup=p;return Value;}
    public TileEntityPowered GetTileEntity(Vector3i p){LastLookup=p;return Tile;}
}
public class BlockPowered
{
    public BlockPlacement BlockPlacementHelper;public Multi multiBlockPos=new Multi();
    public class Multi{public Vector3i Parent;public Vector3i GetParentPos(Vector3i p,BlockValue v)=>Parent;}
    public virtual void Init(){}public virtual TileEntityPowered CreateTileEntity(Chunk c)=>null;public string GetBlockName()=>"screen";
    public virtual void OnBlockAdded(WorldBase w,Chunk c,Vector3i p,BlockValue v,PlatformUserIdentifierAbs a){}
    public virtual void OnBlockRemoved(WorldBase w,Chunk c,Vector3i p,BlockValue v){}
    public virtual void OnBlockStartsToFall(WorldBase w,Vector3i p,BlockValue v){}
    public virtual BlockActivationCommand[] GetBlockActivationCommands(WorldBase w,BlockValue v,Vector3i p,EntityAlive e)=>new[]{new BlockActivationCommand("take","hand",true)};
    public virtual string GetActivationText(WorldBase w,BlockValue v,Vector3i p,EntityAlive e)=>"";
    public virtual bool OnBlockActivated(WorldBase w,Vector3i p,BlockValue v,EntityPlayerLocal e)=>false;
    public virtual bool OnBlockActivated(string s,WorldBase w,Vector3i p,BlockValue v,EntityPlayerLocal e){w.NativeTake=p;return true;}
    public virtual void OnBlockEntityTransformAfterActivated(WorldBase w,Vector3i p,BlockValue v,BlockEntityData d){}
}
namespace PZAEC.Surveillance
{
    public static class ScreenLifecycle{public static void Placed(WorldBase w,Vector3i p,BlockValue v){}public static void Removed(WorldBase w,Vector3i p,BlockValue v){}public static void Falling(WorldBase w,Vector3i p,BlockValue v){}}
    public static class SurveillanceState{public static void Register(Vector3i p,string n,string a){}public static void Broadcast(){}public static void Remove(Vector3i p,string n){}}
    public static class SurveillanceMenu{public static Vector3i Opened;public static void Open(Vector3i p,EntityPlayerLocal e){Opened=p;}}
    public class ScreenView{public void Bind(WorldBase w,Vector3i p){}}
}
