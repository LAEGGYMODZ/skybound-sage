using System;
using System.Collections.Generic;
using System.Threading;
using SkyboundSageIntegration;

internal static class SubscriptionTests
{
    private static int passed,failed;
    private static void Assert(bool value,string message) { if(!value) throw new Exception(message); }
    private static void Test(string name,Action action)
    { try { action(); passed++; Console.WriteLine("PASS "+name); } catch(Exception error) { failed++; Console.WriteLine("FAIL "+name+": "+error.Message); } }
    private static T Throws<T>(Action action) where T:Exception
    { try { action(); } catch(T error) { return error; } throw new Exception("Expected "+typeof(T).Name); }
    private static int Main()
    {
        Test("Attach and detach use exactly the provided callback instance",delegate {
            Action callback=delegate{}; Delegate added=null,removed=null;
            var subscription=new OwnedSubscription(callback,c=>added=c,c=>removed=c);
            Assert(subscription.Attach() && subscription.State==SubscriptionState.Attached,"Attach state wrong.");
            Assert(subscription.Detach() && subscription.State==SubscriptionState.Detached,"Detach state wrong.");
            Assert(Object.ReferenceEquals(callback,added)&&Object.ReferenceEquals(callback,removed),"Callback identity changed.");
        });
        Test("Repeated attach does not register duplicate callbacks",delegate {
            int adds=0; var subscription=new OwnedSubscription(new Action(delegate{}),c=>adds++,c=>{});
            Assert(subscription.Attach()&&!subscription.Attach()&&adds==1,"Duplicate binding.");
        });
        Test("Removing ours leaves unrelated synthetic callbacks present",delegate {
            Action ours=delegate{},otherA=delegate{},otherB=delegate{};
            var host=new List<Delegate>{otherA,otherB};
            var subscription=new OwnedSubscription(ours,c=>host.Add(c),c=>host.Remove(c));
            subscription.Attach(); subscription.Detach();
            Assert(host.Count==2&&host.Contains(otherA)&&host.Contains(otherB)&&!host.Contains(ours),"Unrelated callbacks removed.");
        });
        Test("Detached cleanup is a no-op",delegate {
            int removes=0; var subscription=new OwnedSubscription(new Action(delegate{}),c=>{},c=>removes++);
            Assert(!subscription.Detach()&&removes==0,"Unattached callback was removed.");
        });
        Test("Dispose is idempotent and closes future attachment",delegate {
            int removes=0; var subscription=new OwnedSubscription(new Action(delegate{}),c=>{},c=>removes++);
            subscription.Attach(); subscription.Dispose(); subscription.Dispose();
            Assert(removes==1&&subscription.State==SubscriptionState.Closed&&!subscription.Detach(),"Repeated cleanup or wrong closed state.");
            Throws<ObjectDisposedException>(delegate {subscription.Attach();});
        });
        Test("A partially successful add that throws attempts exact compensating removal",delegate {
            Action ours=delegate{},other=delegate{}; var host=new List<Delegate>{other};
            var subscription=new OwnedSubscription(ours,c=>{host.Add(c);throw new InvalidOperationException("fixture add failure");},c=>host.Remove(c));
            Throws<InvalidOperationException>(delegate {subscription.Attach();});
            Assert(subscription.State==SubscriptionState.Detached&&host.Count==1&&host.Contains(other),"Partial add was not compensated.");
        });
        Test("Failed add and cleanup retain unknown state and both failures",delegate {
            var subscription=new OwnedSubscription(new Action(delegate{}),c=>{throw new InvalidOperationException();},c=>{throw new ApplicationException();});
            var error=Throws<AggregateException>(delegate {subscription.Attach();});
            Assert(error.InnerExceptions.Count==2&&subscription.State==SubscriptionState.Unknown,"Failure evidence or unknown state lost.");
            Throws<InvalidOperationException>(delegate {subscription.Attach();});
        });
        Test("Failed removal is retryable without losing the original callback",delegate {
            Action callback=delegate{}; bool failing=true; Delegate removed=null;
            var subscription=new OwnedSubscription(callback,c=>{},c=>{if(failing)throw new ApplicationException();removed=c;});
            subscription.Attach(); Throws<ApplicationException>(delegate {subscription.Detach();});
            Assert(subscription.State==SubscriptionState.Unknown,"Failed removal claimed clean state.");
            failing=false; Assert(subscription.Detach()&&Object.ReferenceEquals(callback,removed)&&subscription.State==SubscriptionState.Detached,"Cleanup retry lost callback identity.");
        });
        Test("Failed Dispose stays unknown until successful cleanup closes it",delegate {
            bool failing=true; var subscription=new OwnedSubscription(new Action(delegate{}),c=>{},c=>{if(failing)throw new ApplicationException();});
            subscription.Attach(); Throws<ApplicationException>(delegate {subscription.Dispose();});
            Assert(subscription.State==SubscriptionState.Unknown,"Failed Dispose falsely closed the subscription.");
            failing=false; subscription.Dispose(); Assert(subscription.State==SubscriptionState.Closed,"Retry did not close after successful cleanup.");
        });
        Test("Cross-thread binding and cleanup cannot mutate the host",delegate {
            int mutations=0,refusals=0; var subscription=new OwnedSubscription(new Action(delegate{}),c=>mutations++,c=>mutations++);
            var thread=new Thread(delegate(){
                try {subscription.Attach();}catch(InvalidOperationException){refusals++;}
                try {subscription.Detach();}catch(InvalidOperationException){refusals++;}
                try {subscription.Dispose();}catch(InvalidOperationException){refusals++;}
            }); thread.Start(); thread.Join();
            Assert(refusals==3&&mutations==0&&subscription.State==SubscriptionState.Detached,"Wrong-thread call mutated state.");
        });
        Test("Reentrant attachment and removal are refused during registration",delegate {
            OwnedSubscription subscription=null; int refusals=0;
            subscription=new OwnedSubscription(new Action(delegate{}),c=>{
                try {subscription.Attach();}catch(InvalidOperationException){refusals++;}
                try {subscription.Detach();}catch(InvalidOperationException){refusals++;}
            },c=>{});
            subscription.Attach(); Assert(refusals==2&&subscription.State==SubscriptionState.Attached,"Reentry corrupted registration.");
        });
        Test("The helper never invokes the owned event handler",delegate {
            int callbacks=0; Action handler=delegate{callbacks++;};
            using(var subscription=new OwnedSubscription(handler,c=>{},c=>{})) {subscription.Attach();subscription.Detach();}
            Assert(callbacks==0,"Cleanup helper fabricated an event.");
        });
        Test("Null arguments and multicast callback bundles are refused",delegate {
            Action first=delegate{},second=delegate{};
            Throws<ArgumentNullException>(delegate {new OwnedSubscription(null,c=>{},c=>{});});
            Throws<ArgumentNullException>(delegate {new OwnedSubscription(first,null,c=>{});});
            Throws<ArgumentNullException>(delegate {new OwnedSubscription(first,c=>{},null);});
            Throws<ArgumentException>(delegate {new OwnedSubscription(first+second,c=>{},c=>{});});
        });
        Console.WriteLine("RESULT "+passed+" passed, "+failed+" failed"); return failed==0?0:1;
    }
}
