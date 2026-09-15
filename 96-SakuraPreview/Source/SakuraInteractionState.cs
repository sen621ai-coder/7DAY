namespace SakuraPreview
{
    public sealed class SakuraInteractionState
    {
        public int Leader = -1;
        float nextRequest;
        public byte Handle(bool server, bool alive, int playerId, float distanceSquared, byte action, float now)
        {
            if(!server || !alive || playerId<0 || action>4 || float.IsNaN(distanceSquared) || distanceSquared<0 || distanceSquared>16 ||
               float.IsNaN(now) || float.IsInfinity(now) || now<nextRequest)return 255;
            nextRequest=now+.3f;
            if(action==2 || action==3)
            {
                if(Leader!=-1 && Leader!=playerId)return 5;
                Leader=action==2?playerId:-1;
            }
            return action;
        }
    }
}
