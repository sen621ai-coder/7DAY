using System;

namespace PZAEC.Fishing.Contracts
{
    // Constructors provide probe defaults only. E supplies the production file and validation.
    public sealed class FishingConfig
    {
        public int Version = FishingContract.Version;
        public RodConfig Rod = new RodConfig();
        public LineConfig Line = new LineConfig();
        public FloatConfig Float = new FloatConfig();
        public HookConfig Hook = new HookConfig();
        public FishConfig Fish = new FishConfig();
        public ControlConfig Controls = new ControlConfig();
        public SessionConfig Session = new SessionConfig();
    }
    public sealed class RodConfig
    {
        public float LengthMeters=2.4f, StiffnessNewtonsPerMeter=100, DampingNewtonSecondsPerMeter=8;
        public float MaxDeflectionMeters=0.7f, MinPitchRadians=-0.35f, MaxPitchRadians=1.3f, MaxYawRadians=1.2f;
        public float AngularSpeedRadiansPerSecond=2.5f;
    }
    public sealed class LineConfig
    {
        public float FixedLengthMeters=0; // Positive: hand pole, no reel or drag payout.
        public float MaxLengthMeters=35, StiffnessNewtonsPerMeter=180, DampingNewtonSecondsPerMeter=3;
        public float BreakForceNewtons=90, DamageStartFraction=0.8f, DamagePerSecond=0.4f;
        public float DragMinNewtons=5, DragMaxNewtons=65, ReelSpeedMetersPerSecond=0.8f, MaxPayoutMetersPerSecond=6;
    }
    public sealed class FloatConfig
    {
        public float MassKg=0.012f, BuoyancyNewtonsPerMeter=2.5f, DampingNewtonSecondsPerMeter=0.3f;
        public float HeightMeters=0.18f, RestSubmerged01=0.4f;
    }
    public sealed class HookConfig
    {
        public bool NaturalBites=false;
        public float BiteWindowSeconds=1.2f, SlackLossSeconds=2.5f, MinimumStrikeSpeedRadiansPerSecond=0.6f;
        public float MaxSafeStrikeSpeedRadiansPerSecond=4, InitialQuality01=0.8f;
    }
    public sealed class FishConfig
    {
        public string Id=FishingContract.FishDefinition;
        public float MassKg=3, CruiseForceNewtons=9, BurstForceNewtons=50, DragCoefficient=3;
        public float StaminaJoules=450, RecoveryWatts=8, BurstSeconds=2, RecoverySeconds=4;
        public float CruiseSpeedMetersPerSecond=0.5f, BurstSpeedMetersPerSecond=3;
        public float NibbleMinSeconds=1, NibbleMaxSeconds=3, BiteWaitMinSeconds=3, BiteWaitMaxSeconds=12;
        public float NearShoreSurgeMeters=3, LandingStamina01=0.12f, LandingDistanceMeters=1.8f;
    }
    // Stable indices are persisted in catch CVars. Append only; index zero preserves old carp saves.
    public static class FishCatalog
    {
        public static readonly string[] Ids={"carp","crucian","grassCarp","silverCarp","bigheadCarp","whiteStrip","bitterling","sunfish"};
        static readonly string[] Names={"鲤鱼","鲫鱼","草鱼","鲢鱼","鳙鱼","白条","鳑鲏","太阳鱼"};
        static readonly string[] Suffixes={"Carp","Crucian","GrassCarp","SilverCarp","BigheadCarp","WhiteStrip","Bitterling","Sunfish"};
        static readonly float[] MassScale={1,.2f,1.5f,1.2f,1.6f,.035f,.02f,.1f};
        static readonly float[] ForceScale={1,.32f,1.18f,1.08f,1.15f,.12f,.08f,.22f};
        static readonly float[] EnergyScale={1,.22f,1.5f,1.2f,1.4f,.025f,.012f,.1f};
        static readonly int[] Weights={20,25,10,8,7,15,8,7};
        public static int Index(string id){return Array.IndexOf(Ids,id);}
        public static string FromSavedIndex(float index)
        {
            if(!Scalar.IsFinite(index)||index<0||index>=Ids.Length||index!=(int)index)throw new ArgumentException("Invalid saved fish species");
            return Ids[(int)index];
        }
        public static string Name(string id){int i=Index(id);return i<0?"鱼":Names[i];}
        public static string MeatItem(string id){int i=Index(id);return i<0?null:"pzaecFishingMeat"+Suffixes[i];}
        public static int MeatCount(float mass){return Math.Max(1,(int)Math.Ceiling(mass/.25f));}
        public static string Item(string id){int i=Index(id);return i<0?null:"pzaecFishingFish"+Suffixes[i];}
        public static FishConfig Profile(FishConfig basis,string id)
        {
            int i=Index(id);if(i<0)throw new ArgumentException("Unknown fish species");
            var fish=new FishConfig();foreach(var field in typeof(FishConfig).GetFields())field.SetValue(fish,field.GetValue(basis));
            fish.Id=id;fish.MassKg*=MassScale[i];fish.CruiseForceNewtons*=ForceScale[i];fish.BurstForceNewtons*=ForceScale[i];
            fish.StaminaJoules*=EnergyScale[i];fish.RecoveryWatts*=EnergyScale[i];
            if(i!=0){fish.DragCoefficient*=Math.Max(.15f,(float)Math.Pow(MassScale[i],.67));
                fish.BurstSeconds*=i>=5?.55f:1.1f;fish.RecoverySeconds*=i>=5?.7f:1;
                fish.NibbleMinSeconds*=i>=5?.45f:.8f;fish.NibbleMaxSeconds*=i>=5?.6f:.9f;}
            return fish;
        }
        public static FishConfig Size(FishConfig fish,float relativeMass)
        {
            if(!Scalar.IsFinite(relativeMass)||relativeMass<.5f||relativeMass>1.75f)throw new ArgumentException("Fish size outside supported range");
            fish.MassKg*=relativeMass;float strength=(float)Math.Sqrt(relativeMass);
            fish.CruiseForceNewtons*=strength;fish.BurstForceNewtons*=strength;
            fish.StaminaJoules*=relativeMass;fish.RecoveryWatts*=relativeMass;
            fish.DragCoefficient*=(float)Math.Pow(relativeMass,.67);return fish;
        }
        public static FishConfig Select(FishConfig basis,uint seed)
        {
            // Independent mixing prevents species order from correlating with the bite random stream.
            uint mixed=seed; mixed^=mixed>>16;mixed*=0x7feb352d;mixed^=mixed>>15;mixed*=0x846ca68b;mixed^=mixed>>16;
            int roll=(int)(mixed%100);for(int i=0;i<Ids.Length;i++){roll-=Weights[i];if(roll<0)return Size(Profile(basis,Ids[i]),.5f+1.25f*((mixed>>8)%10001)/10000f);}
            return Profile(basis,Ids[0]);
        }
    }
    public sealed class ControlConfig
    {
        public float RadiansPerMouseUnit=0.04f, LoadedResponseFloor01=0.25f, AutoBackThreshold01=0.85f;
        public float AutoBackGain=0.15f, AutoBackDecaySeconds=0.08f, MaxAutoBack01=0.6f;
        public float PlayerResistanceNewtons=110, MinAgainstPullScale=0.1f, InitialDrag01=0.5f, FeedbackIntensity01=0.3f;
        public bool AutoBackEnabled=true;
        public bool ClickStrike=false;
        public bool EffortEnabled=false;
        public string CastKey="Mouse0", StrikeKey="Mouse0", ReelKey="Mouse1", FreeLookKey="LeftAlt", RecenterKey="LeftControl", CancelKey="Escape";
        public string DragIncreaseKey="Equals", DragDecreaseKey="Minus";
    }
    public sealed class SessionConfig
    {
        public float MinDepthMeters=0.6f, MaxCastMeters=25, MaxPlayerDistanceMeters=40, TimeoutSeconds=300;
        public int MaxCatchUpTicks=8;
    }
}
