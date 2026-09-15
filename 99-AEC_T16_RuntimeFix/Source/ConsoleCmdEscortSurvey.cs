using System;
using System.Collections.Generic;
using UnityEngine;

namespace AECT16RuntimeFix
{
    // Explicit console-only, read-only route survey. No world edits/entities.
    public sealed class ConsoleCmdEscortSurvey : ConsoleCmdAbstract
    {
        private static readonly List<Vector3> Points = new List<Vector3>();
        private static World surveyWorld;
        public override string[] getCommands() { return new[] { "aecescortsurvey" }; }
        public override string getDescription() { return "Read-only T16 escort route survey: clear, point, check. No gameplay task is started."; }
        public override void Execute(List<string> args, CommandSenderInfo sender)
        {
            var world = GameManager.Instance?.World;
            if (world == null || world.IsRemote() || sender.RemoteClientInfo != null)
            { Say("Run locally in a single-player TEST save. No remote survey supported."); return; }
            var player = world.GetPrimaryPlayer();
            if (player == null) { Say("No local player."); return; }
            if (!ReferenceEquals(world, surveyWorld)) { Points.Clear(); surveyWorld = world; }
            string op = args.Count == 0 ? "help" : args[0];
            if (op == "clear") { Points.Clear(); Say("Survey cleared; world unchanged."); return; }
            if (op == "point")
            {
                if (Points.Count >= 4) { Say("Four points already recorded. Use clear to restart."); return; }
                Points.Add(player.position); Say("Point " + Points.Count + ": " + player.position); return;
            }
            if (op != "check") { Say("Walk a proposed outdoor road: point at START + 3 CHECKPOINTS, then check. T16 length 300-400m. Never starts an encounter."); return; }
            if (Points.Count != 4) { Say("Need start + three checkpoints."); return; }
            float length = 0;
            for (int i = 1; i < Points.Count; i++) length += Vector3.Distance(Points[i-1], Points[i]);
            if (length < 300 || length > 400) { Say("REJECT length=" + length + "m; expected 300-400m."); return; }
            for (int i = 1; i < Points.Count; i++)
            {
                float segment = Vector3.Distance(Points[i-1], Points[i]);
                if (segment < 50) { Say("REJECT checkpoint segment shorter than 50m."); return; }
                int samples = Mathf.CeilToInt(segment / 2);
                float previous = 0;
                for (int j = 0; j <= samples; j++)
                {
                    Vector3 point = Vector3.Lerp(Points[i-1], Points[i], (float)j / samples);
                    int x = Mathf.FloorToInt(point.x), z = Mathf.FloorToInt(point.z);
                    if (world.GetChunkFromWorldPos(x,z) == null) { Say("UNVERIFIED unloaded chunk at " + x + "," + z + ". Recheck with route chunks loaded."); return; }
                    float height = world.GetHeight(x,z);
                    if (j > 0 && Math.Abs(height - previous) > 2) { Say("REJECT abrupt surface step near " + x + "," + z); return; }
                    previous = height;
                }
            }
            Say("PRELIMINARY PASS length=" + length + "m. Height samples only: water, claims, clearance, road topology and actual drone navigation still require validation. NOT cleared for selling quests.");
        }
        private static void Say(string text) { SdtdConsole.Instance.Output("[AEC-EscortSurvey] " + text); }
    }
}
