using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Text;

namespace SkyboundSageFocused
{
    public sealed class CatalogLimits
    {
        public int MaxAssemblies=64, MaxScannedTypes=20000, MaxSelectedTypes=72;
        public int MaxPerFamily=8, MaxRelatedTypes=24, MaxRows=1400, MaxCharacters=240000;
        public int MaxMembersPerKind=12, MaxRowsPerType=40, MaxBaseDepth=3, SoftTimeBudgetMs=2000;
        internal CatalogLimits Copy()
        {
            if (MaxAssemblies<1 || MaxAssemblies>128 || MaxScannedTypes<1 || MaxScannedTypes>50000 ||
                MaxSelectedTypes<1 || MaxSelectedTypes>128 || MaxPerFamily<1 || MaxPerFamily>16 ||
                MaxRelatedTypes<0 || MaxRelatedTypes>48 || MaxRows<1 || MaxRows>4000 ||
                MaxCharacters<1 || MaxCharacters>500000 || MaxMembersPerKind<1 || MaxMembersPerKind>32 ||
                MaxRowsPerType<1 || MaxRowsPerType>80 || MaxBaseDepth<0 || MaxBaseDepth>4 ||
                SoftTimeBudgetMs<0 || SoftTimeBudgetMs>5000) throw new ArgumentOutOfRangeException("limits");
            return (CatalogLimits)MemberwiseClone();
        }
    }

    public sealed class CatalogResult
    {
        public ReadOnlyCollection<string> Rows { get; private set; }
        public ReadOnlyCollection<string> Coverage { get; private set; }
        public ReadOnlyCollection<string> LimitReasons { get; private set; }
        public int SelectedTypes { get; private set; }
        public int Characters { get; private set; }
        public int MetadataErrors { get; private set; }
        public long ElapsedMs { get; private set; }
        public bool Limited { get { return LimitReasons.Count!=0; } }
        internal CatalogResult(List<string> rows,List<string> coverage,List<string> reasons,int selected,int characters,int errors,long elapsed)
        { Rows=rows.AsReadOnly(); Coverage=coverage.AsReadOnly(); LimitReasons=reasons.AsReadOnly(); SelectedTypes=selected; Characters=characters; MetadataErrors=errors; ElapsedMs=elapsed; }
        public string ReportText() { return "category\tassembly\tfamily\towner\tname\tsignature\n"+String.Join("\n",Rows)+"\n"; }
        public string SummaryText()
        {
            return "Skybound Sage focused discovery 0.4.0\nScope: descriptors only; no combat-event semantics established.\n"+
                "Selected types: "+SelectedTypes+"\nRows: "+Rows.Count+"\nCharacters: "+Characters+
                "\nLimited: "+Limited+"\nLimit reasons: "+String.Join(", ",LimitReasons)+
                "\nMetadata errors: "+MetadataErrors+"\nElapsed milliseconds (module measurement): "+ElapsedMs+
                "\nCoverage columns: assembly | type slots returned | descriptors inspected | candidates | selected | enumeration/inspection state | examples\n"+
                String.Join("\n",Coverage)+
                "\nCoverage counts describe a name heuristic at initialization, never usable callback coverage. Unknown counts are not zero.\n"+
                "No game constructors, methods, getters, field values, event accessors or controller reads are invoked. No subscriptions, patches or effects are installed by this module.\n";
        }
    }

