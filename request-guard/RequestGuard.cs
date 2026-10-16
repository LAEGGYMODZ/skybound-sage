using System;
using System.Collections.Generic;
using System.Linq;
using System.Collections.ObjectModel;
using System.Threading;

namespace SkyboundSageRequestGuard
{
    public enum Origin { UnattributedObservation, SyntheticJournal }
    public enum GuardState { Idle, AwaitingCreation, CandidateOnly, TrackedInFixture, AwaitingRemoval, Uncertain, RemovedInFixture }
    public enum Mutation { Create, Remove, Replace }
    public sealed class Scope
    {
        public readonly object Owner,World,Manager;public readonly long Generation;
        public Scope(object owner,object world,object manager,long generation){if(owner==null||world==null||manager==null||generation<1)throw new ArgumentException();Owner=owner;World=world;Manager=manager;Generation=generation;}
        public bool Same(Scope other){return other!=null&&Object.ReferenceEquals(Owner,other.Owner)&&Object.ReferenceEquals(World,other.World)&&Object.ReferenceEquals(Manager,other.Manager)&&Generation==other.Generation;}
    }
    public sealed class Entry
    {
        public readonly uint Handle;public readonly string Property;public readonly float Value;public readonly object Instance;
        // Fixture sidecar identity, NOT an observed game field or established native capability.
        public readonly long Incarnation;public readonly bool Composite;
        public Entry(uint handle,string property,float value,object instance,long incarnation,bool composite=false)
        {if(handle==0||String.IsNullOrEmpty(property)||property.Length>200||Single.IsNaN(value)||Single.IsInfinity(value)||instance==null||incarnation<1)throw new ArgumentException();Handle=handle;Property=property;Value=value;Instance=instance;Incarnation=incarnation;Composite=composite;}
        public bool Same(Entry other){return other!=null&&Handle==other.Handle&&Property==other.Property&&Value.Equals(other.Value)&&Object.ReferenceEquals(Instance,other.Instance)&&Incarnation==other.Incarnation&&Composite==other.Composite;}
    }
    public sealed class Frame
    {
        public readonly Scope Scope;public readonly long Sequence;public readonly Origin Origin;public readonly bool ReportComplete;
        public readonly ReadOnlyCollection<Entry> Entries;public readonly ReadOnlyCollection<uint> PropertyMapHandles,ListHandles,NotificationHandles;
        public readonly ReadOnlyCollection<KeyValuePair<uint,string>> PropertyAssignments;
        public readonly ReadOnlyCollection<KeyValuePair<string,uint>> ListAssignments;
        public Frame(Scope scope,long sequence,Origin origin,IEnumerable<Entry> entries,IEnumerable<uint> propertyMapHandles=null,IEnumerable<uint> listHandles=null,IEnumerable<uint> notificationHandles=null,bool complete=true,IEnumerable<KeyValuePair<uint,string>> propertyAssignments=null,IEnumerable<KeyValuePair<string,uint>> listAssignments=null)
        {
            if(scope==null||sequence<0||entries==null)throw new ArgumentException();
            Entry[] copied=entries.Take(129).ToArray();if(copied.Length>128||copied.Any(entry=>entry==null))throw new ArgumentException();
            Scope=scope;Sequence=sequence;Origin=origin;ReportComplete=complete;Entries=Array.AsReadOnly(copied);
            PropertyMapHandles=Copy(propertyMapHandles??copied.Select(entry=>entry.Handle));ListHandles=Copy(listHandles??copied.Select(entry=>entry.Handle));NotificationHandles=Copy(notificationHandles??new uint[0]);
            var properties=(propertyAssignments??copied.Select(entry=>new KeyValuePair<uint,string>(entry.Handle,entry.Property))).Take(129).ToArray();
            var lists=(listAssignments??copied.Select(entry=>new KeyValuePair<string,uint>(entry.Property,entry.Handle))).Take(129).ToArray();
            if(properties.Length>128||lists.Length>128)throw new ArgumentException();PropertyAssignments=Array.AsReadOnly(properties);ListAssignments=Array.AsReadOnly(lists);
        }
        private static ReadOnlyCollection<uint> Copy(IEnumerable<uint> source){uint[] values=source.Take(129).ToArray();if(values.Length>128)throw new ArgumentException();return Array.AsReadOnly(values);}
        public bool Consistent
        {get{uint[] handles=Entries.Select(entry=>entry.Handle).OrderBy(id=>id).ToArray();return ReportComplete&&handles.Distinct().Count()==handles.Length&&handles.SequenceEqual(PropertyMapHandles.OrderBy(id=>id))&&handles.SequenceEqual(ListHandles.OrderBy(id=>id))&&NotificationHandles.All(id=>handles.Contains(id))&&handles.SequenceEqual(PropertyAssignments.Select(pair=>pair.Key).OrderBy(id=>id))&&handles.SequenceEqual(ListAssignments.Select(pair=>pair.Value).OrderBy(id=>id))&&PropertyAssignments.All(pair=>Find(pair.Key).Property==pair.Value)&&ListAssignments.All(pair=>Find(pair.Value).Property==pair.Key);}}
        public Entry Find(uint handle){return Entries.FirstOrDefault(entry=>entry.Handle==handle);}
    }
    public sealed class JournalEvent
    {
        public readonly long Sequence;public readonly Guid Operation;public readonly Mutation Mutation;public readonly Entry Before,After;
        public JournalEvent(long sequence,Guid operation,Mutation mutation,Entry before,Entry after){Sequence=sequence;Operation=operation;Mutation=mutation;Before=before;After=after;}
    }
    public sealed class Transition
    {
        public readonly Frame Before,After;public readonly ReadOnlyCollection<JournalEvent> Events;
        public Transition(Frame before,Frame after,IEnumerable<JournalEvent> events)
        {if(before==null||after==null||events==null)throw new ArgumentException();var copy=events.Take(65).ToArray();if(copy.Length>64||copy.Any(e=>e==null))throw new ArgumentException();Before=before;After=after;Events=Array.AsReadOnly(copy);}
        private static bool Equivalent(Frame left,Frame right)
        {return left.Scope.Same(right.Scope)&&left.Sequence==right.Sequence&&left.Origin==right.Origin&&left.Consistent&&right.Consistent&&left.Entries.Count==right.Entries.Count&&left.Entries.All(entry=>entry.Same(right.Find(entry.Handle)))&&left.NotificationHandles.OrderBy(id=>id).SequenceEqual(right.NotificationHandles.OrderBy(id=>id));}
        public bool Fits(Frame expected)
        {
            if(!Equivalent(expected,Before)||!After.Scope.Same(Before.Scope)||After.Origin!=Origin.SyntheticJournal||Before.Origin!=Origin.SyntheticJournal||!After.Consistent)return false;
            if(After.Sequence<Before.Sequence||After.Sequence-Before.Sequence!=Events.Count)return false;
            var replay=Before.Entries.ToDictionary(entry=>entry.Handle);long next=Before.Sequence;
            foreach(var change in Events){
                if(change.Sequence!=++next)return false;
                if(change.Mutation==Mutation.Create){if(change.Before!=null||change.After==null||replay.ContainsKey(change.After.Handle))return false;replay.Add(change.After.Handle,change.After);}
                else{
                    Entry old;if(change.Before==null||!replay.TryGetValue(change.Before.Handle,out old)||!old.Same(change.Before))return false;
                    if(change.Mutation==Mutation.Remove){if(change.After!=null)return false;replay.Remove(old.Handle);}
                    else if(change.Mutation==Mutation.Replace){if(change.After==null||change.After.Handle!=old.Handle)return false;replay[old.Handle]=change.After;}
                    else return false;
                }
            }
            return replay.Count==After.Entries.Count&&After.Entries.All(entry=>replay.ContainsKey(entry.Handle)&&replay[entry.Handle].Same(entry));
        }
    }
    public sealed class CreateProposal
    {
        public readonly Guid Operation;public readonly string Property;public readonly float AbsoluteValue;
        public uint Handle {get{return 0;}}public uint Notification {get{return 0;}}public bool SetDefault {get{return false;}}public bool NativeExecutionAllowed {get{return false;}}
        internal CreateProposal(Guid operation,string property,float value){Operation=operation;Property=property;AbsoluteValue=value;}
    }
    public sealed class RemovalProposal
    {
        public readonly Guid Operation;public readonly Entry Receipt;
        public uint Notification {get{return 0;}}public bool NativeExecutionAllowed {get{return false;}}
        internal RemovalProposal(Guid operation,Entry receipt){Operation=operation;Receipt=receipt;}
    }
    // Pure offline protocol model. No loader, game resolver, native writer, effect host or controller reader.
    public sealed class RequestGuard
    {
        private readonly int thread=Thread.CurrentThread.ManagedThreadId;
        private Frame last;private CreateProposal create;private RemovalProposal remove;
        public GuardState State {get;private set;}public Entry Receipt {get;private set;}public string Reason {get;private set;}
        public bool NativeExecutionAllowed {get{return false;}}
        public Guid PendingCreation {get{return create==null?Guid.Empty:create.Operation;}}
        public RequestGuard(){State=GuardState.Idle;Reason="no-request";}
        private void ThreadCheck(){if(Thread.CurrentThread.ManagedThreadId!=thread)throw new InvalidOperationException("wrong-thread");}
        private bool Unknown(string reason){State=GuardState.Uncertain;Reason=reason;return false;}
        public CreateProposal Begin(Frame before,string property,float absoluteValue)
        {
            ThreadCheck();if(State!=GuardState.Idle)throw new InvalidOperationException("new-create-blocked");
            if(before==null||String.IsNullOrEmpty(property)||Single.IsNaN(absoluteValue)||Single.IsInfinity(absoluteValue)||absoluteValue<=0)throw new ArgumentException();
            if(!before.Consistent){Unknown("inconsistent-before");return null;}
            // A retained request is not proof of a valid native baseline or calculator.
            if(!before.Entries.Any(entry=>entry.Property==property)){Unknown("no-retained-property-request");return null;}
            last=before;create=new CreateProposal(Guid.NewGuid(),property,absoluteValue);State=GuardState.AwaitingCreation;Reason="awaiting-attribution";return create;
        }
        public bool ObserveCreation(Transition transition)
        {
            ThreadCheck();if(create==null||(State!=GuardState.AwaitingCreation&&State!=GuardState.Uncertain))throw new InvalidOperationException("no-pending-create");
            if(transition==null)return Unknown("missing-create-readback");
            if(transition.Before.Origin!=Origin.SyntheticJournal||transition.After.Origin!=Origin.SyntheticJournal){State=GuardState.CandidateOnly;Reason="snapshot-delta-is-not-ownership";return false;}
            if(!transition.Fits(last))return Unknown("incomplete-or-inconsistent-create-journal");
            var own=transition.Events.Where(change=>change.Operation==create.Operation).ToArray();
            if(own.Length!=1||own[0].Mutation!=Mutation.Create||own[0].After==null)return Unknown("no-single-correlated-create");
            Entry entry=own[0].After;Entry current=transition.After.Find(entry.Handle);
            if(transition.Events.Any(change=>change.Sequence>own[0].Sequence&&((change.Before!=null&&change.Before.Handle==entry.Handle)||(change.After!=null&&change.After.Handle==entry.Handle))))return Unknown("candidate-changed-during-create-window");
            if(current==null||!entry.Same(current)||entry.Property!=create.Property||!entry.Value.Equals(create.AbsoluteValue)||entry.Composite)return Unknown("create-candidate-mismatch");
            if(last.Find(entry.Handle)!=null||last.Entries.Any(old=>Object.ReferenceEquals(old.Instance,entry.Instance))||transition.After.Entries.Any(other=>other.Handle!=entry.Handle&&Object.ReferenceEquals(other.Instance,entry.Instance)))return Unknown("reused-or-shared-request-identity");
            if(transition.After.NotificationHandles.Contains(entry.Handle))return Unknown("notification-group-present");
            Receipt=entry;last=transition.After;State=GuardState.TrackedInFixture;Reason="fixture-attribution-only";return true;
        }
        public RemovalProposal PlanRemoval(Transition sinceLast)
        {
            ThreadCheck();if(State!=GuardState.TrackedInFixture)throw new InvalidOperationException("removal-blocked");
            if(sinceLast==null||!sinceLast.Fits(last)){Unknown("continuity-not-established");return null;}
            if(sinceLast.Events.Any(change=>(change.Before!=null&&change.Before.Handle==Receipt.Handle)||(change.After!=null&&change.After.Handle==Receipt.Handle))){Unknown("owned-handle-changed-or-recycled");return null;}
            Entry current=sinceLast.After.Find(Receipt.Handle);
            if(current==null||!Receipt.Same(current)||current.Composite||sinceLast.After.NotificationHandles.Contains(current.Handle)||sinceLast.After.Entries.Any(other=>other.Handle!=Receipt.Handle&&Object.ReferenceEquals(other.Instance,Receipt.Instance))){Unknown("receipt-not-current");return null;}
            if(sinceLast.After.Entries.Count(entry=>entry.Property==Receipt.Property)<2){Unknown("no-retained-property-request");return null;}
            last=sinceLast.After;remove=new RemovalProposal(Guid.NewGuid(),Receipt);State=GuardState.AwaitingRemoval;Reason="fixture-removal-proposed";return remove;
        }
        public bool ObserveRemoval(Transition transition)
        {
            ThreadCheck();if(remove==null||(State!=GuardState.AwaitingRemoval&&State!=GuardState.Uncertain))throw new InvalidOperationException("no-pending-removal");
            if(transition==null||!transition.Fits(last))return Unknown("removal-readback-incomplete");
            var own=transition.Events.Where(change=>change.Operation==remove.Operation).ToArray();
            if(own.Length!=1||own[0].Mutation!=Mutation.Remove||own[0].Before==null||!Receipt.Same(own[0].Before))return Unknown("exact-removal-not-proven-in-fixture");
            if(transition.Events.Any(change=>change.Sequence<own[0].Sequence&&((change.Before!=null&&change.Before.Handle==Receipt.Handle)||(change.After!=null&&change.After.Handle==Receipt.Handle))))return Unknown("receipt-changed-before-removal");
            Entry current=transition.After.Find(Receipt.Handle);
            if(current!=null&&Receipt.Same(current))return Unknown("request-still-present");
            last=transition.After;State=GuardState.RemovedInFixture;Reason="old-request-removal-journaled-in-fixture";return true;
        }
    }
}
