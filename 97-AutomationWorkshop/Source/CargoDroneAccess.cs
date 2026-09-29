using System;
using System.Linq;

namespace YFAutomation.CargoDrones
{
    // Called only by authoritative server request handlers. Invitations, clients'
    // local friend flags and party IDs alone do not grant control.
    public static class CargoHubAccess
    {
        public static bool CanControl(EntityPlayer actor,string owner)
        {
            if(actor==null||string.IsNullOrEmpty(owner))return false;
            if(actor.PersistentPlayerData?.PrimaryId?.CombinedString==owner)return true;
            var party=actor.Party;
            return party!=null&&party.MemberList!=null&&party.MemberList.Contains(actor)&&
                party.MemberList.Any(member=>member!=null&&ReferenceEquals(member.Party,party)&&member.PersistentPlayerData?.PrimaryId?.CombinedString==owner);
        }
    }
}
