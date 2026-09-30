using System;
using PZAEC.Fishing.Controls;

internal static class ControlTests
{
    static int count;
    static long seq;
    static ControlFrame Frame(double seconds = 1.0 / 60)
    {
        return new ControlFrame { Sequence = ++seq, Seconds = seconds, SessionActive = true, CanControl = true, HasFocus = true };
    }
    static void Assert(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }
    static void Near(double a, double b, string message, double tolerance = 1e-9)
    {
        Assert(Math.Abs(a - b) <= tolerance, message + ": " + a + " vs " + b);
    }
    static void Test(string name, Action body)
    {
        body(); count++; Console.WriteLine("PASS " + name);
    }
    static ControlLoad Heavy()
    {
        return new ControlLoad { LineTaut = true, TensionNewtons = 100, PullForward = 1 };
    }
    static ControlSettings HighRod()
    {
        return new ControlSettings { InitialPitch = 1.2 };
    }
    static ControlIntent Stroke(int hz, double sensitivity = 1, double scale = 1)
    {
        var mapper = new FishingControlMapper(new ControlSettings { Sensitivity = sensitivity });
        ControlIntent output = default(ControlIntent);
        for (int i = 0; i < hz; i++)
        {
            var frame = Frame(1.0 / hz);
            frame.MousePull = 100 * scale / hz; frame.MouseSide = 70 * scale / hz;
            output = mapper.Sample(frame, default(ControlLoad));
        }
        return output;
    }
    static int Main()
    {
        Test("Mouse displacement independent of frame rate", () => {
            var baseline = Stroke(30);
            foreach (int hz in new[] { 60, 144, 240 }) {
                var result = Stroke(hz);
                Near(result.PitchRadians, baseline.PitchRadians, "pitch");
                Near(result.SideRadians, baseline.SideRadians, "side");
            }
        });
        Test("Sensitivity compensates mouse resolution", () => {
            Near(Stroke(60, 0.5, 2).PitchRadians, Stroke(60).PitchRadians, "sensitivity");
        });
        Test("Tension resists raising, permits sending rod", () => {
            var f = Frame(); f.MousePull = 10;
            var loaded = new FishingControlMapper(); var slack = new FishingControlMapper();
            var a = loaded.Sample(f, Heavy()); var b = slack.Sample(f, default(ControlLoad));
            Assert(a.PitchRadians < b.PitchRadians, "loaded rod should respond less");
            f = Frame(); f.MousePull = -10;
            Near(a.PitchRadians - loaded.Sample(f, Heavy()).PitchRadians, 0.03, "send rod");
        });
        Test("Slack line does not slow movement", () => {
            var f = Frame(); f.MoveForward = -1;
            var load = Heavy(); load.LineTaut = false;
            Near(new FishingControlMapper().Sample(f, load).MoveForward, -1, "slack");
        });
        Test("Movement resistance follows pull direction", () => {
            var m = new FishingControlMapper(); var f = Frame(); f.MoveForward = -1;
            Near(m.Sample(f, Heavy()).MoveForward, -0.2, "backward against fish");
            f = Frame(); f.MoveForward = 1;
            Near(m.Sample(f, Heavy()).MoveForward, 1, "toward fish");
            f = Frame(); f.MoveRight = 1;
            Near(m.Sample(f, Heavy()).MoveRight, 1, "sideways");
            var load = Heavy(); load.PullForward = -1;
            f = Frame(); f.MoveForward = -1;
            Near(m.Sample(f, load).MoveForward, -1, "fish behind");
        });
        Test("Backstep starts only near rod limit", () => {
            var m = new FishingControlMapper(); var f = Frame(); f.MousePull = 5;
            Near(m.Sample(f, default(ControlLoad)).AutoBackstep, 0, "low rod");
            m = new FishingControlMapper(HighRod()); f = Frame(); f.MousePull = 5;
            Assert(m.Sample(f, default(ControlLoad)).MoveForward < 0, "high rod backstep");
        });
        Test("Mouse stop drains backstep within release time at all frame rates", () => {
            foreach (int hz in new[] { 30, 60, 144 }) {
                var m = new FishingControlMapper(HighRod()); var f = Frame(1.0 / hz); f.MousePull = 400.0 / hz;
                m.Sample(f, Heavy()); ControlIntent result = default(ControlIntent);
                for (int i = 0; i < Math.Ceiling(0.12 * hz); i++) result = m.Sample(Frame(1.0 / hz), Heavy());
                Near(result.AutoBackstep, 0, "release"); Near(result.MoveForward, 0, "feet stop");
            }
        });
        Test("Forward key overrides auto retreat; manual retreat is not doubled", () => {
            var m = new FishingControlMapper(HighRod()); var f = Frame(); f.MousePull = 20; f.MoveForward = 1;
            Near(m.Sample(f, Heavy()).MoveForward, 1, "forward priority");
            f = Frame(); f.MousePull = 20; f.MoveForward = -1;
            Near(m.Sample(f, Heavy()).MoveForward, -0.2, "single load application");
        });
        Test("Auto backstep can be disabled", () => {
            var settings = HighRod(); settings.AutoBackstep = false;
            var f = Frame(); f.MousePull = 50;
            Near(new FishingControlMapper(settings).Sample(f, Heavy()).AutoBackstep, 0, "disabled");
        });
        Test("Diagonal movement stays bounded", () => {
            var f = Frame(); f.MoveForward = -1; f.MoveRight = 1;
            var r = new FishingControlMapper().Sample(f, default(ControlLoad));
            Near(Math.Sqrt(r.MoveForward * r.MoveForward + r.MoveRight * r.MoveRight), 1, "diagonal");
        });
        Test("Menu immediately releases and discards stale movement", () => {
            var m = new FishingControlMapper(HighRod()); var f = Frame(); f.MousePull = 20;
            var before = m.Sample(f, Heavy()); f = Frame(); f.MenuOrChatOpen = true; f.MousePull = 10000; f.ReelHeld = true;
            var r = m.Sample(f, Heavy());
            Assert(!r.OwnsRodInput && !r.SuppressLook && !r.ReelHeld, "release input");
            Near(r.AutoBackstep, 0, "clear feet"); Near(r.PitchRadians, before.PitchRadians, "discard menu mouse");
            f = Frame(); f.MousePull = 10000; f.ReelHeld = true;
            r = m.Sample(f, Heavy()); Near(r.PitchRadians, before.PitchRadians, "discard resume jump");
            Assert(!r.ReelHeld, "held reel cannot leak out of UI");
            f = Frame(); f.MousePull = -5; f.ReelHeld = true;
            r = m.Sample(f, Heavy()); Assert(r.PitchRadians < before.PitchRadians && !r.ReelHeld, "rod resumes while held action waits");
            m.Sample(Frame(), Heavy()); f = Frame(); f.ReelHeld = true;
            Assert(m.Sample(f, Heavy()).ReelHeld, "release then press resumes reel");
        });
        Test("Free look and mouse recenter preserve pose and stop retreat", () => {
            foreach (bool look in new[] { true, false }) {
                var m = new FishingControlMapper(HighRod()); var f = Frame(); f.MousePull = 20;
                var before = m.Sample(f, Heavy()); f = Frame(); f.FreeLook = look; f.Recenter = !look; f.MousePull = -900;
                var r = m.Sample(f, Heavy()); Near(r.PitchRadians, before.PitchRadians, "clutched pose");
                Near(r.AutoBackstep, 0, "clutched feet"); Assert(r.SuppressLook == !look, "look ownership");
                f = Frame(); f.MousePull = 900; r = m.Sample(f, Heavy()); Near(r.PitchRadians, before.PitchRadians, "resume pose");
            }
        });
        Test("Lifecycle loss and cancellation release controls", () => {
            for (int reason = 0; reason < 5; reason++) {
                var m = new FishingControlMapper(HighRod()); var f = Frame(); f.MousePull = 20; m.Sample(f, Heavy());
                f = Frame(); f.ReelHeld = true;
                if (reason == 0) f.CanControl = false;
                if (reason == 1) f.SessionActive = false;
                if (reason == 2) f.HasFocus = false;
                if (reason == 3) f.CancelPressed = true;
                if (reason == 4) f.Seconds = 1;
                var r = m.Sample(f, Heavy());
                Assert(!r.OwnsRodInput && !r.ReelHeld, "lifecycle gate"); Near(r.MoveForward, 0, "lifecycle feet");
            }
        });
        Test("Drag adjusts per second and remains bounded", () => {
            foreach (int hz in new[] { 30, 144 }) {
                var m = new FishingControlMapper(); ControlIntent r = default(ControlIntent);
                for (int i = 0; i < hz; i++) { var f = Frame(1.0 / hz); f.DragAxis = 1; r = m.Sample(f, Heavy()); }
                Near(r.DragFraction, 0.9, "drag per second");
            }
        });
        Test("No repeated sequence can apply mouse delta twice", () => {
            var m = new FishingControlMapper(); var f = Frame(); m.Sample(f, Heavy());
            bool threw = false; try { m.Sample(f, Heavy()); } catch (ArgumentException) { threw = true; }
            Assert(threw, "duplicate sequence rejected");
        });
        Test("Invalid data cannot produce NaN outputs", () => {
            var f = Frame(); f.MousePull = double.NaN; f.MouseSide = double.PositiveInfinity; f.MoveRight = double.NaN;
            var load = Heavy(); load.TensionNewtons = double.NaN; load.PullRight = double.PositiveInfinity;
            var r = new FishingControlMapper().Sample(f, load);
            Assert(double.IsFinite(r.PitchRadians) && double.IsFinite(r.MoveRight) && double.IsFinite(r.MoveForward), "finite output");
            bool threw = false; try { new FishingControlMapper(new ControlSettings { Sensitivity = double.NaN }); } catch (ArgumentException) { threw = true; }
            Assert(threw, "invalid settings rejected");
        });
        Test("Reset clears session state", () => {
            var m = new FishingControlMapper(HighRod()); var f = Frame(); f.MousePull = 100; f.DragAxis = 1; m.Sample(f, Heavy());
            m.Reset(); var r = m.Sample(Frame(), Heavy());
            Near(r.PitchRadians, 1.2, "pose reset"); Near(r.DragFraction, 0.5, "drag reset"); Near(r.AutoBackstep, 0, "feet reset");
        });
        Test("Fast render frames preserve strike until one simulation tick", () => {
            var b = new ControlTickBuffer();
            b.Publish(new ControlIntent { Sequence = 1, OwnsRodInput = true, StrikePressed = true, PitchRadians = 0.5 });
            b.Publish(new ControlIntent { Sequence = 2, OwnsRodInput = true, ReelHeld = true, PitchRadians = 0.6 });
            var first = b.Consume(); var second = b.Consume();
            Assert(first.StrikePressed && !second.StrikePressed, "edge once");
            Assert(first.ReelHeld && second.ReelHeld, "held state across ticks");
            Near(second.PitchRadians, 0.6, "absolute pose not delta replay");
        });
        Test("Control loss clears queued strike; cancellation survives until consumed", () => {
            var b = new ControlTickBuffer();
            b.Publish(new ControlIntent { Sequence = 1, OwnsRodInput = true, StrikePressed = true });
            b.Publish(new ControlIntent { Sequence = 2, CancelRequested = true });
            b.Publish(new ControlIntent { Sequence = 3 });
            var first = b.Consume(); Assert(first.CancelRequested && !first.StrikePressed, "cancel supersedes strike");
            Assert(!b.Consume().CancelRequested, "cancel edge once");
        });
        Test("Tick bridge rejects duplicate publication and resets", () => {
            var b = new ControlTickBuffer(); b.Publish(new ControlIntent { Sequence = 1 });
            bool threw = false; try { b.Publish(new ControlIntent { Sequence = 1 }); } catch (ArgumentException) { threw = true; }
            Assert(threw, "duplicate publish"); b.Reset(); b.Publish(new ControlIntent { Sequence = 1 });
            Assert(!b.Consume().OwnsRodInput, "reset neutral");
        });
        Console.WriteLine("Passed " + count + " control behavior tests.");
        ContractAdapterTests.Run();
        return 0;
    }
}
