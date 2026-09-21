using System;
using System.Runtime.CompilerServices;
using UnityEngine;

namespace PZAEC.PortableShower
{
    // Local cosmetic only: no AI noise event, heatmap entry, damage or network object.
    public static class Visuals
    {
        private sealed class Holder { public Spray Effect; public bool Failed; }
        private static readonly ConditionalWeakTable<EntityPlayerLocal, Holder> Effects = new ConditionalWeakTable<EntityPlayerLocal, Holder>();
        public static void Set(EntityPlayerLocal player, bool active)
        {
            if (!active)
            {
                Holder old; if (Effects.TryGetValue(player, out old) && old.Effect != null) old.Effect.Stop();
                return;
            }
            var holder = Effects.GetValue(player, p => new Holder());
            if (holder.Failed) return;
            try
            {
                if (holder.Effect == null)
                {
                    var go = new GameObject("PZAEC wrist shower audio");
                    go.transform.SetParent(player.transform, false);
                    go.transform.localPosition = new Vector3(.28f, 1.1f, .35f);
                    go.transform.localRotation = Quaternion.Euler(90, 0, 0);
                    holder.Effect = go.AddComponent<Spray>();
                    holder.Effect.Init(player);
                }
                holder.Effect.Refresh();
            }
            catch (Exception error)
            {
                holder.Failed = true;
                if (holder.Effect != null) UnityEngine.Object.Destroy(holder.Effect.gameObject);
                Log.Warning("[PortableShower] Cosmetic effect unavailable; cleaning remains active: " + error.Message);
            }
        }
    }
    public sealed class Spray : MonoBehaviour
    {
        private EntityPlayerLocal player;
        private AudioSource flow;
        private AudioClip clip;
        private float expires;
        public void Init(EntityPlayerLocal owner)
        {
            player = owner;
            // Visual water spray intentionally removed; keep the existing quiet audio.
            // Filtered low-volume noise gives a soft spray without shipping an audio asset.
            var samples = new float[22050]; var random = new System.Random(7301); float previous = 0;
            for (int i = 0; i < samples.Length; i++)
            {
                previous = previous * .7f + ((float)random.NextDouble() * 2 - 1) * .3f;
                samples[i] = previous * .4f * Mathf.Min(1, Mathf.Min(i, samples.Length - 1 - i) / 150f);
            }
            clip = AudioClip.Create("PZAEC gentle water spray", samples.Length, 1, 22050, false);
            clip.SetData(samples, 0);
            flow = gameObject.AddComponent<AudioSource>(); flow.clip = clip;
            flow.playOnAwake = false; flow.loop = true; flow.volume = .055f; flow.spatialBlend = 0;
        }
        public void Refresh()
        {
            expires = Time.time + 1.5f;
            if (!flow.isPlaying) flow.Play();
        }
        public void Stop()
        {
            expires = 0;
            if (flow != null) flow.Stop();
        }
        private void Update()
        {
            if (expires <= 0) return;
            if (player == null || player.IsDead() || Time.time > expires || !player.Buffs.HasBuff(Rules.Active) ||
                player.AttachedToEntity != null || player.IsRunning || player.MovementRunning || player.CalcIfSwimming()) Stop();
        }
        private void OnDestroy()
        {
            if (clip != null) UnityEngine.Object.Destroy(clip);
        }
    }
}
