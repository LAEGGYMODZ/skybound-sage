using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using SkyboundSageFocused;

public static class SideEffects { public static int Calls; }
internal sealed class EventCollectionFixture { public Action OnPlayerHit=null; }
internal sealed class PlayerCharacterFixture { }
internal sealed class PerfectDodgeFixture { }
internal sealed class UnitDieFixture { }
internal sealed class GamePadFixture { }
internal sealed class WhiteListFixture { }
internal sealed class DownloadFixture { }
internal sealed class UnrelatedFixture { }
internal sealed class PlayerHitFixture
{
    public const string FixtureSecret="fixture-value-must-not-be-reported";
    static PlayerHitFixture() { SideEffects.Calls++; }
    public PlayerHitFixture() { SideEffects.Calls++; }
    public Action OnPerfectDodge=null;
    public string PlayerState { get { SideEffects.Calls++; return FixtureSecret; } }
    public bool PlayerHit(ref int attacker,out int target,int sequence=7) { SideEffects.Calls++; target=1; return attacker>0; }
    public event Action HitCompleted { add { SideEffects.Calls++; } remove { SideEffects.Calls++; } }
}
internal sealed class BrokenMetadataAssembly : Assembly
{
    public override AssemblyName GetName() { return new AssemblyName("b1.MetadataErrorFixture"); }
    public override Type[] GetTypes() { throw new ReflectionTypeLoadException(new Type[]{typeof(PlayerHitFixture),null},new Exception[]{new InvalidOperationException("private fixture error must not appear")}); }
}
internal static class FocusedTests
{
    private static int passed,failed,fixtureSerial;
    private static void Assert(bool value,string message) { if(!value) throw new Exception(message); }
    private static void Test(string name,Action action)
    { try { action(); passed++; Console.WriteLine("PASS "+name); } catch(Exception error) { failed++; Console.WriteLine("FAIL "+name+": "+error.Message); } }
    private static CatalogLimits Limits() { return new CatalogLimits {SoftTimeBudgetMs=5000}; }
    private static Type[] Families() { return new[]{typeof(EventCollectionFixture),typeof(PlayerCharacterFixture),typeof(PerfectDodgeFixture),typeof(PlayerHitFixture),typeof(UnitDieFixture),typeof(GamePadFixture)}; }
    private static AssemblyBuilder FixtureAssembly(string name)
    { return AppDomain.CurrentDomain.DefineDynamicAssembly(new AssemblyName(name+"."+(++fixtureSerial)),AssemblyBuilderAccess.Run); }
    private static Type EmptyType(ModuleBuilder module,string name)
    { return module.DefineType(name,TypeAttributes.Public).CreateType(); }
    private static bool Contains(CatalogResult result,string text) { return result.Rows.Any(row=>row.Contains(text)); }
    private static void Throws<T>(Action action) where T:Exception
    { try { action(); } catch(T) { return; } throw new Exception("Expected "+typeof(T).Name); }
    private static Type[] LinkedFixtures()
    {
        var assembly=FixtureAssembly("b1.Managed.LinkedFixture"); var module=assembly.DefineDynamicModule("fixture");
        var baseBuilder=module.DefineType("NativeMulticastBase`1",TypeAttributes.Public);
        Type parameter=baseBuilder.DefineGenericParameters("T")[0];
        foreach(string name in new[]{"Add","Remove"}) {
            var method=baseBuilder.DefineMethod(name,MethodAttributes.Public,typeof(void),new[]{parameter});
            method.DefineParameter(1,ParameterAttributes.None,"handler"); method.GetILGenerator().Emit(OpCodes.Ret);
        }
        Type bindingBase=baseBuilder.CreateType();
        var callbackBuilder=module.DefineType("OpaqueCallbackShape",TypeAttributes.Public);
        var invoke=callbackBuilder.DefineMethod("Invoke",MethodAttributes.Public,typeof(void),new[]{typeof(int),typeof(int)});
        invoke.DefineParameter(1,ParameterAttributes.None,"attacker"); invoke.DefineParameter(2,ParameterAttributes.None,"target"); invoke.GetILGenerator().Emit(OpCodes.Ret);
        Type callback=callbackBuilder.CreateType();
        var wrapperBuilder=module.DefineType("OpaqueWrapper",TypeAttributes.Public,bindingBase.MakeGenericType(callback));
        Type wrapper=wrapperBuilder.CreateType();
        var eventBuilder=module.DefineType("BattleEventCollection",TypeAttributes.Public);
        var prop=eventBuilder.DefineProperty("OnPlayerHit",PropertyAttributes.None,wrapper,Type.EmptyTypes);
        var getter=eventBuilder.DefineMethod("get_OnPlayerHit",MethodAttributes.Public|MethodAttributes.SpecialName|MethodAttributes.HideBySig,wrapper,Type.EmptyTypes);
        var il=getter.GetILGenerator(); il.Emit(OpCodes.Ldsfld,typeof(SideEffects).GetField("Calls")); il.Emit(OpCodes.Ldc_I4_1); il.Emit(OpCodes.Add); il.Emit(OpCodes.Stsfld,typeof(SideEffects).GetField("Calls")); il.Emit(OpCodes.Ldnull); il.Emit(OpCodes.Ret);
        prop.SetGetMethod(getter); Type collection=eventBuilder.CreateType();
        return new[]{collection,wrapper,callback};
    }
    private static int Main()
    {
        Test("Descriptors do not run constructors, getters, methods, or event accessors",delegate {
            SideEffects.Calls=0; var result=new ApiCatalog(Limits()).InspectTypes(Families());
            Assert(result.SelectedTypes==6,"All six families were not selected."); Assert(SideEffects.Calls==0,"A fixture action was invoked.");
        });
        Test("Field and property values and optional defaults stay excluded",delegate {
            string report=new ApiCatalog(Limits()).InspectTypes(new[]{typeof(PlayerHitFixture)}).ReportText();
            Assert(!report.Contains(PlayerHitFixture.FixtureSecret),"Fixture value leaked.");
            Assert(report.Contains("default value excluded") && !report.Contains("sequence=7"),"Optional default was evaluated or signature omitted.");
        });
        Test("Parameter names and ref/out modes are retained as metadata",delegate {
            var result=new ApiCatalog(Limits()).InspectTypes(new[]{typeof(PlayerHitFixture)});
            Assert(Contains(result,"ref System.Int32& attacker;out System.Int32& target;System.Int32 sequence"),"Ownership-shaped parameter descriptors missing.");
        });
        Test("Unrelated whitelist and download names do not pretend to be hit/load types",delegate {
            var result=new ApiCatalog(Limits()).InspectTypes(new[]{typeof(WhiteListFixture),typeof(DownloadFixture),typeof(UnrelatedFixture)});
            Assert(result.SelectedTypes==0,"Substring false positive selected.");
        });
        Test("UnitDie is recognized as a lifecycle candidate",delegate {
            Assert(Contains(new ApiCatalog(Limits()).InspectTypes(new[]{typeof(UnitDieFixture)}),"\tlifecycle\tUnitDieFixture\t"),"Actual naming pattern was missed.");
        });
        Test("Root identities survive a tiny row budget across all combat families",delegate {
            var limits=Limits(); limits.MaxRows=6;
            var result=new ApiCatalog(limits).InspectTypes(Families());
            Assert(result.Rows.Count==6 && result.Rows.All(row=>row.StartsWith("TYPE\t")),"Members crowded out root identities.");
            foreach(Type type in Families()) Assert(Contains(result,"\t"+type.Name+"\t"),"A family was starved.");
            Assert(result.LimitReasons.Contains("REPORT_ROW_LIMIT"),"Row truncation was not explicit.");
        });
        Test("An event-family flood cannot starve player dodge hit lifecycle and controller roots",delegate {
            var assembly=FixtureAssembly("b1.Managed.FamilyFlood"); var module=assembly.DefineDynamicModule("fixture"); var types=new List<Type>();
            for(int i=0;i<100;i++) types.Add(EmptyType(module,"EventCollection"+i)); types.AddRange(Families().Skip(1));
            var limits=Limits(); limits.MaxPerFamily=1; limits.MaxRelatedTypes=0;
            var result=new ApiCatalog(limits).InspectTypes(types.ToArray());
            Assert(result.SelectedTypes==6 && result.LimitReasons.Contains("FAMILY_OR_SELECTED_TYPE_LIMIT"),"Family quotas failed.");
            Assert(Contains(result,"PerfectDodgeFixture") && Contains(result,"PlayerHitFixture"),"Combat roots crowded out.");
        });
        Test("Reference-linked opaque wrapper and callback shapes are inspected without getters",delegate {
            SideEffects.Calls=0; Type[] types=LinkedFixtures();
            var result=new ApiCatalog(Limits()).Capture(new[]{types[0].Assembly});
            Assert(Contains(result,"\tbinding\tOpaqueWrapper\t") && Contains(result,"\tbinding\tOpaqueCallbackShape\t"),"Opaque reference targets were not linked.");
            Assert(Contains(result,"System.Int32 attacker;System.Int32 target;"),"Linked callback signature missing.");
            Assert(SideEffects.Calls==0,"Reference following invoked a getter.");
        });
        Test("Inherited Add and Remove descriptors carry declaring type and assembly",delegate {
            Type[] types=LinkedFixtures(); var result=new ApiCatalog(Limits()).Capture(new[]{types[0].Assembly});
            Assert(result.Rows.Any(row=>row.StartsWith("INHERITED_BINDING\t")&&row.Contains("\tAdd\t")&&row.Contains("declaringAssembly=b1.Managed.LinkedFixture")),"Add descriptor missing provenance.");
            Assert(result.Rows.Any(row=>row.StartsWith("INHERITED_BINDING\t")&&row.Contains("\tRemove\t")&&row.Contains("declaringType=NativeMulticastBase")),"Remove descriptor missing provenance.");
        });
        Test("Disabling related expansion marks missing relationship coverage",delegate {
            Type[] types=LinkedFixtures(); var limits=Limits(); limits.MaxRelatedTypes=0;
            var result=new ApiCatalog(limits).Capture(new[]{types[0].Assembly});
            Assert(!Contains(result,"\tbinding\tOpaqueWrapper\t") && result.LimitReasons.Contains("RELATED_OR_SELECTED_TYPE_LIMIT"),"Missing relationship coverage was hidden.");
        });
        Test("A previously unselected root can still be reached through a reference",delegate {
            var assembly=FixtureAssembly("b1.Managed.ExcludedRootLink"); var module=assembly.DefineDynamicModule("fixture");
            Type callback=EmptyType(module,"ZUnitDieCallback");
            var root=module.DefineType("EventCollectionLinked",TypeAttributes.Public); root.DefineField("PlayerDeath",callback,FieldAttributes.Public);
            Type collection=root.CreateType(); Type preferred=EmptyType(module,"AUnitDie");
            var limits=Limits(); limits.MaxPerFamily=1;
            var result=new ApiCatalog(limits).Capture(new[]{collection.Assembly});
            Assert(Contains(result,"\tbinding\tZUnitDieCallback\t"),"Known but unselected root prevented relationship expansion.");
            Assert(Contains(result,preferred.Name),"Preferred lifecycle fixture missing.");
        });
        Test("Zero returned type slots and unexpanded assembly coverage remain distinct",delegate {
            var empty=FixtureAssembly("b1.Managed.EmptyFixture");
            var result=new ApiCatalog(Limits()).Capture(new Assembly[]{empty,typeof(string).Assembly});
            Assert(result.Coverage.Any(row=>row.Contains("EmptyFixture")&&row.Contains(" | 0 | 0 | 0 | 0 | ENUMERATED_AND_INSPECTED")),"Zero count not measured explicitly.");
            Assert(result.Coverage.Any(row=>row.StartsWith("mscorlib | UNKNOWN |")&&row.Contains("NAME_ONLY")),"Excluded coverage was turned into zero.");
        });
        Test("Every type and member row names its actual source assembly",delegate {
            Type[] types=LinkedFixtures(); var result=new ApiCatalog(Limits()).Capture(new[]{types[0].Assembly});
            Assert(result.Rows.Count>0 && result.Rows.All(row=>row.Split('\t')[1]==types[0].Assembly.GetName().Name),"Assembly provenance missing or guessed.");
        });
        Test("Non-game assemblies are named without expanding their members",delegate {
            var result=new ApiCatalog(Limits()).Capture(new[]{typeof(FocusedTests).Assembly});
            Assert(result.Rows.Count==0 && result.Coverage.Count==1 && result.Coverage[0].Contains("UNKNOWN"),"Unscoped fixture assembly expanded.");
        });
        Test("Game assemblies get inspection priority over general assembly names",delegate {
            var assembly=FixtureAssembly("b1.Managed.PriorityFixture"); var module=assembly.DefineDynamicModule("fixture"); Type game=EmptyType(module,"PlayerHitTarget");
            var limits=Limits(); limits.MaxAssemblies=1;
            var result=new ApiCatalog(limits).Capture(new[]{typeof(string).Assembly,game.Assembly});
            Assert(Contains(result,"PlayerHitTarget") && result.Coverage.Count==1 && result.LimitReasons.Contains("ASSEMBLY_LIMIT"),"General assembly consumed the only opportunity.");
        });
        Test("Scanned-type clipping records partial per-assembly inspection",delegate {
            var assembly=FixtureAssembly("b1.Managed.TypeLimitFixture"); var module=assembly.DefineDynamicModule("fixture");
            for(int i=0;i<10;i++) EmptyType(module,"PlayerHit"+i);
            var limits=Limits(); limits.MaxScannedTypes=2;
            var result=new ApiCatalog(limits).Capture(new Assembly[]{assembly});
            Assert(result.Coverage.Any(row=>row.Contains(" | 10 | 2 | 2 |")&&row.Contains("PARTIAL_ENUMERATION_OR_INSPECTION")),"Type inspection completeness was fabricated.");
            Assert(result.LimitReasons.Contains("SCANNED_TYPE_LIMIT"),"Missing scanned-type reason.");
        });
        Test("Partial type-load errors preserve only error type and partial coverage",delegate {
            var result=new ApiCatalog(Limits()).Capture(new Assembly[]{new BrokenMetadataAssembly()});
            Assert(result.MetadataErrors==1 && result.LimitReasons.Contains("METADATA_ERROR"),"Load error hidden.");
            Assert(result.Coverage.Any(row=>row.Contains("PARTIAL_ENUMERATION_OR_INSPECTION")),"Partial type load marked complete.");
            Assert(!result.SummaryText().Contains("private fixture error"),"Arbitrary error message leaked.");
        });
        Test("Character caps and field-name escaping keep a valid bounded TSV",delegate {
            var assembly=FixtureAssembly("b1.Managed.EscapingFixture"); var module=assembly.DefineDynamicModule("fixture");
            var type=module.DefineType("EventCollectionEscape",TypeAttributes.Public); type.DefineField("PerfectDodge\tName\nBreak",typeof(int),FieldAttributes.Public);
            var result=new ApiCatalog(Limits()).Capture(new[]{type.CreateType().Assembly});
            Assert(result.Rows.All(row=>row.Split('\t').Length==6 && !row.Contains("\n")),"TSV escaped field structure broke.");
            Assert(Contains(result,"PerfectDodge\\tName\\nBreak"),"Escaped metadata field missing.");
            var limits=Limits(); limits.MaxCharacters=20;
            var tiny=new ApiCatalog(limits).InspectTypes(Families());
            Assert(tiny.Characters<=20 && tiny.LimitReasons.Contains("REPORT_CHARACTER_LIMIT"),"Character cap failed.");
        });
        Test("Member and per-type caps report incompleteness explicitly",delegate {
            var limits=Limits(); limits.MaxMembersPerKind=1; limits.MaxRowsPerType=1;
            var result=new ApiCatalog(limits).InspectTypes(new[]{typeof(PlayerHitFixture)});
            Assert(result.Rows.Count<=2 && result.LimitReasons.Contains("MEMBER_KIND_LIMIT") && result.LimitReasons.Contains("PER_TYPE_ROW_LIMIT"),"Member output not bounded or clipping hidden.");
        });
        Test("Zero time budget yields an explicitly limited empty result",delegate {
            var limits=Limits(); limits.SoftTimeBudgetMs=0;
            var result=new ApiCatalog(limits).InspectTypes(Families());
            Assert(result.Rows.Count==0 && result.LimitReasons.Contains("SOFT_TIME_BUDGET"),"Zero-budget result wrong.");
        });
        Test("Duplicate types are selected once and limits cannot be enlarged by callers",delegate {
            var limits=Limits(); limits.MaxSelectedTypes=1; var scanner=new ApiCatalog(limits); limits.MaxSelectedTypes=100;
            var result=scanner.InspectTypes(new[]{typeof(PlayerHitFixture),typeof(PlayerHitFixture),typeof(UnitDieFixture)});
            Assert(result.SelectedTypes==1 && result.Rows.Count(row=>row.StartsWith("TYPE\t"))==1,"Duplicate or mutable limits escaped.");
            Throws<ArgumentOutOfRangeException>(delegate {new ApiCatalog(new CatalogLimits {MaxRows=Int32.MaxValue});});
        });
        Test("Snapshots are immutable and a scanner cannot perform a second pass",delegate {
            var scanner=new ApiCatalog(Limits()); var result=scanner.InspectTypes(Families());
            Throws<NotSupportedException>(delegate {((IList<string>)result.Rows).Add("fake");});
            Throws<NotSupportedException>(delegate {((IList<string>)result.Coverage).Add("fake");});
            Throws<InvalidOperationException>(delegate {scanner.InspectTypes(Families());});
        });
        Console.WriteLine("RESULT "+passed+" passed, "+failed+" failed"); return failed==0?0:1;
    }
}
