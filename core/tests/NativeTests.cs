using System;
using System.Globalization;
using System.IO;
using SkyboundSage;

internal static class NativeTests
{
    private static int passed, failed;
    private static int dodgeId;
    private static readonly CultureInfo Culture = CultureInfo.InvariantCulture;
    private static void Assert(bool condition, string message) { if (!condition) throw new Exception(message); }
    private static void Near(double actual, double expected) { Assert(Math.Abs(actual - expected) < 0.0000001, actual + " != " + expected); }
    private static void Equal<T>(T actual, T expected) { Assert(Object.Equals(actual, expected), actual + " != " + expected); }
    private static void Throws<T>(Action action) where T : Exception
    { try { action(); } catch (T) { return; } throw new Exception("Expected " + typeof(T).Name); }
    private static void Test(string name, Action action)
    {
        try { action(); passed++; Console.WriteLine("PASS " + name); }
        catch (Exception error) { failed++; Console.WriteLine("FAIL " + name + ": " + error.Message); }
    }
    private static EventResult Hit(CombatRules rules, string id, Move move, long at)
    { return rules.Hit(id, "enemy-1", move, HitSource.Player, at); }
    private static void Charge(CombatRules rules, long start)
    { for (int i = 0; i < 5; i++) rules.Dodge("d" + (++dodgeId), true, HitSource.Player, start + i * 10); }

