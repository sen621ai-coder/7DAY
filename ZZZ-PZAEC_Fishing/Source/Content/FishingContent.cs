using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Xml;
using System.Xml.Linq;
using PZAEC.Fishing.Contracts;

namespace PZAEC.Fishing.Content
{
    /// <summary>File-backed v1 content. Does not write inventory or resolve session authority.</summary>
    public sealed class FishingContent : IFishingContent
    {
        public const int BaitPerAcceptedCast = 1;
        public const int CatchPerLanding = 1;
        private bool loaded;
        private float loadedMassKg;

        private static readonly Dictionary<string, float[]> Ranges = new Dictionary<string, float[]>(StringComparer.Ordinal)
        {
            {"Rod.LengthMeters", R(.5f,10)}, {"Rod.StiffnessNewtonsPerMeter", R(1,2000)},
            {"Rod.DampingNewtonSecondsPerMeter", R(0,100)}, {"Rod.MaxDeflectionMeters", R(.01f,3)},
            {"Rod.MinPitchRadians", R(-1.5f,0)}, {"Rod.MaxPitchRadians", R(.1f,1.55f)},
            {"Rod.MaxYawRadians", R(.1f,3.14f)}, {"Rod.AngularSpeedRadiansPerSecond", R(.1f,10)},
            {"Line.MaxLengthMeters", R(2,200)}, {"Line.StiffnessNewtonsPerMeter", R(1,2000)},
            {"Line.DampingNewtonSecondsPerMeter", R(0,100)}, {"Line.BreakForceNewtons", R(1,1000)},
            {"Line.DamageStartFraction", R(.1f,.99f)}, {"Line.DamagePerSecond", R(.01f,10)},
            {"Line.DragMinNewtons", R(.1f,1000)}, {"Line.DragMaxNewtons", R(.1f,1000)},
            {"Line.ReelSpeedMetersPerSecond", R(.05f,5)}, {"Line.MaxPayoutMetersPerSecond", R(.1f,20)},
            {"Float.MassKg", R(.001f,.5f)}, {"Float.BuoyancyNewtonsPerMeter", R(.1f,100)},
            {"Float.DampingNewtonSecondsPerMeter", R(.01f,10)}, {"Float.HeightMeters", R(.02f,1)},
            {"Float.RestSubmerged01", R(.05f,.95f)},
            {"Hook.BiteWindowSeconds", R(.1f,10)}, {"Hook.SlackLossSeconds", R(.1f,20)},
            {"Hook.MinimumStrikeSpeedRadiansPerSecond", R(.01f,10)},
            {"Hook.MaxSafeStrikeSpeedRadiansPerSecond", R(.1f,15)}, {"Hook.InitialQuality01", R(.01f,1)},
            {"Fish.MassKg", R(.05f,100)}, {"Fish.CruiseForceNewtons", R(.1f,1000)},
            {"Fish.BurstForceNewtons", R(.1f,1000)}, {"Fish.DragCoefficient", R(.1f,100)},
            {"Fish.StaminaJoules", R(1,100000)}, {"Fish.RecoveryWatts", R(0,1000)},
            {"Fish.BurstSeconds", R(.1f,30)}, {"Fish.RecoverySeconds", R(.1f,60)},
            {"Fish.CruiseSpeedMetersPerSecond", R(.05f,10)}, {"Fish.BurstSpeedMetersPerSecond", R(.1f,20)},
            {"Fish.NibbleMinSeconds", R(.1f,60)}, {"Fish.NibbleMaxSeconds", R(.1f,60)},
            {"Fish.BiteWaitMinSeconds", R(.1f,120)}, {"Fish.BiteWaitMaxSeconds", R(.1f,120)},
            {"Fish.NearShoreSurgeMeters", R(.1f,20)}, {"Fish.LandingStamina01", R(.01f,.5f)},
            {"Fish.LandingDistanceMeters", R(.2f,5)},
            {"Controls.RadiansPerMouseUnit", R(.0001f,1)}, {"Controls.LoadedResponseFloor01", R(.01f,1)},
            {"Controls.AutoBackThreshold01", R(.1f,1)}, {"Controls.AutoBackGain", R(.001f,2)},
            {"Controls.AutoBackDecaySeconds", R(.01f,1)}, {"Controls.MaxAutoBack01", R(0,1)},
            {"Controls.PlayerResistanceNewtons", R(1,2000)}, {"Controls.MinAgainstPullScale", R(.01f,1)},
            {"Controls.InitialDrag01", R(0,1)}, {"Controls.FeedbackIntensity01", R(0,1)},
            {"Session.MinDepthMeters", R(.1f,10)}, {"Session.MaxCastMeters", R(1,100)},
            {"Session.MaxPlayerDistanceMeters", R(2,200)}, {"Session.TimeoutSeconds", R(10,3600)},
            {"Session.MaxCatchUpTicks", R(1,16)}
        };

