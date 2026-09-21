namespace AECT16RuntimeFix
{
    // One initial attempt plus at most three delayed retries per activation.
    public sealed class ForestryRetryState
    {
        public int Failures { get; private set; }
        public bool Pending { get; private set; }
        public float NextAt { get; private set; }
        public void Reset(){Failures=0;Pending=false;NextAt=0;}
        public void Restart(float now){Reset();Pending=true;NextAt=now;}
        public void Fail(float now)
        {Failures++;Pending=true;NextAt=now+(Failures==1?5:Failures==2?15:30);}
        public bool Ready(float now){return Pending&&Failures<=3&&now>=NextAt;}
        public void Recovered(){Pending=false;}
    }
}
