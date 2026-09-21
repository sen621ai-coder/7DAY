$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot
$source=Get-Content "$root/99-AEC_T16_RuntimeFix/Source/ApacheFlightAssist.cs" -Raw
$start=$source.IndexOf('        public static KeyCode Key(')
$end=$source.IndexOf('        public static void ReceiveMark(',$start)
$methods=$source.Substring($start,$end-$start)
$stub=@'
using System;
using UnityEngine;
namespace UnityEngine {
 public class Camera {public float fieldOfView=80;}
 public enum KeyCode {None,Mouse1}
 public static class Input {public static bool Held=true;public static bool GetKey(KeyCode key)=>Held;}
 public static class Mathf {public static float Abs(float n)=>Math.Abs(n);}
}
public class Properties {public System.Collections.Generic.Dictionary<string,string> Values=new System.Collections.Generic.Dictionary<string,string>();}
public class VehicleData {public Properties Properties=new Properties();}
public class EntityVehicle {public VehicleData vehicle=new VehicleData();public int Seat=1;}
public class Player {public EntityVehicle AttachedToEntity=new EntityVehicle();public int entityId=7;public Camera playerCamera=new Camera();public bool Dead;public bool IsDead()=>Dead;}
public class World {public Player Player=new Player();public Player GetPrimaryPlayer()=>Player;}
public class GameManager {public static GameManager Instance=new GameManager();public World World=new World();public bool GameIsFocused=true,Paused;public bool IsPaused()=>Paused;}
public class Windows {public bool Cursor,Text;public bool IsCursorWindowOpen()=>Cursor;public bool IsInputActive()=>Text;}
public class LocalPlayerUI {public Windows windowManager=new Windows();public static LocalPlayerUI UI=new LocalPlayerUI();public static bool Modal;public static LocalPlayerUI GetUIForPlayer(Player p)=>UI;public static bool AnyModalWindowOpen()=>Modal;}
namespace AECT16RuntimeFix {
 public static class ApacheFiringFeedback {public static void Clear(){}public static void RestoreCamera(){}public static void UpdateCamera(Player p,bool usable,bool aiming){} }
 public static class ApacheWeapons {public static bool IsApache(EntityVehicle v)=>v!=null;public static int Seat(EntityVehicle v,int actor)=>v.Seat;}
 public static class ApacheAimVisibility {public static bool Hidden;public static void Update(EntityVehicle v,bool aiming){Hidden=aiming;}public static void Clear(){Hidden=false;}}
 public static class ApacheNightVision {public static bool Active;public static void Update(bool wanted){Active=wanted;}public static void Clear(){Active=false;}}
 public static class SkyManager {public static bool Dark=true;public static bool IsDark()=>Dark;}
 public static class ZoomFixture {
  static Camera zoomCamera;static float baseFov,lastFov,markUntil,nextPrediction;static int predictionVehicle,markVehicle;
'@
$tests=@'
  static int checks;static void Check(bool ok,string message){checks++;if(!ok)throw new Exception(message);}
  static void Frame(){BeforeCameraUpdated();CameraUpdated();}
  public static void Run(){
   var p=GameManager.Instance.World.Player;var c=p.playerCamera;
   Frame();float expected=(float)(2*Math.Atan(Math.Tan(80*Math.PI/360)/2)*180/Math.PI);
   Check(Math.Abs(c.fieldOfView-expected)<.001&&ApacheAimVisibility.Hidden&&ApacheNightVision.Active,"2x night aiming hides local airframe and enables night vision");
   for(int i=0;i<100;i++)Frame();Check(Math.Abs(c.fieldOfView-expected)<.001,"no compounding FOV across frames");
   SkyManager.Dark=false;Frame();Check(!ApacheNightVision.Active&&c.fieldOfView==expected,"daylight keeps zoom but disables automatic night vision");SkyManager.Dark=true;Frame();Check(ApacheNightVision.Active,"night transition while aiming enables automatic night vision");
   BeforeCameraUpdated();Check(c.fieldOfView==80,"native camera sees unmodified FOV before update");
   c.fieldOfView=90;CameraUpdated();Input.Held=false;Frame();Check(c.fieldOfView==90&&zoomCamera==null&&!ApacheAimVisibility.Hidden&&!ApacheNightVision.Active,"release restores camera, airframe and night vision");
   Input.Held=true;Frame();p.AttachedToEntity.Seat=0;Frame();Check(c.fieldOfView<90,"pilot also has aiming magnification");
   p.AttachedToEntity.Seat=1;Frame();LocalPlayerUI.Modal=true;Frame();Check(c.fieldOfView==90&&!ApacheAimVisibility.Hidden&&!ApacheNightVision.Active,"menu restores camera, airframe and night vision");LocalPlayerUI.Modal=false;
   Frame();GameManager.Instance.GameIsFocused=false;Frame();Check(c.fieldOfView==90,"focus loss restores FOV");GameManager.Instance.GameIsFocused=true;
   Frame();p.Dead=true;Frame();Check(c.fieldOfView==90,"death restores FOV");p.Dead=false;
   Frame();p.AttachedToEntity=null;Frame();Check(c.fieldOfView==90,"dismount restores FOV");p.AttachedToEntity=new EntityVehicle();
   Frame();Clear();Check(c.fieldOfView==90&&zoomCamera==null,"world cleanup restores FOV");
   Frame();c.fieldOfView=100;Clear();Check(c.fieldOfView==100,"cleanup respects another system's FOV change");
   Console.WriteLine("PASS: "+checks+" actual camera-hook FOV and restoration checks with mocked game state; Unity execution order still needs in-game verification.");
  }
 }
}
'@
Add-Type -TypeDefinition ($stub+$methods+$tests) -CompilerOptions '/nowarn:0414'
[AECT16RuntimeFix.ZoomFixture]::Run()
