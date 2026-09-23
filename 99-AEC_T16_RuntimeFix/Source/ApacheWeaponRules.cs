using System;

namespace AECT16RuntimeFix
{
    // SI units; independent of Unity so rate, heat, and seat rules can be tested.
    public static class ApacheWeaponRules
    {
        public const string RocketAmmo = "pzApacheRocket";
        public const string GuidedAmmo = "pzApacheGuidedMissile";
        public const float LockSeconds = 2f, GuidedCooldown = 8f, PilotRange = 350f;
        public const float GuidedDamage = 150000f, GuidedTurnRate = 45f;
        public const string CannonAmmo = "pzApache30mm";
        public const int SalvoSize = 3;
        public const float SalvoInterval = .15f, RocketCooldown = 1.5f;
        public const float CannonInterval = 1f / 6f, HeatPerShot = 6f, Cooling = 16f, ResumeHeat = 35f;
        public const float RocketSpeed = 70f, RocketLifetime = 6f, CannonRange = 300f;
        // Anti-horde support: T16-T19 ordinary HP is 160k/240k/360k/540k.
        // Keep terrain damage separate; native armor/headshot/explosion falloff still apply.
        public const float CannonEntityDamage = 20000f, CannonBlockDamage = 8f;
        public const float RocketEntityDamage = 100000f, RocketBlockDamage = 50f;
        public const float HoldTimeout = .5f, MuzzleOffset = 1.295f;
        public const float CannonMinPitch = -85f, CannonMaxPitch = 15f;
        // A sequence protects release against delayed/replayed keep-alive packets.
        public sealed class TriggerLease
        {
            public int Actor = -1, Sequence;
            public float Until;
            public bool Held, Seen;
            public bool Accept(int actor, int sequence, bool held, float now)
            {
                if(!Finite(now)||actor<0)return false;
                if(Actor==actor&&Seen&&unchecked(sequence-Sequence)<=0)return false;
                Actor=actor;Sequence=sequence;Seen=true;Held=held;Until=now+HoldTimeout;return true;
            }
            public bool Active(int occupant,float now){return Held&&Actor==occupant&&Finite(now)&&now<Until;}
            public void Stop(){Held=false;}
        }
        public static bool Finite(float n) { return !float.IsNaN(n) && !float.IsInfinity(n); }
        public static bool ValidDirection(float x, float y, float z)
        {
            if (!Finite(x) || !Finite(y) || !Finite(z)) return false;
            double length = (double)x*x + (double)y*y + (double)z*z;
            return length > .25 && length < 2.25;
        }
        public static bool InArc(float x, float y, float z)
        {
            if (!ValidDirection(x,y,z)) return false;
            double yaw = Math.Atan2(x,z)*180/Math.PI;
            double pitch = Math.Atan2(y, Math.Sqrt((double)x*x+(double)z*z))*180/Math.PI;
            return Math.Abs(yaw) <= 100.001 && pitch >= CannonMinPitch-.001 && pitch <= CannonMaxPitch+.001;
        }
        public static bool OperatorAllowed(int seat, int actor, int occupant, bool dead, bool vehicleDead, bool storageOpen)
        { return seat >= 0 && seat <= 1 && actor >= 0 && actor == occupant && !dead && !vehicleDead && !storageOpen; }

        public sealed class Gate
        {
            public float NextRocket, NextCannon, Heat, LastTime;
            public bool Overheated;
            public void Cool(float now)
            {
                if (!Finite(now)) return;
                Heat = Math.Max(0, Heat - Math.Max(0, now-LastTime)*Cooling);
                LastTime = now;
                if (Heat <= ResumeHeat) Overheated = false;
            }
            public bool Ready(int seat, float now)
            {
                if (!Finite(now)) return false;
                Cool(now);
                return seat == 0 ? now >= NextRocket : seat == 1 && now >= NextCannon && !Overheated;
            }
            // Commit only after server-authoritative ammo removal succeeds.
            public void Commit(int seat, float now)
            {
                Cool(now);
                if (seat == 0) NextRocket = now + RocketCooldown;
                else if (seat == 1)
                {
                    NextCannon = now + CannonInterval;
                    Heat = Math.Min(100, Heat + HeatPerShot);
                    if (Heat >= 100) Overheated = true;
                }
            }
        }
    }
}
