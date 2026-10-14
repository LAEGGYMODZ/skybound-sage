using System;
using System.Threading;

namespace SkyboundSageIntegration
{
    public enum SubscriptionState { Detached, Binding, Attached, Removing, Unknown, Closed }

    // Original lifecycle helper, not a Wukong API adapter. The caller must supply
    // verified single-callback add/remove operations and the exact owned delegate.
    public sealed class OwnedSubscription : IDisposable
    {
        private readonly Delegate callback;
        private readonly Action<Delegate> bindMine;
        private readonly Action<Delegate> unbindMine;
        private readonly int ownerThread;
        private SubscriptionState state;
        public SubscriptionState State { get { return state; } }

        public OwnedSubscription(Delegate ownedCallback,Action<Delegate> addOwned,Action<Delegate> removeOwned)
        {
            if(ownedCallback==null) throw new ArgumentNullException("ownedCallback");
            if(addOwned==null) throw new ArgumentNullException("addOwned");
            if(removeOwned==null) throw new ArgumentNullException("removeOwned");
            if(ownedCallback.GetInvocationList().Length!=1) throw new ArgumentException("One owned callback is required.","ownedCallback");
            callback=ownedCallback; bindMine=addOwned; unbindMine=removeOwned;
            ownerThread=Thread.CurrentThread.ManagedThreadId; state=SubscriptionState.Detached;
        }
        private void RequireOwnerThread()
        { if(Thread.CurrentThread.ManagedThreadId!=ownerThread) throw new InvalidOperationException("Subscription operations must be serialized on the owner thread."); }
        private void RejectTransition()
        { if(state==SubscriptionState.Binding || state==SubscriptionState.Removing) throw new InvalidOperationException("Reentrant subscription mutation refused."); }

        public bool Attach()
        {
            RequireOwnerThread(); RejectTransition();
            if(state==SubscriptionState.Closed) throw new ObjectDisposedException("OwnedSubscription");
            if(state==SubscriptionState.Attached) return false;
            if(state==SubscriptionState.Unknown) throw new InvalidOperationException("Cleanup must succeed before another binding attempt.");
            state=SubscriptionState.Binding;
            try { bindMine(callback); state=SubscriptionState.Attached; return true; }
            catch(Exception bindError) {
                // A thrown add may have registered the callback already. Try removing
                // that exact callback; never clear a shared delegate collection.
                state=SubscriptionState.Removing;
                try { unbindMine(callback); state=SubscriptionState.Detached; }
                catch(Exception cleanupError) {
                    state=SubscriptionState.Unknown;
                    throw new AggregateException("Binding and compensating cleanup both failed; state is unknown.",bindError,cleanupError);
                }
                throw;
            }
        }
        public bool Detach()
        {
            RequireOwnerThread(); RejectTransition();
            if(state==SubscriptionState.Closed || state==SubscriptionState.Detached) return false;
            state=SubscriptionState.Removing;
            try { unbindMine(callback); state=SubscriptionState.Detached; return true; }
            catch { state=SubscriptionState.Unknown; throw; }
        }
        public void Dispose()
        {
            RequireOwnerThread(); RejectTransition();
            if(state==SubscriptionState.Closed) return;
            Detach();
            // A failed removal throws and leaves Unknown, retaining the callback and
            // remover for a later owner-thread retry rather than claiming shutdown.
            state=SubscriptionState.Closed;
        }
    }
}