    // Original reflection-descriptor scanner. No game action or instance value is accessed.
    public sealed class ApiCatalog
    {
        private sealed class Census
        {
            public string Name,State="NAME_ONLY";
            public int Returned=-1,Inspected,Candidates,Selected;
            public readonly List<string> Examples=new List<string>();
        }
        private sealed class Candidate
        {
            public Type Type; public string Assembly,Family; public int Score;
            public List<string> Members=new List<string>(); public string TypeRow;
        }
        private static readonly string[] Families={"events","player","dodge","hit","lifecycle","controller"};
        private const BindingFlags Declared=BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance|BindingFlags.Static|BindingFlags.DeclaredOnly;
        private readonly CatalogLimits limits;
        private readonly Stopwatch clock=new Stopwatch();
        private readonly List<string> rows=new List<string>();
        private readonly HashSet<string> reasons=new HashSet<string>(StringComparer.Ordinal);
        private readonly Dictionary<Assembly,Census> censuses=new Dictionary<Assembly,Census>();
        private readonly HashSet<Type> known=new HashSet<Type>();
        private readonly HashSet<Type> included=new HashSet<Type>();
        private readonly List<Candidate> selected=new List<Candidate>();
        private bool captured;
        private int characters,errors,scanned,related;
        public ApiCatalog() : this(new CatalogLimits()) { }
        public ApiCatalog(CatalogLimits options) { if(options==null) throw new ArgumentNullException("options"); limits=options.Copy(); }
        private void Begin() { if(captured) throw new InvalidOperationException("Use a fresh scanner for each pass."); captured=true; clock.Start(); }
        private bool TimeStop()
        { if(clock.ElapsedMilliseconds<limits.SoftTimeBudgetMs) return false; reasons.Add("SOFT_TIME_BUDGET"); return true; }
        private string Token(string value,int cap)
        {
            if(value==null) value="<unknown>";
            if(value.Length>cap) { value=value.Substring(0,cap-16)+"[text clipped]"; reasons.Add("TOKEN_CLIPPED"); }
            return value.Replace("\\","\\\\").Replace("\t","\\t").Replace("\r","\\r").Replace("\n","\\n");
        }
        private string Line(string category,string assembly,string family,string owner,string name,string signature)
        { return Token(category,300)+"\t"+Token(assembly,300)+"\t"+Token(family,300)+"\t"+Token(owner,700)+"\t"+Token(name,300)+"\t"+Token(signature,1400); }
        private bool Emit(string row)
        {
            if(rows.Count>=limits.MaxRows) { reasons.Add("REPORT_ROW_LIMIT"); return false; }
            if(row.Length+1>limits.MaxCharacters-characters) { reasons.Add("REPORT_CHARACTER_LIMIT"); return false; }
            rows.Add(row); characters+=row.Length+1; return true;
        }
        private static string TypeName(Type type) { return type==null?"<unknown>":type.FullName??type.Name; }
        private static bool GameAssembly(string name)
        { return name.Equals("b1",StringComparison.OrdinalIgnoreCase)||name.StartsWith("b1.",StringComparison.OrdinalIgnoreCase)||name.StartsWith("b1-",StringComparison.OrdinalIgnoreCase); }
        private static bool RelatedAssembly(string name)
        { return GameAssembly(name)||name=="UnrealEngine.Runtime"||name=="CSharpModBase"; }
        private static int AssemblyRank(string name)
        { if(name=="b1.Managed") return 0; if(GameAssembly(name)) return 1; if(name=="CSharpModBase") return 2; return 3; }
        private static HashSet<string> Words(string text)
        {
            var result=new HashSet<string>(StringComparer.Ordinal); var word=new StringBuilder();
            for(int i=0;i<text.Length;i++) {
                char c=text[i]; bool boundary=!Char.IsLetter(c) || (i>0 && Char.IsUpper(c) &&
                    (Char.IsLower(text[i-1]) || (Char.IsUpper(text[i-1]) && i+1<text.Length && Char.IsLower(text[i+1]))));
                if(boundary && word.Length>0) { result.Add(word.ToString().ToLowerInvariant()); word.Length=0; }
                if(Char.IsLetter(c)) word.Append(c);
            }
            if(word.Length>0) result.Add(word.ToString().ToLowerInvariant()); return result;
        }
        private static bool Has(HashSet<string> words,params string[] values) { return values.Any(words.Contains); }
        private static string Family(string name)
        {
            var words=Words(name);
            if(Has(words,"eventcollection") || (words.Contains("event")&&words.Contains("collection"))) return "events";
            if(Has(words,"perfect","dodge","evade","counter","parry")) return "dodge";
            if(Has(words,"attack","attacked","attacking","damage","dmg","hit","hurt","beattacked")) return "hit";
            if(Has(words,"die","died","death","dead","revive","respawn","rest","load","save","reset")) return "lifecycle";
            if(Has(words,"player","character","controlled","controller")) return "player";
            if(Has(words,"gamepad","input") || (words.Contains("game")&&words.Contains("pad"))) return "controller";
            return null;
        }
        private static int Score(string name)
        {
            var words=Words(name);
            if(Family(name)=="events") return 110;
            if(words.Contains("perfect")) return 100;
            if(Has(words,"player","character")) return 95;
            if(Has(words,"dodge","evade","parry")) return 90;
            if(Has(words,"attack","damage","hit","die","death")) return 80;
            if(Has(words,"add","remove","bind","unbind","invoke","invoker","signature")) return 75;
            return Family(name)!=null?60:0;
        }
        private static int CompareNames(string a,string b)
        { int priority=Score(b).CompareTo(Score(a)); return priority!=0?priority:StringComparer.Ordinal.Compare(a,b); }
        private void Error(Census census,Exception error)
        { errors++; reasons.Add("METADATA_ERROR"); if(census!=null) census.State="METADATA_ERROR:"+error.GetType().Name; }
        private Census CensusFor(Assembly assembly)
        {
            Census census;
            if(!censuses.TryGetValue(assembly,out census)) { census=new Census {Name=assembly.GetName().Name}; censuses.Add(assembly,census); }
            return census;
        }
        private Candidate Describe(Type type,string family)
        { var census=CensusFor(type.Assembly); return new Candidate {Type=type,Assembly=census.Name,Family=family,Score=Score(type.Name)}; }

