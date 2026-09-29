#Requires -Version 7.0
$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot
# Compile the production access predicate against small data-only stand-ins.
# Native transport/EntityPlayer integration is covered separately by NativeQA.
$stubs=@'
using System.Collections.Generic;
public sealed class UserId { public string CombinedString; }
public sealed class PlayerData { public UserId PrimaryId; }
public sealed class EntityPlayer { public PlayerData PersistentPlayerData; public Party Party; }
public sealed class Party { public int PartyID; public List<EntityPlayer> MemberList=new List<EntityPlayer>(); }
public static class CargoPartyTests {
 static int checks;
 static void Check(bool value,string message){if(!value)throw new System.Exception(message);checks++;}
 static EntityPlayer Player(string id){return new EntityPlayer{PersistentPlayerData=new PlayerData{PrimaryId=new UserId{CombinedString=id}}};}
 public static int Run(){
  var owner=Player("owner");var mate=Player("mate");var party=new Party();
  Check(YFAutomation.CargoDrones.CargoHubAccess.CanControl(owner,"owner"),"owner alone");
  Check(!YFAutomation.CargoDrones.CargoHubAccess.CanControl(mate,"owner"),"unrelated player");
  owner.Party=mate.Party=party;party.MemberList.Add(owner);
  Check(!YFAutomation.CargoDrones.CargoHubAccess.CanControl(mate,"owner"),"invitation/reference alone");
  party.MemberList.Add(mate);
  Check(YFAutomation.CargoDrones.CargoHubAccess.CanControl(mate,"owner"),"actual teammate");
  party.MemberList.Remove(mate);
  Check(!YFAutomation.CargoDrones.CargoHubAccess.CanControl(mate,"owner"),"membership removed immediately");
  party.MemberList.Add(mate);mate.Party=new Party{PartyID=party.PartyID};mate.Party.MemberList.Add(mate);
  Check(!YFAutomation.CargoDrones.CargoHubAccess.CanControl(mate,"owner"),"same numeric ID is insufficient");
  mate.Party=party;owner.Party=null;
  Check(!YFAutomation.CargoDrones.CargoHubAccess.CanControl(mate,"owner"),"owner left party");
  owner.Party=party;party.MemberList.Remove(owner);
  Check(!YFAutomation.CargoDrones.CargoHubAccess.CanControl(mate,"owner"),"owner removed from membership");
  Check(!YFAutomation.CargoDrones.CargoHubAccess.CanControl(null,"owner"),"missing actor");
  Check(!YFAutomation.CargoDrones.CargoHubAccess.CanControl(mate,null),"missing owner");
  return checks;
 }
}
'@
$source=[IO.File]::ReadAllText((Join-Path $root '97-AutomationWorkshop/Source/CargoDroneAccess.cs'))
Add-Type -TypeDefinition ($source+"`n"+$stubs.Replace('using System.Collections.Generic;','').Replace('List<EntityPlayer>','System.Collections.Generic.List<EntityPlayer>'))
Write-Output "PASS party access checks=$([CargoPartyTests]::Run()); production predicate, data-only stubs; native transport not tested."
