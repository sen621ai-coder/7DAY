using UnityEngine;

namespace AECT16RuntimeFix
{
    // Temporarily borrows the native NightVision screen effect while an Apache
    // crew member aims in darkness. The previous target/fade are restored so a
    // helmet night-vision mod keeps the state selected by the player.
    public static class ApacheNightVision
    {
        private const string EffectName = "NightVision";
        private static ScreenEffects owner;
        private static bool applied;
        private static float restoreTarget, restoreFade;

        public static bool Active { get { return applied; } }

        private static ScreenEffects.ScreenEffect Current(ScreenEffects effects)
        {
            return effects != null ? effects.Find(EffectName, effects.activeEffects) : null;
        }

        private static void Restore()
        {
            if (!applied) { owner = null; return; }
            var effects = owner;
            applied = false;
            owner = null;
            if (effects != null) effects.SetScreenEffect(EffectName, restoreTarget, restoreFade);
        }

        public static void Update(bool wanted)
        {
            var effects = ScreenEffects.Instance;
            if (!wanted || effects == null)
            {
                Restore();
                return;
            }
            if (applied && owner != effects) Restore();
            if (!applied)
            {
                owner = effects;
                var prior = Current(effects);
                restoreTarget = prior != null ? Mathf.Clamp01(prior.TargetIntensity) : 0f;
                restoreFade = prior != null ? Mathf.Max(0f, prior.FadeTime) : .08f;
                applied = true;
            }
            else
            {
                // Preserve an explicit native change made while aiming (for
                // example, switching helmet night vision off). Apache vision
                // remains on until aim is released, then restores that choice.
                var current = Current(effects);
                if (current != null && Mathf.Abs(current.TargetIntensity - 1f) > .001f)
                {
                    restoreTarget = Mathf.Clamp01(current.TargetIntensity);
                    restoreFade = Mathf.Max(0f, current.FadeTime);
                }
            }
            effects.SetScreenEffect(EffectName, 1f, .08f);
        }

        public static void Clear()
        {
            Restore();
            restoreTarget = restoreFade = 0f;
        }
    }
}