    private static int Main(string[] args)
    {
        if (args.Length == 2 && args[0] == "--trace") { Trace(args[1]); return 0; }
        if (args.Length != 0) { Console.Error.WriteLine("Use no arguments for tests, or --trace with a local sample trace."); return 2; }

        Test("Variety earns more style than repeated light attacks", delegate {
            var varied = new CombatRules(); var repeat = new CombatRules();
            var moves = new[] { Move.Light, Move.Heavy, Move.Aerial, Move.Counter };
            for (int i = 0; i < 4; i++) { Hit(varied, i.ToString(), moves[i], i * 100); Hit(repeat, i.ToString(), Move.Light, i * 100); }
            Near(varied.Snapshot().StyleScore, 125); Near(repeat.Snapshot().StyleScore, 35);
            Assert(varied.Snapshot().FuryCharge > repeat.Snapshot().FuryCharge, "Variety should charge faster.");
        });
        Test("Duplicate callbacks and clone/echo hits cannot inflate score", delegate {
            var r = new CombatRules(); Hit(r, "one", Move.Heavy, 0);
            Equal(Hit(r, "one", Move.Heavy, 50).Accepted, false);
            Equal(r.Hit("clone", "enemy-1", Move.Counter, HitSource.Clone, 100).Accepted, false);
            Equal(r.Hit("echo", "enemy-1", Move.Counter, HitSource.Echo, 200).Accepted, false);
            Near(r.Snapshot().StyleScore, 30);
            Equal(r.Hit("one", "enemy-2", Move.Heavy, HitSource.Player, 300).Accepted, true);
        });
        Test("Ordinary dodge gives no credit; confirmed perfect dodge does", delegate {
            var r = new CombatRules(); r.Dodge("normal", false, HitSource.Player, 0); Near(r.Snapshot().StyleScore, 0);
            r.Dodge("perfect", true, HitSource.Player, 10); Near(r.Snapshot().StyleScore, 25); Near(r.Snapshot().FuryCharge, 20);
        });
        Test("Fury requires charge and expires exactly at six seconds", delegate {
            var r = new CombatRules(); Equal(r.ActivateFury(0).Accepted, false); Charge(r, 0);
            Equal(r.ActivateFury(100).Accepted, true); Near(r.Snapshot().RequestedAttackMultiplier, 1.15);
            Equal(r.Advance(6099).FuryState, "active"); Equal(r.Advance(6100).FuryState, "cooldown"); Near(r.Snapshot().RequestedAttackMultiplier, 1);
        });
        Test("New charge cannot bypass Fury cooldown", delegate {
            var r = new CombatRules(); Charge(r, 0); r.ActivateFury(100); Charge(r, 6100);
            Equal(r.ActivateFury(6500).Accepted, false); Equal(r.ActivateFury(18100).Accepted, true);
        });
        Test("Death/load/rest/shutdown resets clear temporary state", delegate {
            foreach (string reason in new[] { "death", "load", "rest", "shutdown" }) {
                var r = new CombatRules(); Charge(r, 0); r.ActivateFury(100); var s = r.Reset(reason, 200);
                Near(s.StyleScore, 0); Near(s.FuryCharge, 0); Near(s.RequestedAttackMultiplier, 1);
                Equal(s.CooldownRemainingMs, 0L); Equal(s.LastResetReason, reason);
            }
        });
        Test("Disabling rules clears Fury and prevents scoring", delegate {
            var r = new CombatRules(); Charge(r, 0); r.ActivateFury(100); r.SetEnabled(false, 200);
            Hit(r, "off", Move.Counter, 300); r.Dodge("off", true, HitSource.Player, 400);
            Near(r.Snapshot().StyleScore, 0); Near(r.Snapshot().RequestedAttackMultiplier, 1);
            r.SetEnabled(true, 500); Near(r.Snapshot().FuryCharge, 0);
        });
        Test("Fury switch leaves style scoring available", delegate {
            var r = new CombatRules(); Charge(r, 0); r.ActivateFury(100); r.SetFeature(Feature.Fury, false, 200);
            double old = r.Snapshot().StyleScore; Hit(r, "style", Move.Heavy, 300);
            Assert(r.Snapshot().StyleScore > old, "Style was lost."); Equal(r.Snapshot().FuryState, "off"); Near(r.Snapshot().RequestedAttackMultiplier, 1);
        });
        Test("Idle decay begins after the grace period", delegate {
            var r = new CombatRules(); Hit(r, "start", Move.Counter, 0);
            Near(r.Advance(3000).StyleScore, 40); Near(r.Advance(4000).StyleScore, 15); Near(r.Advance(5000).StyleScore, 0);
        });
        Test("Bad IDs, enums and backward clocks grant no score", delegate {
            var r = new CombatRules(); Throws<ArgumentException>(delegate { Hit(r, "bad", (Move)999, 0); });
            Throws<ArgumentException>(delegate { Hit(r, new string('a', 101), Move.Light, 0); });
            Throws<ArgumentException>(delegate { r.Hit("bad", "enemy", Move.Light, (HitSource)999, 0); });
            Throws<ArgumentOutOfRangeException>(delegate { r.Advance(-1); }); r.Advance(100);
            Throws<ArgumentOutOfRangeException>(delegate { r.Advance(99); }); Near(r.Snapshot().StyleScore, 0);
        });
        Test("Perfect dodge callbacks award once", delegate {
            var r = new CombatRules(); r.Dodge("evade", true, HitSource.Player, 0);
            Equal(r.Dodge("evade", true, HitSource.Player, 10).Accepted, false); Near(r.Snapshot().StyleScore, 25);
        });
        Test("Hit and dodge IDs are independent", delegate {
            var r = new CombatRules(); r.Hit("dodge", "same", Move.Light, HitSource.Player, 0);
            Equal(r.Dodge("same", true, HitSource.Player, 10).Accepted, true); Near(r.Snapshot().StyleScore, 45);
        });
        Test("Long session keeps scores, charge and event cache bounded", delegate {
            var r = new CombatRules(); for (int i = 0; i < 10000; i++) Hit(r, i.ToString(), (Move)(i % 4), i * 25);
            var s = r.Snapshot(); Assert(s.StyleScore >= 0 && s.StyleScore <= 1000, "Score out of range.");
            Assert(s.FuryCharge >= 0 && s.FuryCharge <= 100, "Charge out of range."); Assert(r.RecentEventCount <= 512, "Cache overflow.");
        });
        Test("Full cache refuses new credit without forgetting duplicates", delegate {
            var r = new CombatRules(new CombatConfig { RecentEventLimit = 2 }); Hit(r, "one", Move.Light, 0); Hit(r, "two", Move.Heavy, 10);
            Equal(Hit(r, "three", Move.Counter, 20).Reason, "hit window full"); Equal(Hit(r, "one", Move.Light, 30).Reason, "duplicate hit");
            Near(r.Snapshot().StyleScore, 50); Equal(Hit(r, "fresh", Move.Counter, 10000).Accepted, true);
        });
        Test("Caller configuration cannot change an existing rules instance", delegate {
            var c = new CombatConfig(); var r = new CombatRules(c); c.FuryAttackMultiplier = 99; c.StyleEnabled = false;
            Charge(r, 0); r.ActivateFury(100); Near(r.Snapshot().RequestedAttackMultiplier, 1.15); Assert(r.Snapshot().StyleScore > 0, "Mutable configuration leaked.");
            Throws<ArgumentOutOfRangeException>(delegate { new CombatRules(new CombatConfig { StyleCap = Double.NaN }); });
            Throws<ArgumentOutOfRangeException>(delegate { new CombatRules(new CombatConfig { RecentEventLimit = Int32.MaxValue }); });
        });
        Test("IDs containing separators do not collide", delegate {
            var r = new CombatRules(); r.Hit("a:b", "c", Move.Light, HitSource.Player, 0);
            Equal(r.Hit("a", "b:c", Move.Heavy, HitSource.Player, 1).Accepted, true); Near(r.Snapshot().StyleScore, 50);
        });
        Test("Timer overflow refuses activation without consuming charge", delegate {
            var r = new CombatRules(); Charge(r, Int64.MaxValue - 100);
            Throws<OverflowException>(delegate { r.ActivateFury(Int64.MaxValue - 50); });
            Near(r.Snapshot().FuryCharge, 100); Near(r.Snapshot().RequestedAttackMultiplier, 1);
        });
        Test("Old callbacks cannot cross a lifecycle reset", delegate {
            var s = new ObservationSession(); long old = s.Generation;
            s.ConfirmHit(old, true, "one", "enemy", Move.Heavy, HitSource.Player, 0); s.LifecycleReset("load", 100);
            Equal(s.ConfirmHit(old, true, "late", "enemy", Move.Heavy, HitSource.Player, 100000).Reason, "stale session");
            Equal(s.ConfirmHit(s.Generation, true, "new", "enemy", Move.Light, HitSource.Player, 101).Accepted, true); Near(s.Snapshot().StyleScore, 20);
        });
        Test("Unconfirmed button-like events cannot earn style", delegate {
            var s = new ObservationSession(); Equal(s.ConfirmDodge(s.Generation, false, "button", true, HitSource.Player, 900000).Reason, "unconfirmed event");
            Equal(s.ConfirmHit(s.Generation, false, "press", "enemy", Move.Counter, HitSource.Player, 900000).Accepted, false);
            Equal(s.ConfirmHit(s.Generation, true, "real", "enemy", Move.Light, HitSource.Player, 0).Accepted, true); Near(s.Snapshot().StyleScore, 20);
        });
        Test("Disable/re-enable rejects callbacks from the earlier enabled session", delegate {
            var s = new ObservationSession(); long old = s.Generation; s.SetEnabled(false, 10); s.SetEnabled(true, 20);
            Equal(s.ConfirmDodge(old, true, "old", true, HitSource.Player, 30).Accepted, false); Near(s.Snapshot().FuryCharge, 0);
            Equal(s.ConfirmDodge(s.Generation, true, "new", true, HitSource.Player, 30).Accepted, true);
        });
        Test("Shutdown is idempotent and permanently closes event intake", delegate {
            var s = new ObservationSession(); s.ConfirmDodge(s.Generation, true, "d", true, HitSource.Player, 0);
            s.Shutdown(100); s.Shutdown(0); Equal(s.Closed, true); Equal(s.Snapshot().LastResetReason, "shutdown");
            Equal(s.ConfirmHit(s.Generation, true, "late", "enemy", Move.Light, HitSource.Player, 100000).Reason, "session closed");
            Near(s.Snapshot().StyleScore, 0); Near(s.Snapshot().RequestedAttackMultiplier, 1);
        });
        Test("A rejected backward reset does not invalidate the current session", delegate {
            var s = new ObservationSession(); s.Tick(100); long old = s.Generation;
            Throws<ArgumentOutOfRangeException>(delegate { s.LifecycleReset("load", 99); }); Equal(s.Generation, old);
            Equal(s.ConfirmHit(old, true, "real", "enemy", Move.Light, HitSource.Player, 101).Accepted, true);
        });
        Console.WriteLine("RESULT " + passed + " passed, " + failed + " failed"); return failed == 0 ? 0 : 1;
    }

