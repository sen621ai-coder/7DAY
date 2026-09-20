using System;
using System.Runtime.CompilerServices;
using UnityEngine;

namespace PZAEC.PortableShower
{
    public sealed class ModApi : IModApi
    {
        public void InitMod(Mod mod) { Log.Out("[PortableShower] Wrist cleaner loaded: owner inventory, 30s combat delay, 2 bottles reserved."); }
    }

    public static class Rules
    {
        public const string Module = "modPZAECWristShower";
        public const string Water = "$PZAECShowerWaterSeconds";
        public const string Cooldown = "$PZAECShowerCombatSeconds";
        public const string Active = "buffPZAECShowerActive";
        public const string Empty = "buffPZAECShowerEmpty";
        public static float Clamp(float value, float max) => float.IsNaN(value) || float.IsInfinity(value) ? 0 : Math.Max(0, Math.Min(max, value));
        public static float CleanSeconds(float hygiene, float maximum, float water, float elapsed)
        {
            if (maximum <= 0 || float.IsNaN(maximum) || float.IsInfinity(maximum)) return 0;
            return Math.Min(Clamp(elapsed, 1), Math.Min(Clamp(water, 60), Math.Max(0, maximum - Clamp(hygiene, maximum)) * 300 / maximum));
        }
    }

    public static class Cleaner
    {
        private sealed class Clock { public float Last; public float Until; }
        private static readonly ConditionalWeakTable<EntityPlayerLocal, Clock> Clocks = new ConditionalWeakTable<EntityPlayerLocal, Clock>();
        private static Clock State(EntityPlayerLocal player)
        {
            return Clocks.GetValue(player, p => new Clock { Last = Time.time, Until = Time.time + 30 });
        }
        private static void Set(EntityPlayerLocal p, string name, float value) { p.Buffs.SetCustomVar(name, value, true); }
        private static void Hide(EntityPlayerLocal p)
        {
            Visuals.Set(p, false);
            if (p.Buffs.HasBuff(Rules.Active)) p.Buffs.RemoveBuff(Rules.Active);
            if (p.Buffs.HasBuff(Rules.Empty)) p.Buffs.RemoveBuff(Rules.Empty);
        }
        public static void Combat(EntityPlayerLocal player)
        {
            if (player == null) return;
            var state = State(player); state.Until = Time.time + 30;
            Set(player, Rules.Cooldown, 30); Hide(player);
        }
        public static bool Equipped(EntityPlayerLocal player)
        {
            var armor = player.equipment == null ? null : player.equipment.GetItems();
            if (armor == null) return false;
            foreach (var item in armor)
            {
                if (item == null || item.IsEmpty() || item.ItemClass == null ||
                    !item.ItemClass.ItemTags.Test_AnySet(FastTags<TagGroup.Global>.Parse("armorHands")) || item.Modifications == null) continue;
                foreach (var mod in item.Modifications)
                    if (mod != null && mod.ItemClass != null && mod.ItemClass.GetItemName() == Rules.Module) return true;
            }
            return false;
        }
        public static void Tick(EntityPlayerLocal player)
        {
            // Only the owning client has authoritative player inventory. Dedicated servers
            // and remote replicas never consume bottles or advance this survival effect.
            if (player == null || player.world == null) return;
            var state = State(player); float now = Time.time;
            float elapsed = Rules.Clamp(now - state.Last, 1); state.Last = now;
            if (player.Buffs.HasBuff("buffBlockIsInCombat")) Combat(player);
            Set(player, Rules.Cooldown, Rules.Clamp(state.Until - now, 30));
            if (player.IsDead() || now < state.Until || !Equipped(player) || player.AttachedToEntity != null ||
                player.IsRunning || player.MovementRunning || player.CalcIfSwimming() ||
                player.Buffs.HasBuff("buffHygieneStatus") || player.Buffs.HasBuff("buffBathStatus") || player.Buffs.HasBuff("buffBathRelax"))
            { Hide(player); return; }
            float maximum = player.Buffs.GetCustomVar("$HygieneTotal");
            float hygiene = player.Buffs.GetCustomVar("$HygieneStatus");
            if (maximum <= 0 || hygiene >= maximum || elapsed <= 0) { Hide(player); return; }
            float water = Rules.Clamp(player.Buffs.GetCustomVar(Rules.Water), 60);
            if (water <= 0)
            {
                var bottle = ItemClass.GetItem("drinkJarBoiledWater", false);
                if (bottle == null || bottle.type == 0 || player.bag == null || player.bag.GetItemCount(bottle) <= 2 || player.bag.DecItem(bottle, 1) != 1)
                {
                    Visuals.Set(player, false);
                    if (player.Buffs.HasBuff(Rules.Active)) player.Buffs.RemoveBuff(Rules.Active);
                    player.Buffs.AddBuff(Rules.Empty); return;
                }
                water = 60;
                Set(player, Rules.Water, water);
                // Notify only after a real bottle was consumed, never on resume/tick.
                player.Buffs.AddBuff("buffPZAECShowerRefilled");
            }
            float used = Rules.CleanSeconds(hygiene, maximum, water, elapsed);
            if (used <= 0) { Hide(player); return; }
            Set(player, Rules.Water, Math.Max(0, water - used));
            Set(player, "$HygieneStatus", Math.Min(maximum, hygiene + maximum * used / 300));
            if (player.Buffs.HasBuff(Rules.Empty)) player.Buffs.RemoveBuff(Rules.Empty);
            player.Buffs.AddBuff(Rules.Active);
            Visuals.Set(player, true);
        }
    }
}

// Native XML action factory supplies the MinEventAction prefix.
public sealed class MinEventActionPZAECShowerTick : MinEventActionBase
{
    public override void Execute(MinEventParams context) { PZAEC.PortableShower.Cleaner.Tick(context.Self as EntityPlayerLocal); }
}
public sealed class MinEventActionPZAECShowerCombat : MinEventActionBase
{
    public override void Execute(MinEventParams context) { PZAEC.PortableShower.Cleaner.Combat(context.Self as EntityPlayerLocal); }
}
