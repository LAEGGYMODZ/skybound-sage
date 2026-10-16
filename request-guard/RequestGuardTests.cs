using System;
using System.Linq;
using System.Collections.Generic;
using System.Threading;
using SkyboundSageRequestGuard;

internal static class RequestGuardTests
{
    private static int passed,failed;
    private static void Assert(bool condition,string message){if(!condition)throw new Exception(message);}
    private static void Test(string name,Action action){try{action();passed++;Console.WriteLine("PASS "+name);}catch(Exception error){failed++;Console.WriteLine("FAIL "+name+": "+error.Message);}}
    private static void Throws<T>(Action action)where T:Exception{try{action();}catch(T){return;}throw new Exception("Expected "+typeof(T).Name);}
    private sealed class Fixture
    {
        public readonly Scope Scope=new Scope(new object(),new object(),new object(),1);
        public readonly Entry Anchor=new Entry(10,"Movement_MaxWalkSpeed",500,new object(),1);
        public readonly Entry Other=new Entry(11,"OtherProperty",7,new object(),2);
        public readonly Entry Ours=new Entry(12,"Movement_MaxWalkSpeed",550,new object(),3);
        public readonly RequestGuard Guard=new RequestGuard();public Frame Before,Active;public CreateProposal Create;
        public Fixture(bool activate=true){Before=new Frame(Scope,0,Origin.SyntheticJournal,new[]{Anchor,Other});Create=Guard.Begin(Before,"Movement_MaxWalkSpeed",550);Active=Frame(1,Anchor,Other,Ours);if(activate)Assert(Guard.ObserveCreation(new Transition(Before,Active,new[]{Made(1,Create.Operation,Ours)})),"Fixture attribution failed");}
        public Frame Frame(long seq,params Entry[] entries){return new Frame(Scope,seq,Origin.SyntheticJournal,entries);}
        public RemovalProposal Plan(){return Guard.PlanRemoval(new Transition(Active,Active,new JournalEvent[0]));}
    }
    private static JournalEvent Made(long seq,Guid op,Entry entry){return new JournalEvent(seq,op,Mutation.Create,null,entry);}
    private static JournalEvent Gone(long seq,Guid op,Entry entry){return new JournalEvent(seq,op,Mutation.Remove,entry,null);}
    private static JournalEvent Changed(long seq,Entry before,Entry after){return new JournalEvent(seq,Guid.NewGuid(),Mutation.Replace,before,after);}
    private static int Main()
    {
        Test("Create proposal uses zero handle zero notification and non-default mode",delegate{var f=new Fixture(false);Assert(f.Create.Handle==0&&f.Create.Notification==0&&!f.Create.SetDefault&&!f.Create.NativeExecutionAllowed,"Incorrect setter proposal");});
        Test("No native execution path exists even after successful fixture attribution",delegate{var f=new Fixture();Assert(!f.Guard.NativeExecutionAllowed&&!f.Plan().NativeExecutionAllowed,"Fixture promoted to native permission");});
        Test("Correlated complete fixture creation is tracked",delegate{Assert(new Fixture().Guard.State==GuardState.TrackedInFixture,"Not tracked");});
        Test("Snapshot delta without a fixture journal cannot establish ownership",delegate{
            var f=new Fixture(false);var a=new Frame(f.Scope,0,Origin.UnattributedObservation,new[]{f.Anchor,f.Other});var b=new Frame(f.Scope,1,Origin.UnattributedObservation,new[]{f.Anchor,f.Other,f.Ours});
            Assert(!f.Guard.ObserveCreation(new Transition(a,b,new[]{Made(1,f.Create.Operation,f.Ours)}))&&f.Guard.State==GuardState.CandidateOnly,"Unattributed snapshot accepted");
        });
        Test("Identical foreign request cannot be adopted from matching value",delegate{var f=new Fixture(false);Assert(!f.Guard.ObserveCreation(new Transition(f.Before,f.Active,new[]{Made(1,Guid.NewGuid(),f.Ours)})),"Foreign request adopted");});
        Test("An independently journaled external addition can coexist with ours",delegate{
            var f=new Fixture(false);var extra=new Entry(13,"OtherProperty",8,new object(),4);var after=f.Frame(2,f.Anchor,f.Other,extra,f.Ours);
            Assert(f.Guard.ObserveCreation(new Transition(f.Before,after,new[]{Made(1,Guid.NewGuid(),extra),Made(2,f.Create.Operation,f.Ours)})),"Independent external request blocked");
        });
        Test("Missing journal sequence remains uncertain",delegate{var f=new Fixture(false);Assert(!f.Guard.ObserveCreation(new Transition(f.Before,f.Frame(2,f.Anchor,f.Other,f.Ours),new[]{Made(2,f.Create.Operation,f.Ours)})),"Gap accepted");});
        foreach(string part in new[]{"owner","world","manager","generation"}){
            string selected=part;Test("Changed "+part+" scope rejects attribution",delegate{
                var f=new Fixture(false);var scope=new Scope(selected=="owner"?new object():f.Scope.Owner,selected=="world"?new object():f.Scope.World,selected=="manager"?new object():f.Scope.Manager,selected=="generation"?2:1);
                Assert(!f.Guard.ObserveCreation(new Transition(f.Before,new Frame(scope,1,Origin.SyntheticJournal,new[]{f.Anchor,f.Other,f.Ours}),new[]{Made(1,f.Create.Operation,f.Ours)})),"Cross-context attribution");
            });
        }
        Test("Partial insertion with a missing handle map remains uncertain",delegate{
            var f=new Fixture(false);var after=new Frame(f.Scope,1,Origin.SyntheticJournal,new[]{f.Anchor,f.Other,f.Ours},new uint[]{10,11});Assert(!f.Guard.ObserveCreation(new Transition(f.Before,after,new[]{Made(1,f.Create.Operation,f.Ours)})),"Partial map accepted");
        });
        Test("Wrong property mapping cannot be hidden by a matching handle set",delegate{
            var f=new Fixture(false);var after=new Frame(f.Scope,1,Origin.SyntheticJournal,new[]{f.Anchor,f.Other,f.Ours},propertyAssignments:new[]{new KeyValuePair<uint,string>(10,f.Anchor.Property),new KeyValuePair<uint,string>(11,f.Other.Property),new KeyValuePair<uint,string>(12,"WrongProperty")});Assert(!after.Consistent&&!f.Guard.ObserveCreation(new Transition(f.Before,after,new[]{Made(1,f.Create.Operation,f.Ours)})),"Wrong property accepted");
        });
        Test("Wrong property list assignment rejected",delegate{
            var f=new Fixture(false);var after=new Frame(f.Scope,1,Origin.SyntheticJournal,new[]{f.Anchor,f.Other,f.Ours},listAssignments:new[]{new KeyValuePair<string,uint>(f.Anchor.Property,10),new KeyValuePair<string,uint>(f.Other.Property,11),new KeyValuePair<string,uint>("Wrong",12)});Assert(!after.Consistent,"Wrong list accepted");
        });
        Test("Duplicate handles reject a frame",delegate{var f=new Fixture(false);Assert(!f.Frame(1,f.Anchor,f.Other,f.Ours,f.Ours).Consistent,"Duplicates accepted");});
        Test("Incomplete report cannot establish attribution",delegate{
            var f=new Fixture(false);var after=new Frame(f.Scope,1,Origin.SyntheticJournal,new[]{f.Anchor,f.Other,f.Ours},complete:false);Assert(!f.Guard.ObserveCreation(new Transition(f.Before,after,new[]{Made(1,f.Create.Operation,f.Ours)})),"Incomplete accepted");
        });
        Test("Wrong absolute value rejected",delegate{var f=new Fixture(false);var wrong=new Entry(12,f.Ours.Property,551,f.Ours.Instance,3);Assert(!f.Guard.ObserveCreation(new Transition(f.Before,f.Frame(1,f.Anchor,f.Other,wrong),new[]{Made(1,f.Create.Operation,wrong)})),"Wrong value adopted");});
        Test("Wrong property rejected",delegate{var f=new Fixture(false);var wrong=new Entry(12,"OtherProperty",550,f.Ours.Instance,3);Assert(!f.Guard.ObserveCreation(new Transition(f.Before,f.Frame(1,f.Anchor,f.Other,wrong),new[]{Made(1,f.Create.Operation,wrong)})),"Wrong property adopted");});
        Test("Composite creation is outside this protocol",delegate{var f=new Fixture(false);var wrong=new Entry(12,f.Ours.Property,550,f.Ours.Instance,3,true);Assert(!f.Guard.ObserveCreation(new Transition(f.Before,f.Frame(1,f.Anchor,f.Other,wrong),new[]{Made(1,f.Create.Operation,wrong)})),"Composite adopted");});
        Test("Notification-associated creation rejected",delegate{var f=new Fixture(false);var after=new Frame(f.Scope,1,Origin.SyntheticJournal,new[]{f.Anchor,f.Other,f.Ours},notificationHandles:new uint[]{12});Assert(!f.Guard.ObserveCreation(new Transition(f.Before,after,new[]{Made(1,f.Create.Operation,f.Ours)})),"Notification group accepted");});
        Test("Shared pooled reference cannot establish ownership",delegate{var f=new Fixture(false);var wrong=new Entry(12,f.Ours.Property,550,f.Other.Instance,3);Assert(!f.Guard.ObserveCreation(new Transition(f.Before,f.Frame(1,f.Anchor,f.Other,wrong),new[]{Made(1,f.Create.Operation,wrong)})),"Shared object adopted");});
        Test("Candidate removed and recreated during creation window rejected",delegate{
            var f=new Fixture(false);var reused=new Entry(12,f.Ours.Property,550,f.Ours.Instance,4);Assert(!f.Guard.ObserveCreation(new Transition(f.Before,f.Frame(3,f.Anchor,f.Other,reused),new[]{Made(1,f.Create.Operation,f.Ours),Gone(2,Guid.NewGuid(),f.Ours),Made(3,Guid.NewGuid(),reused)})),"Recreated candidate adopted");
        });
        Test("Changed receipt is retained as uncertain instead of removed",delegate{
            var f=new Fixture();var changed=new Entry(12,f.Ours.Property,600,f.Ours.Instance,3);Assert(f.Guard.PlanRemoval(new Transition(f.Active,f.Frame(2,f.Anchor,f.Other,changed),new[]{Changed(2,f.Ours,changed)}))==null&&Object.ReferenceEquals(f.Guard.Receipt,f.Ours),"Changed receipt removed or forgotten");
        });
        Test("Journaled ABA with identical id reference epoch and value is rejected",delegate{
            var f=new Fixture();Assert(f.Guard.PlanRemoval(new Transition(f.Active,f.Frame(3,f.Anchor,f.Other,f.Ours),new[]{Gone(2,Guid.NewGuid(),f.Ours),Made(3,Guid.NewGuid(),f.Ours)}))==null,"Journaled ABA missed");
        });
        Test("Replacement with recycled object and new incarnation rejected",delegate{
            var f=new Fixture();var reused=new Entry(12,f.Ours.Property,550,f.Ours.Instance,4);Assert(f.Guard.PlanRemoval(new Transition(f.Active,f.Frame(2,f.Anchor,f.Other,reused),new[]{Changed(2,f.Ours,reused)}))==null,"Reuse accepted");
        });
        Test("Scope change during cleanup remains uncertain",delegate{
            var f=new Fixture();var next=new Frame(new Scope(f.Scope.Owner,new object(),f.Scope.Manager,1),1,Origin.SyntheticJournal,f.Active.Entries);Assert(f.Guard.PlanRemoval(new Transition(f.Active,next,new JournalEvent[0]))==null&&f.Guard.State==GuardState.Uncertain,"Wrong world cleanup");
        });
        Test("External additions survive removal planning",delegate{
            var f=new Fixture();var extra=new Entry(13,"OtherProperty",8,new object(),4);Assert(f.Guard.PlanRemoval(new Transition(f.Active,f.Frame(2,f.Anchor,f.Other,f.Ours,extra),new[]{Made(2,Guid.NewGuid(),extra)}))!=null,"External addition lost");
        });
        Test("Removing the retained same-property request blocks cleanup proposal",delegate{
            var f=new Fixture();Assert(f.Guard.PlanRemoval(new Transition(f.Active,f.Frame(2,f.Other,f.Ours),new[]{Gone(2,Guid.NewGuid(),f.Anchor)}))==null,"Empty remaining property permitted");
        });
        Test("Removal proposal always excludes grouped reset",delegate{Assert(new Fixture().Plan().Notification==0,"Grouped reset proposed");});
        Test("Exact fixture removal preserves other current requests",delegate{
            var f=new Fixture();var removal=f.Plan();var after=f.Frame(2,f.Anchor,f.Other);Assert(f.Guard.ObserveRemoval(new Transition(f.Active,after,new[]{Gone(2,removal.Operation,f.Ours)}))&&f.Guard.State==GuardState.RemovedInFixture&&after.Find(11).Same(f.Other),"Exact fixture cleanup failed");
        });
        Test("Void-like success with no removal event stays uncertain",delegate{
            var f=new Fixture();f.Plan();Assert(!f.Guard.ObserveRemoval(new Transition(f.Active,f.Active,new JournalEvent[0]))&&f.Guard.State==GuardState.Uncertain&&f.Guard.Receipt!=null,"Unconfirmed cleanup accepted");
        });
        Test("Partial cleanup preserves receipt and blocks a new creation",delegate{
            var f=new Fixture();var removal=f.Plan();var broken=new Frame(f.Scope,2,Origin.SyntheticJournal,new[]{f.Anchor,f.Other},new uint[]{10,11,12});Assert(!f.Guard.ObserveRemoval(new Transition(f.Active,broken,new[]{Gone(2,removal.Operation,f.Ours)}))&&f.Guard.Receipt!=null,"Partial cleanup accepted");Throws<InvalidOperationException>(delegate{f.Guard.Begin(f.Active,f.Ours.Property,550);});
        });
        Test("Later complete fixture removal can reconcile a pending failure",delegate{
            var f=new Fixture();var removal=f.Plan();Assert(!f.Guard.ObserveRemoval(null),"Null readback accepted");Assert(f.Guard.ObserveRemoval(new Transition(f.Active,f.Frame(2,f.Anchor,f.Other),new[]{Gone(2,removal.Operation,f.Ours)})),"Reconciliation failed");
        });
        Test("Foreign disappearance alone cannot prove our removal completed",delegate{
            var f=new Fixture();f.Plan();Assert(!f.Guard.ObserveRemoval(new Transition(f.Active,f.Frame(2,f.Anchor,f.Other),new[]{Gone(2,Guid.NewGuid(),f.Ours)})),"Foreign disappearance accepted");
        });
        Test("ID reused after journaled removal is left alone",delegate{
            var f=new Fixture();var removal=f.Plan();var other=new Entry(12,f.Ours.Property,800,f.Ours.Instance,4);var after=f.Frame(3,f.Anchor,f.Other,other);Assert(f.Guard.ObserveRemoval(new Transition(f.Active,after,new[]{Gone(2,removal.Operation,f.Ours),Made(3,Guid.NewGuid(),other)}))&&after.Find(12).Same(other),"Reused external request removed");
        });
        Test("Intervening replacement before our removal event is rejected",delegate{
            var f=new Fixture();var removal=f.Plan();Assert(!f.Guard.ObserveRemoval(new Transition(f.Active,f.Frame(3,f.Anchor,f.Other),new[]{Changed(2,f.Ours,f.Ours),Gone(3,removal.Operation,f.Ours)})),"Intervening replacement missed");
        });
        Test("Removal proposal is single use",delegate{var f=new Fixture();f.Plan();Throws<InvalidOperationException>(delegate{f.Plan();});});
        Test("Uncertain creation blocks another attempt",delegate{var f=new Fixture(false);f.Guard.ObserveCreation(null);Assert(f.Guard.PendingCreation==f.Create.Operation,"Pending token forgotten");Throws<InvalidOperationException>(delegate{f.Guard.Begin(f.Before,f.Ours.Property,550);});});
        Test("Thread affinity is enforced",delegate{
            var f=new Fixture();Exception caught=null;var thread=new Thread(delegate(){try{f.Plan();}catch(Exception error){caught=error;}});thread.Start();thread.Join();Assert(caught is InvalidOperationException&&f.Guard.State==GuardState.TrackedInFixture,"Wrong-thread mutation");
        });
        Test("Frame collections copy caller arrays",delegate{
            var f=new Fixture(false);var entries=new[]{f.Anchor,f.Other};var frame=new Frame(f.Scope,0,Origin.SyntheticJournal,entries);entries[0]=f.Ours;Assert(frame.Entries[0].Same(f.Anchor),"Caller array leaked");
        });
        Test("Input bounds and invalid floats rejected",delegate{
            var f=new Fixture(false);Throws<ArgumentException>(delegate{new Frame(f.Scope,0,Origin.SyntheticJournal,Enumerable.Repeat(f.Anchor,129));});Throws<ArgumentException>(delegate{new Entry(1,"kind",Single.NaN,new object(),1);});Throws<ArgumentException>(delegate{new RequestGuard().Begin(f.Before,"kind",Single.PositiveInfinity);});
        });
        Test("Absent retained property request blocks creation",delegate{
            var f=new Fixture(false);Assert(new RequestGuard().Begin(f.Before,"UnseenProperty",550)==null,"Unsupported property accepted");
        });
        Console.WriteLine(passed+" passed, "+failed+" failed");return failed==0?0:1;
    }
}