    // Local sample traces support cross-language verification. This does not read game data.
    private static void Trace(string path)
    {
        var rules = new CombatRules(); int index = 0;
        foreach (string line in File.ReadAllLines(path)) {
            string[] p = line.Split('\t'); long at = Int64.Parse(p[1], Culture);
            switch (p[0]) {
                case "H": rules.Hit(p[2], p[3], (Move)Enum.Parse(typeof(Move), p[4], true), (HitSource)Enum.Parse(typeof(HitSource), p[5], true), at); break;
                case "D": rules.Dodge(p[2], p[3] == "1", HitSource.Player, at); break;
                case "F": rules.ActivateFury(at); break;
                case "T": rules.TakeHit(at); break;
                case "R": rules.Reset(p[2], at); break;
                case "E": rules.SetEnabled(p[2] == "1", at); break;
                case "S": rules.SetFeature((Feature)Enum.Parse(typeof(Feature), p[2], true), p[3] == "1", at); break;
                case "A": rules.Advance(at); break;
                default: throw new ArgumentException("Unknown trace operation.");
            }
            CombatSnapshot s = rules.Snapshot();
            Console.WriteLine(String.Join("\t", new[] { (index++).ToString(Culture), s.Enabled ? "1" : "0", s.StyleEnabled ? "1" : "0", s.FuryEnabled ? "1" : "0",
                s.StyleScore.ToString("R", Culture), s.Rank, s.FuryCharge.ToString("R", Culture), s.FuryState,
                s.FuryRemainingMs.ToString(Culture), s.CooldownRemainingMs.ToString(Culture), s.RequestedAttackMultiplier.ToString("R", Culture), s.LastResetReason }));
        }
    }
}
