using System;
using PZAEC.Fishing.Contracts;
using UnityEngine;

namespace PZAEC.Fishing.Runtime
{
    public sealed class NativeBindings
    {
        readonly KeyCode cast,strike,reel,free,recenter,cancel,increase,decrease;
        static KeyCode Parse(string name) {KeyCode key;if(!Enum.TryParse(name,true,out key)||key==KeyCode.None||!Enum.IsDefined(typeof(KeyCode),key))throw new ArgumentException("Unknown fishing key: "+name);return key;}
        public NativeBindings(ControlConfig config) {
            cast=Parse(config.CastKey);strike=Parse(config.StrikeKey);reel=Parse(config.ReelKey);free=Parse(config.FreeLookKey);
            recenter=Parse(config.RecenterKey);cancel=Parse(config.CancelKey);increase=Parse(config.DragIncreaseKey);decrease=Parse(config.DragDecreaseKey);
        }
        public bool CastPressed=>Input.GetKeyDown(cast);
        public RawInputFrame Apply(RawInputFrame input) {
            input.CastPressed=Input.GetKeyDown(cast);input.StrikePressed=Input.GetKeyDown(strike);input.ReelHeld=Input.GetKey(reel);
            input.FreeLookHeld=Input.GetKey(free);input.RecenterHeld=Input.GetKey(recenter);input.CancelPressed=Input.GetKeyDown(cancel);
            input.DragAdjustDelta=((Input.GetKey(increase)?1:0)-(Input.GetKey(decrease)?1:0))*.25f*input.DurationSeconds;
            return input;
        }
    }
}