        public CatalogResult Capture(Assembly[] assemblies)
        {
            if(assemblies==null) throw new ArgumentNullException("assemblies"); Begin();
            var roots=new List<Candidate>(); var unique=assemblies.Where(a=>a!=null).Distinct().ToList();
            unique.Sort(delegate(Assembly a,Assembly b) {
                string an=a.GetName().Name,bn=b.GetName().Name;
                int rank=AssemblyRank(an).CompareTo(AssemblyRank(bn)); return rank!=0?rank:StringComparer.Ordinal.Compare(an,bn);
            });
            int inspectedAssemblies=0;
            foreach(Assembly assembly in unique) {
                if(TimeStop()) break;
                if(inspectedAssemblies++>=limits.MaxAssemblies) { reasons.Add("ASSEMBLY_LIMIT"); break; }
                Census census=null;
                try {
                    census=CensusFor(assembly);
                    if(!GameAssembly(census.Name)&&census.Name!="CSharpModBase") continue;
                    if(scanned>=limits.MaxScannedTypes) { census.State="NOT_INSPECTED_TYPE_LIMIT"; reasons.Add("SCANNED_TYPE_LIMIT"); continue; }
                    Type[] types; bool complete=true;
                    try { types=assembly.GetTypes(); }
                    catch(ReflectionTypeLoadException error) { Error(census,error); types=error.Types; complete=false; }
                    census.Returned=types.Length;
                    foreach(Type type in types) {
                        if(TimeStop() || scanned>=limits.MaxScannedTypes) { complete=false; reasons.Add(scanned>=limits.MaxScannedTypes?"SCANNED_TYPE_LIMIT":"SOFT_TIME_BUDGET"); break; }
                        scanned++; census.Inspected++;
                        if(type==null) { complete=false; continue; }
                        if(census.Examples.Count<5) census.Examples.Add(Token(TypeName(type),300));
                        string family=Family(type.Name);
                        // The loader's controller helper is the sole non-game root.
                        if(census.Name=="CSharpModBase" && TypeName(type)!="CSharpModBase.Input.GamePadUtils") continue;
                        if(family!=null && known.Add(type)) { census.Candidates++; roots.Add(Describe(type,family)); }
                    }
                    census.State=complete?"ENUMERATED_AND_INSPECTED":"PARTIAL_ENUMERATION_OR_INSPECTION";
                } catch(Exception error) { Error(census,error); }
            }
            return Finish(roots);
        }
        // Local fixture seam; production entry point always calls Capture with loaded assemblies.
        public CatalogResult InspectTypes(Type[] types)
        {
            if(types==null) throw new ArgumentNullException("types"); Begin(); var roots=new List<Candidate>();
            foreach(Type type in types) {
                if(TimeStop()) break;
                if(scanned>=limits.MaxScannedTypes) { reasons.Add("SCANNED_TYPE_LIMIT"); break; }
                scanned++; if(type==null || !known.Add(type)) continue;
                var census=CensusFor(type.Assembly); census.Inspected++; census.State="LOCAL_FIXTURE_SEAM";
                string family=Family(type.Name); if(family!=null) { census.Candidates++; roots.Add(Describe(type,family)); }
            }
            return Finish(roots);
        }
        private CatalogResult Finish(List<Candidate> roots)
        {
            roots.Sort(delegate(Candidate a,Candidate b) { int rank=b.Score.CompareTo(a.Score); return rank!=0?rank:StringComparer.Ordinal.Compare(TypeName(a.Type),TypeName(b.Type)); });
            var queues=Families.Select(f=>new Queue<Candidate>(roots.Where(c=>c.Family==f))).ToArray();
            // Reserve fair opportunities across families; one event collection cannot consume all root slots.
            for(int round=0;round<limits.MaxPerFamily;round++) foreach(var queue in queues) {
                if(TimeStop()) break;
                if(queue.Count==0) continue;
                if(selected.Count>=limits.MaxSelectedTypes) { reasons.Add("SELECTED_TYPE_LIMIT"); break; }
                Candidate root=queue.Dequeue(); selected.Add(root); included.Add(root.Type);
            }
            if(queues.Any(q=>q.Count>0)) reasons.Add("FAMILY_OR_SELECTED_TYPE_LIMIT");
            // Each root can reveal the real wrapper/signature types through descriptors, even if their names do not match.
            for(int i=0;i<selected.Count && !TimeStop();i++) {
                Candidate candidate=selected[i];
                try { Collect(candidate); } catch(Exception error) { Error(CensusFor(candidate.Type.Assembly),error); }
            }
            // Emit type identities before details, then distribute member rows round by round.
            foreach(Candidate candidate in selected) {
                if(candidate.TypeRow==null) continue;
                if(!Emit(candidate.TypeRow)) break;
                CensusFor(candidate.Type.Assembly).Selected++;
            }
            bool outputStopped=false;
            for(int round=0;round<limits.MaxRowsPerType && !outputStopped;round++) foreach(Candidate candidate in selected) {
                if(round<candidate.Members.Count && !Emit(candidate.Members[round])) { outputStopped=true; break; }
            }
            clock.Stop(); var coverage=new List<string>();
            foreach(var pair in censuses.OrderBy(p=>p.Value.Name,StringComparer.Ordinal)) {
                Census c=pair.Value;
                coverage.Add(Token(c.Name,300)+" | "+(c.Returned<0?"UNKNOWN":c.Returned.ToString())+" | "+c.Inspected+" | "+c.Candidates+" | "+c.Selected+" | "+c.State+" | "+String.Join("; ",c.Examples));
            }
            return new CatalogResult(new List<string>(rows),coverage,reasons.OrderBy(s=>s,StringComparer.Ordinal).ToList(),selected.Count,characters,errors,clock.ElapsedMilliseconds);
        }
        private void Member(Candidate candidate,string kind,string name,string signature)
        {
            if(candidate.Members.Count>=limits.MaxRowsPerType) { reasons.Add("PER_TYPE_ROW_LIMIT"); return; }
            candidate.Members.Add(Line(kind,candidate.Assembly,candidate.Family,TypeName(candidate.Type),name,signature));
        }
        private void Related(Type type)
        {
            if(type==null || TimeStop() || included.Contains(type)) return;
            if(!RelatedAssembly(type.Assembly.GetName().Name)) return;
            if(related>=limits.MaxRelatedTypes || selected.Count>=limits.MaxSelectedTypes) { reasons.Add("RELATED_OR_SELECTED_TYPE_LIMIT"); return; }
            included.Add(type); related++; selected.Add(Describe(type,"binding"));
        }
        private void Collect(Candidate candidate)
        {
            Type type=candidate.Type;
            candidate.TypeRow=Line("TYPE",candidate.Assembly,candidate.Family,TypeName(type),type.Name,"base="+TypeName(type.BaseType));
            FieldInfo[] fields=type.GetFields(Declared); Array.Sort(fields,delegate(FieldInfo a,FieldInfo b){return CompareNames(a.Name,b.Name);});
            PropertyInfo[] properties=type.GetProperties(Declared); Array.Sort(properties,delegate(PropertyInfo a,PropertyInfo b){return CompareNames(a.Name,b.Name);});
            EventInfo[] events=type.GetEvents(Declared); Array.Sort(events,delegate(EventInfo a,EventInfo b){return CompareNames(a.Name,b.Name);});
            MethodInfo[] methods=type.GetMethods(Declared); Array.Sort(methods,delegate(MethodInfo a,MethodInfo b){return CompareNames(a.Name,b.Name);});
            if(fields.Length>limits.MaxMembersPerKind || properties.Length>limits.MaxMembersPerKind || events.Length>limits.MaxMembersPerKind || methods.Count(m=>!m.IsSpecialName)>limits.MaxMembersPerKind) reasons.Add("MEMBER_KIND_LIMIT");
            // Usable signature and binding descriptors come before private native-offset plumbing.
            foreach(MethodInfo method in methods.Where(m=>!m.IsSpecialName).Take(limits.MaxMembersPerKind)) {
                if(TimeStop()) return; Member(candidate,"METHOD",method.Name,MethodSignature(method));
            }
            foreach(PropertyInfo property in properties.Take(limits.MaxMembersPerKind)) {
                if(TimeStop()) return;
                Member(candidate,"PROPERTY",property.Name,TypeName(property.PropertyType)+"; getter="+(property.GetGetMethod(true)!=null)+"; setter="+(property.GetSetMethod(true)!=null)+"; descriptors only"); Related(property.PropertyType);
            }
            foreach(EventInfo item in events.Take(limits.MaxMembersPerKind)) {
                if(TimeStop()) return; Member(candidate,"EVENT",item.Name,TypeName(item.EventHandlerType)); Related(item.EventHandlerType);
            }
            // Base descriptors are read directly; no Add/Bind/Invoke/Remove method is executed.
            Type parent=type.BaseType;
            for(int depth=0;parent!=null && depth<limits.MaxBaseDepth;depth++,parent=parent.BaseType) {
                if(TimeStop()) return;
                string parentAssembly=parent.Assembly.GetName().Name;
                if(parent.IsGenericType) foreach(Type argument in parent.GetGenericArguments()) Related(argument);
                if(!RelatedAssembly(parentAssembly)) continue;
                MethodInfo[] inherited=parent.GetMethods(Declared).Where(m=>!m.IsSpecialName && BindingName(m.Name)).ToArray();
                Array.Sort(inherited,delegate(MethodInfo a,MethodInfo b){return CompareNames(a.Name,b.Name);});
                if(inherited.Length>limits.MaxMembersPerKind) reasons.Add("BINDING_MEMBER_LIMIT");
                foreach(MethodInfo method in inherited.Take(limits.MaxMembersPerKind)) Member(candidate,"INHERITED_BINDING",method.Name,"declaringAssembly="+parentAssembly+"; declaringType="+TypeName(parent)+"; "+MethodSignature(method));
            }
            if(parent!=null && RelatedAssembly(parent.Assembly.GetName().Name)) reasons.Add("BASE_DEPTH_LIMIT");
            foreach(FieldInfo field in fields.Take(limits.MaxMembersPerKind)) {
                if(TimeStop()) return;
                Member(candidate,"FIELD",field.Name,TypeName(field.FieldType)+(field.IsStatic?" static":" instance")+(typeof(Delegate).IsAssignableFrom(field.FieldType)?" managed delegate":"")+"; descriptor only"); Related(field.FieldType);
            }
            // Nested delegate signatures are linked only through descriptor type relationships.
            if(candidate.Family=="binding") {
                Type[] nested=type.GetNestedTypes(BindingFlags.Public|BindingFlags.NonPublic);
                foreach(Type item in nested.Take(limits.MaxMembersPerKind)) if(typeof(Delegate).IsAssignableFrom(item)) Related(item);
                if(nested.Length>limits.MaxMembersPerKind) reasons.Add("NESTED_TYPE_LIMIT");
            }
        }
        private static bool BindingName(string name)
        { var words=Words(name); return Has(words,"add","remove","bind","unbind","clear","invoke","invoker","bound"); }
        private string MethodSignature(MethodInfo method)
        {
            var text=new StringBuilder(TypeName(method.ReturnType)); text.Append(method.IsStatic?" static":" instance");
            text.Append(method.IsPublic?" public (":" nonpublic ("); ParameterInfo[] parameters=method.GetParameters();
            foreach(ParameterInfo parameter in parameters.Take(12)) {
                string mode=parameter.IsOut?"out ":parameter.ParameterType.IsByRef?"ref ":"";
                text.Append(mode); text.Append(TypeName(parameter.ParameterType)); text.Append(" "); text.Append(parameter.Name??"<unnamed>");
                if(parameter.IsOptional) text.Append(" [optional; default value excluded]"); text.Append(";");
            }
            if(parameters.Length>12) { text.Append("[parameters clipped]"); reasons.Add("PARAMETER_LIMIT"); }
            text.Append(")"); return text.ToString();
        }
    }
}
