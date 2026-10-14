using System;
using System.Collections.Generic;
using System.Globalization;

namespace SkyboundSage
{
    // Original portable rules. No game, controller, network or filesystem access.
    public enum Move { Light, Heavy, Aerial, Counter }
    public enum HitSource { Player, Clone, Echo }
    public enum Feature { Style, Fury }

    public sealed class CombatConfig
    {
        public bool StyleEnabled = true;
        public bool FuryEnabled = true;
        public double StyleCap = 1000;
        public long IdleGraceMs = 3000;
        public double DecayPerSecond = 25;
        public long FuryDurationMs = 6000;
        public long FuryCooldownMs = 12000;
        public double FuryAttackMultiplier = 1.15;
        public long DuplicateWindowMs = 10000;
        public int RecentEventLimit = 512;

        internal CombatConfig CopyAndValidate()
        {
            if (!Finite(StyleCap) || StyleCap < 1 || !Finite(DecayPerSecond) ||
                DecayPerSecond < 0 || !Finite(FuryAttackMultiplier) || FuryAttackMultiplier < 1 ||
                IdleGraceMs < 0 || FuryDurationMs < 1 || FuryCooldownMs < 0 ||
                DuplicateWindowMs < 0 || RecentEventLimit < 1 || RecentEventLimit > 65536)
                throw new ArgumentOutOfRangeException("config", "Invalid combat configuration.");
            return (CombatConfig)MemberwiseClone();
        }

        private static bool Finite(double value) { return !Double.IsNaN(value) && !Double.IsInfinity(value); }
    }

    public sealed class CombatSnapshot
    {
        public bool Enabled { get; private set; }
        public bool StyleEnabled { get; private set; }
        public bool FuryEnabled { get; private set; }
        public double StyleScore { get; private set; }
        public string Rank { get; private set; }
        public double FuryCharge { get; private set; }
        public string FuryState { get; private set; }
        public long FuryRemainingMs { get; private set; }
        public long CooldownRemainingMs { get; private set; }
        // Intent only. Nothing in this assembly applies damage or modifies a player.
        public double RequestedAttackMultiplier { get; private set; }
        public string LastResetReason { get; private set; }

        internal CombatSnapshot(bool enabled, CombatConfig config, double score, double charge,
            long now, long furyUntil, long cooldownUntil, string resetReason)
        {
            Enabled = enabled; StyleEnabled = config.StyleEnabled; FuryEnabled = config.FuryEnabled;
            StyleScore = Math.Round(score, 2, MidpointRounding.AwayFromZero);
            FuryCharge = Math.Round(charge, 2, MidpointRounding.AwayFromZero);
            Rank = score >= 800 ? "S" : score >= 600 ? "A" : score >= 400 ? "B" : score >= 200 ? "C" : "D";
            bool active = enabled && config.FuryEnabled && now < furyUntil;
            bool cooldown = enabled && config.FuryEnabled && !active && now < cooldownUntil;
            FuryState = !enabled || !config.FuryEnabled ? "off" : active ? "active" : cooldown ? "cooldown" : charge >= 100 ? "ready" : "charging";
            FuryRemainingMs = active ? furyUntil - now : 0;
            CooldownRemainingMs = cooldown ? cooldownUntil - now : 0;
            RequestedAttackMultiplier = active ? config.FuryAttackMultiplier : 1;
            LastResetReason = resetReason;
        }
    }

    public sealed class EventResult
    {
        public bool Accepted { get; private set; }
        public string Reason { get; private set; }
        public bool Repeated { get; private set; }
        public double ScoreAward { get; private set; }
        internal EventResult(bool accepted, string reason, bool repeated, double scoreAward)
        { Accepted = accepted; Reason = reason; Repeated = repeated; ScoreAward = scoreAward; }
        internal static EventResult Reject(string reason) { return new EventResult(false, reason, false, 0); }
    }

    public sealed class CombatRules
    {
        private readonly CombatConfig config;
        private readonly Dictionary<string, long> recentEvents = new Dictionary<string, long>(StringComparer.Ordinal);
        private bool enabled = true;
        private long nowMs, lastActionMs, furyUntilMs, cooldownUntilMs;
        private double styleScore, furyCharge;
        private string lastMove;
        private string lastResetReason = "start";
        public int RecentEventCount { get { return recentEvents.Count; } }

        public CombatRules() : this(new CombatConfig()) { }
        public CombatRules(CombatConfig options)
        {
            if (options == null) throw new ArgumentNullException("options");
            config = options.CopyAndValidate();
            Clear();
        }

        private void Clear()
        {
            styleScore = furyCharge = 0; furyUntilMs = cooldownUntilMs = 0;
            lastActionMs = nowMs; lastMove = null; recentEvents.Clear();
        }

        public CombatSnapshot Advance(long atMs)
        {
            if (atMs < nowMs) throw new ArgumentOutOfRangeException("atMs", "Clock must be nonnegative and monotonic.");
            // Subtraction avoids overflow from adding grace to a near-limit clock.
            if (enabled && config.StyleEnabled && atMs - lastActionMs > config.IdleGraceMs)
            {
                long decayStart = Math.Max(nowMs, lastActionMs + config.IdleGraceMs);
                styleScore = Math.Max(0, styleScore - ((atMs - decayStart) / 1000.0) * config.DecayPerSecond);
            }
            nowMs = atMs;
            var expired = new List<string>();
            foreach (var item in recentEvents)
                if (nowMs - item.Value >= config.DuplicateWindowMs) expired.Add(item.Key);
            foreach (string key in expired) recentEvents.Remove(key);
            return Snapshot();
        }

