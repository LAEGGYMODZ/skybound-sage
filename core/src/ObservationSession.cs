using System;

namespace SkyboundSage
{
    // This is our own integration boundary, NOT a Wukong or loader API.
    // A future adapter must verify hit ownership, success, IDs and lifecycle hooks.
    public sealed class ObservationSession
    {
        private readonly CombatRules rules;
        private long generation = 1;
        private bool closed;
        public long Generation { get { return generation; } }
        public bool Closed { get { return closed; } }

        public ObservationSession() { rules = new CombatRules(); }
        public CombatSnapshot Snapshot() { return rules.Snapshot(); }

        private EventResult Check(long eventGeneration, bool confirmed)
        {
            if (closed) return EventResult.Reject("session closed");
            if (eventGeneration != generation) return EventResult.Reject("stale session");
            if (!confirmed) return EventResult.Reject("unconfirmed event");
            return null;
        }

        public EventResult ConfirmHit(long eventGeneration, bool confirmed, string attackId,
            string targetId, Move move, HitSource source, long atMs)
        {
            EventResult refused = Check(eventGeneration, confirmed);
            return refused ?? rules.Hit(attackId, targetId, move, source, atMs);
        }

        public EventResult ConfirmDodge(long eventGeneration, bool confirmed, string eventId,
            bool perfect, HitSource source, long atMs)
        {
            EventResult refused = Check(eventGeneration, confirmed);
            return refused ?? rules.Dodge(eventId, perfect, source, atMs);
        }

        public EventResult ConfirmPlayerDamage(long eventGeneration, bool confirmed, long atMs)
        {
            EventResult refused = Check(eventGeneration, confirmed);
            if (refused != null) return refused;
            rules.TakeHit(atMs); return new EventResult(true, "", false, 0);
        }

        public CombatSnapshot Tick(long atMs)
        { return closed ? rules.Snapshot() : rules.Advance(atMs); }

        // First integration is observational. No activation input or game modifier is exposed.
        public CombatSnapshot LifecycleReset(string reason, long atMs)
        {
            if (closed) return rules.Snapshot();
            long next = checked(generation + 1);
            CombatSnapshot state = rules.Reset(reason, atMs);
            generation = next; return state;
        }

        public CombatSnapshot SetEnabled(bool enabled, long atMs)
        {
            if (closed) return rules.Snapshot();
            long next = checked(generation + 1);
            CombatSnapshot state = rules.SetEnabled(enabled, atMs);
            generation = next; return state;
        }

        public void Shutdown(long atMs)
        {
            if (closed) return;
            rules.SetEnabled(false, atMs);
            rules.Reset("shutdown", atMs);
            closed = true;
        }
    }
}
