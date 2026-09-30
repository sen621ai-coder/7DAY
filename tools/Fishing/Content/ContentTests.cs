using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml.Linq;
using System.Globalization;
using PZAEC.Fishing.Content;
using PZAEC.Fishing.Contracts;

internal static class ContentTests
{
    private static int checks;
    private static void Check(bool value, string name) { if (!value) throw new Exception(name); checks++; }
    private static FishingConfig Read(FishingContent content, string xml)
    {
        using (var stream = new MemoryStream(Encoding.UTF8.GetBytes(xml)))
        {
            var config = content.LoadStream(stream);
            Check(stream.CanRead, "Caller stream ownership");
            return config;
        }
    }
    private static void Reject(FishingContent content, string xml, string label)
    {
        bool rejected = false;
        try { Read(content, xml); } catch (InvalidDataException) { rejected = true; } catch (System.Xml.XmlException) { rejected = true; }
        Check(rejected, "Accepted invalid XML: " + label);
    }
    private static string Change(string xml, string section, string field, string value)
    {
        var doc = XDocument.Parse(xml);
        doc.Root.Element(section).SetAttributeValue(field, value);
        return doc.ToString();
    }
    public static int Main(string[] args)
    {
        string mod = Path.GetFullPath(args[0]);
        string xml = File.ReadAllText(Path.Combine(mod, FishingContract.ConfigFile));
        var content = new FishingContent();
        var valid = new CatchResult { SessionId = Guid.NewGuid(), SettlementId = Guid.NewGuid(), PlayerPersistentId = "fixture", FishDefinitionId = FishingContract.FishDefinition, MassKg = 3, TerminalTick = 120 };
        RewardSpec reward;
        Check(!content.TryGetReward(valid, out reward), "Reward before load");
        var config = content.Load(mod);
        string error;
        Check(content.Validate(config, out error), "Shipped config: " + error);
        Check(content.TryGetReward(valid, out reward) && reward.ItemId == FishingContract.FishItem && reward.Count == 1 && reward.FishMassKg == 3, "One fish reward");
        Check(content.TryGetReward(valid, out reward), "Lookup is stateless; A owns deduplication");
        Check(!content.Validate(null, out error), "Null config");

        var oldCulture = CultureInfo.CurrentCulture;
        try { CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR"); Check(Read(content, xml).Rod.LengthMeters == 2.4f, "Invariant numeric parsing"); }
        finally { CultureInfo.CurrentCulture = oldCulture; }
        var another = content.Load(mod);
        another.Fish.MassKg = 10;
        Check(config.Fish.MassKg == 3 && content.TryGetReward(valid, out reward), "Returned DTO mutation must not alter catalog reward mass");

        Reject(content, xml.Replace("version=\"1\"", "version=\"2\""), "version");
        Check(!content.TryGetReward(valid, out reward), "Failed reload invalidates lookup");
        Reject(content, Change(xml, "Line", "BreakForceNewtons", "NaN"), "NaN");
        Reject(content, Change(xml, "Fish", "MassKg", "Infinity"), "infinity");
        Reject(content, Change(xml, "Fish", "MassKg", "0"), "zero mass");
        Reject(content, Change(xml, "Fish", "MassKg", "3,5"), "locale number");
        Reject(content, Change(xml, "Controls", "AutoBackEnabled", "yes"), "boolean");
        Reject(content, Change(xml, "Controls", "ReelKey", "Mous1"), "unknown key");
        Reject(content, Change(xml, "Controls", "ReelKey", "Mouse0"), "key conflict");
        Reject(content, Change(xml, "Fish", "Id", "shark"), "fish id");
        Reject(content, Change(xml, "Line", "DragMinNewtons", "80"), "drag order");
        Reject(content, Change(xml, "Line", "BreakForceNewtons", "60"), "drag above break");
        Reject(content, Change(xml, "Fish", "NibbleMinSeconds", "5"), "timing order");
        Reject(content, Change(xml, "Fish", "BurstForceNewtons", "5"), "burst weaker than cruise");
        Reject(content, Change(xml, "Session", "MaxCastMeters", "80"), "cast beyond spool");
        Reject(content, Change(xml, "Session", "MaxCatchUpTicks", "1.5"), "fractional tick count");
        Reject(content, Change(xml, "Float", "MassKg", ".5"), "sinking float");
        Reject(content, Change(xml, "Rod", "LengthMeters", null), "missing attribute");
        Reject(content, Change(xml, "Rod", "SpellingError", "10"), "unknown attribute");
        var changed = XDocument.Parse(xml); changed.Root.Add(new XElement(changed.Root.Element("Rod")));
        Reject(content, changed.ToString(), "duplicate section");
        changed = XDocument.Parse(xml); changed.Root.Element("Hook").Remove();
        Reject(content, changed.ToString(), "missing section");
        Reject(content, xml.Replace("<Rod ", "<Rod unknown=\"1\" "), "unknown field");
        Reject(content, "<!DOCTYPE fishingConfig [<!ENTITY x SYSTEM 'file:///not-readable'>]>" + XDocument.Parse(xml), "DTD");
        Reject(content, xml.Replace("<fishingConfig version=\"1\">", "<fishingConfig version=\"1\">unrecognized"), "unexpected text");

        // Validate external mutable DTOs too, not just XML input.
        foreach (var section in typeof(FishingConfig).GetFields().Where(f => f.Name != "Version"))
        {
            var broken = content.Load(mod); section.SetValue(broken, null);
            Check(!content.Validate(broken, out error), "Null section " + section.Name);
            foreach (var field in section.FieldType.GetFields().Where(f => f.FieldType == typeof(float)))
            {
                broken = content.Load(mod); field.SetValue(section.GetValue(broken), float.NaN);
                Check(!content.Validate(broken, out error), "NaN DTO " + section.Name + "." + field.Name);
            }
        }
        content.Load(mod);
        var invalid = valid; invalid.SessionId = Guid.Empty; Check(!content.TryGetReward(invalid, out reward), "Empty session");
        invalid = valid; invalid.SettlementId = Guid.Empty; Check(!content.TryGetReward(invalid, out reward), "Empty settlement");
        invalid = valid; invalid.PlayerPersistentId = " "; Check(!content.TryGetReward(invalid, out reward), "Empty player");
        invalid = valid; invalid.FishDefinitionId = "other"; Check(!content.TryGetReward(invalid, out reward), "Unknown fish");
        invalid = valid; invalid.MassKg = float.NaN; Check(!content.TryGetReward(invalid, out reward), "Invalid mass");
        invalid = valid; invalid.MassKg = 99; Check(!content.TryGetReward(invalid, out reward), "Unconfigured mass");
        invalid = valid; invalid.TerminalTick = -1; Check(!content.TryGetReward(invalid, out reward), "Invalid terminal tick");
        Check(content.TryGetReward(valid, out reward) && reward.Count == 1, "Valid reward still available");
        Console.WriteLine("PASS: " + checks + " content checks; no game or inventory side effects. Runtime=" + Environment.Version);
        return 0;
    }
}