        private static void ValidateId(string value, string label)
        {
            if (String.IsNullOrEmpty(value) || value.Length > 100)
                throw new ArgumentException("A nonempty ID of at most 100 characters is required.", label);
        }

        // Length-prefixing makes IDs containing separators unambiguous.
        private static string HitKey(string attackId, string targetId)
        { return "H" + attackId.Length.ToString(CultureInfo.InvariantCulture) + ":" + attackId + targetId.Length.ToString(CultureInfo.InvariantCulture) + ":" + targetId; }

        private EventResult Reserve(string key, string duplicateReason)
        {
            if (recentEvents.ContainsKey(key)) return EventResult.Reject(duplicateReason);
            if (recentEvents.Count >= config.RecentEventLimit) return EventResult.Reject("hit window full");
            recentEvents.Add(key, nowMs); return null;
        }

        public EventResult Hit(string attackId, string targetId, Move move, HitSource source, long atMs)
        {
            ValidateId(attackId, "attackId"); ValidateId(targetId, "targetId");
            if (!Enum.IsDefined(typeof(Move), move) || !Enum.IsDefined(typeof(HitSource), source))
                throw new ArgumentException("Unknown move or source.");
            Advance(atMs);
            if (!enabled) return EventResult.Reject("disabled");
            if (source != HitSource.Player) return EventResult.Reject("secondary hit");
            EventResult refused = Reserve(HitKey(attackId, targetId), "duplicate hit");
            if (refused != null) return refused;
            string moveName = move.ToString();
            bool repeated = lastMove == moveName && nowMs - lastActionMs <= config.IdleGraceMs;
            double factor = repeated ? 0.25 : 1;
            double score = move == Move.Light ? 20 : move == Move.Heavy ? 30 : move == Move.Aerial ? 35 : 40;
            double charge = move == Move.Light ? 12 : move == Move.Heavy ? 18 : move == Move.Aerial ? 20 : 25;
            if (config.StyleEnabled) styleScore = Math.Min(config.StyleCap, styleScore + score * factor);
            if (config.FuryEnabled && nowMs >= furyUntilMs) furyCharge = Math.Min(100, furyCharge + charge * factor);
            lastMove = moveName; lastActionMs = nowMs;
            return new EventResult(true, "", repeated, config.StyleEnabled ? score * factor : 0);
        }

        public EventResult Dodge(string eventId, bool perfect, HitSource source, long atMs)
        {
            ValidateId(eventId, "eventId");
            if (source != HitSource.Player) throw new ArgumentException("Dodge requires an explicit player source.");
            Advance(atMs);
            if (!enabled || !perfect) return EventResult.Reject("no confirmed perfect dodge");
            EventResult refused = Reserve("D" + eventId, "duplicate dodge");
            if (refused != null) return refused;
            if (config.StyleEnabled) styleScore = Math.Min(config.StyleCap, styleScore + 25);
            if (config.FuryEnabled && nowMs >= furyUntilMs) furyCharge = Math.Min(100, furyCharge + 20);
            lastMove = "dodge"; lastActionMs = nowMs;
            return new EventResult(true, "", false, config.StyleEnabled ? 25 : 0);
        }

        public EventResult ActivateFury(long atMs)
        {
            Advance(atMs);
            if (!enabled || !config.FuryEnabled) return EventResult.Reject("Fury disabled");
            if (nowMs < furyUntilMs || nowMs < cooldownUntilMs) return EventResult.Reject("cooldown");
            if (furyCharge < 100) return EventResult.Reject("not charged");
            // Check both deadlines before consuming charge; overflow must not create a permanent effect.
            long expiry = checked(nowMs + config.FuryDurationMs);
            long cooldown = checked(expiry + config.FuryCooldownMs);
            furyCharge = 0; furyUntilMs = expiry; cooldownUntilMs = cooldown;
            return new EventResult(true, "", false, 0);
        }

        public void TakeHit(long atMs)
        {
            Advance(atMs); if (!enabled) return;
            styleScore = Math.Max(0, styleScore - 60); lastMove = null; lastActionMs = nowMs;
        }

        public CombatSnapshot Reset(string reason, long atMs)
        {
            Advance(atMs); Clear(); lastResetReason = String.IsNullOrEmpty(reason) ? "reset" : reason;
            return Snapshot();
        }

        public CombatSnapshot SetEnabled(bool value, long atMs)
        {
            Advance(atMs); enabled = value; Clear(); lastResetReason = value ? "enabled" : "disabled";
            return Snapshot();
        }

        public CombatSnapshot SetFeature(Feature feature, bool value, long atMs)
        {
            if (!Enum.IsDefined(typeof(Feature), feature)) throw new ArgumentException("Unknown feature.");
            Advance(atMs);
            if (feature == Feature.Style && config.StyleEnabled != value)
            { config.StyleEnabled = value; styleScore = 0; lastMove = null; lastActionMs = nowMs; }
            else if (feature == Feature.Fury && config.FuryEnabled != value)
            { config.FuryEnabled = value; furyCharge = 0; furyUntilMs = cooldownUntilMs = 0; }
            return Snapshot();
        }

        public CombatSnapshot Snapshot()
        { return new CombatSnapshot(enabled, config, styleScore, furyCharge, nowMs, furyUntilMs, cooldownUntilMs, lastResetReason); }
    }
}