        private static float[] R(float min, float max) { return new[] { min, max }; }
        private static FieldInfo[] Sections { get { return typeof(FishingConfig).GetFields().Where(f => f.Name != "Version").ToArray(); } }

        public FishingConfig Load(string modDirectory)
        {
            loaded = false;
            if (string.IsNullOrWhiteSpace(modDirectory)) throw new ArgumentException("Mod directory is required.", "modDirectory");
            using (var stream = File.OpenRead(Path.Combine(modDirectory, FishingContract.ConfigFile)))
                return LoadStream(stream);
        }

        // Public for deterministic fixtures and hosts which already own the file stream.
        // Streams remain open. Failed reloads revoke reward lookup until valid content is loaded again.
        public FishingConfig LoadStream(Stream stream)
        {
            loaded = false;
            if (stream == null) throw new ArgumentNullException("stream");
            var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, CloseInput = false, MaxCharactersInDocument = 65536 };
            XDocument document;
            using (var reader = XmlReader.Create(stream, settings)) document = XDocument.Load(reader);
            var root = document.Root;
            Need(root != null && root.Name == "fishingConfig", "Expected fishingConfig root.");
            Need(root.Attributes().Count() == 1 && (string)root.Attribute("version") == FishingContract.Version.ToString(CultureInfo.InvariantCulture), "Unsupported or missing config version.");
            var config = new FishingConfig();
            var sections = Sections;
            Need(root.Elements().Count() == sections.Length, "Missing or duplicate configuration section.");
            NoText(root);
            foreach (var section in sections)
            {
                var nodes = root.Elements(section.Name).ToArray();
                Need(nodes.Length == 1, "Expected one " + section.Name + " section.");
                var node = nodes[0];
                Need(!node.HasElements, "Unexpected nested section: " + section.Name);
                NoText(node);
                var target = section.GetValue(config);
                var fields = section.FieldType.GetFields();
                Need(node.Attributes().Count() == fields.Length, "Missing or unknown field in " + section.Name);
                foreach (var field in fields)
                {
                    var attribute = node.Attribute(field.Name);
                    Need(attribute != null, "Missing field: " + section.Name + "." + field.Name);
                    string raw = attribute.Value;
                    object value;
                    if (field.FieldType == typeof(float))
                    {
                        float number;
                        Need(float.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out number) && Scalar.IsFinite(number), "Invalid number: " + field.Name);
                        value = number;
                    }
                    else if (field.FieldType == typeof(int))
                    {
                        int number;
                        Need(int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out number), "Invalid integer: " + field.Name);
                        value = number;
                    }
                    else if (field.FieldType == typeof(bool))
                    {
                        Need(raw == "true" || raw == "false", "Invalid boolean: " + field.Name);
                        value = raw == "true";
                    }
                    else if (field.FieldType == typeof(string)) value = raw;
                    else throw new InvalidDataException("Unsupported field type: " + field.Name);
                    field.SetValue(target, value);
                }
            }
            string error;
            Need(Validate(config, out error), error);
            loadedMassKg = config.Fish.MassKg;
            loaded = true;
            return config;
        }

        public bool Validate(FishingConfig config, out string error)
        {
            error = null;
            if (config == null || config.Version != FishingContract.Version) return Fail("Missing config or unsupported version.", out error);
            foreach (var section in Sections)
            {
                object value = section.GetValue(config);
                if (value == null) return Fail("Missing section: " + section.Name, out error);
                foreach (var field in section.FieldType.GetFields())
                {
                    string name = section.Name + "." + field.Name;
                    if (field.FieldType == typeof(float) || field.FieldType == typeof(int))
                    {
                        float number = Convert.ToSingle(field.GetValue(value), CultureInfo.InvariantCulture);
                        float[] bounds;
                        if (!Ranges.TryGetValue(name, out bounds) || !Scalar.IsFinite(number) || number < bounds[0] || number > bounds[1])
                            return Fail("Out-of-range or unsupported field: " + name, out error);
                    }
                    else if (field.FieldType == typeof(string))
                    {
                        string text = (string)field.GetValue(value);
                        if (name == "Fish.Id")
                        {
                            if (text != FishingContract.FishDefinition) return Fail("Unknown fish definition.", out error);
                        }
                        else if (!IsSupportedKey(text)) return Fail("Unsupported key: " + name, out error);
                    }
                }
            }
            if (config.Rod.MaxDeflectionMeters >= config.Rod.LengthMeters) return Fail("Rod deflection must be shorter than rod.", out error);
            if (config.Line.DragMinNewtons >= config.Line.DragMaxNewtons || config.Line.DragMaxNewtons >= config.Line.BreakForceNewtons)
                return Fail("Drag range must be ordered and below break force.", out error);
            if (config.Fish.CruiseForceNewtons > config.Fish.BurstForceNewtons || config.Fish.CruiseSpeedMetersPerSecond > config.Fish.BurstSpeedMetersPerSecond)
                return Fail("Fish burst must not be weaker than cruising.", out error);
            if (config.Fish.NibbleMinSeconds > config.Fish.NibbleMaxSeconds || config.Fish.BiteWaitMinSeconds > config.Fish.BiteWaitMaxSeconds)
                return Fail("Reversed fish timing range.", out error);
            if (config.Hook.MinimumStrikeSpeedRadiansPerSecond >= config.Hook.MaxSafeStrikeSpeedRadiansPerSecond)
                return Fail("Strike speed range is reversed.", out error);
            if (config.Session.MaxCastMeters > config.Line.MaxLengthMeters || config.Session.MaxCastMeters > config.Session.MaxPlayerDistanceMeters)
                return Fail("Cast range exceeds line capacity or player range.", out error);
            if (config.Fish.LandingDistanceMeters > config.Fish.NearShoreSurgeMeters) return Fail("Landing must be inside near-shore range.", out error);
            if (config.Float.BuoyancyNewtonsPerMeter * config.Float.HeightMeters <= config.Float.MassKg * 9.81f)
                return Fail("Float cannot support its own mass within its height.", out error);
            // Cast and strike intentionally share a button in different states; other roles cannot overlap.
            var roles = typeof(ControlConfig).GetFields().Where(f => f.FieldType == typeof(string)).ToArray();
            for (int i = 0; i < roles.Length; i++) for (int j = i + 1; j < roles.Length; j++)
            {
                bool castStrike = (roles[i].Name == "CastKey" && roles[j].Name == "StrikeKey") || (roles[i].Name == "StrikeKey" && roles[j].Name == "CastKey");
                if (!castStrike && Equals(roles[i].GetValue(config.Controls), roles[j].GetValue(config.Controls)))
                    return Fail("Conflicting key bindings: " + roles[i].Name + " / " + roles[j].Name, out error);
            }
            return true;
        }

        public bool TryGetReward(CatchResult result, out RewardSpec reward)
        {
            reward = default(RewardSpec);
            // This is definition validation only. Authority, landed state and idempotency belong to A.
            if (!loaded || result.SessionId == Guid.Empty || result.SettlementId == Guid.Empty || result.TerminalTick < 0
                || string.IsNullOrWhiteSpace(result.PlayerPersistentId) || result.FishDefinitionId != FishingContract.FishDefinition
                || !Scalar.IsFinite(result.MassKg) || Math.Abs(result.MassKg - loadedMassKg) > .001f) return false;
            reward = new RewardSpec { ItemId = FishingContract.FishItem, Count = CatchPerLanding, FishMassKg = result.MassKg };
            return true;
        }

        private static bool IsSupportedKey(string key)
        {
            if (string.IsNullOrWhiteSpace(key)) return false;
            if (key.Length == 1 && key[0] >= 'A' && key[0] <= 'Z') return true;
            if (key.Length == 6 && key.StartsWith("Alpha", StringComparison.Ordinal) && key[5] >= '0' && key[5] <= '9') return true;
            if (key.Length == 6 && key.StartsWith("Mouse", StringComparison.Ordinal) && key[5] >= '0' && key[5] <= '6') return true;
            return new[] { "LeftAlt", "RightAlt", "LeftControl", "RightControl", "LeftShift", "RightShift", "Space", "Escape", "Equals", "Minus", "Tab", "BackQuote", "Comma", "Period", "Slash", "Semicolon", "LeftBracket", "RightBracket" }.Contains(key);
        }
        private static void NoText(XElement element) { Need(!element.Nodes().OfType<XText>().Any(t => !string.IsNullOrWhiteSpace(t.Value)), "Unexpected configuration text."); }
        private static bool Fail(string message, out string error) { error = message; return false; }
        private static void Need(bool valid, string message) { if (!valid) throw new InvalidDataException("Fishing content: " + message); }
    }
}
