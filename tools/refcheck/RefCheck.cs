// Usage: mono RefCheck.exe <mod.dll> <dir>...   — resolves every type/member ref and every
// [HarmonyPatch(typeof(T), "name")] target of the mod against the assemblies in <dir>s.
using System;
using System.Collections.Generic;
using System.Linq;
using Mono.Cecil;

class RefCheck
{
    static readonly HashSet<string> Skip = new HashSet<string> { "mscorlib", "System", "System.Core", "netstandard", "0Harmony", "UnityModManager", "PrismLib", "PrismLib.UI" };

    static int Main(string[] a)
    {
        var res = new DefaultAssemblyResolver();
        foreach (var d in a.Skip(1)) res.AddSearchDirectory(d);
        var mod = ModuleDefinition.ReadModule(a[0], new ReaderParameters { AssemblyResolver = res });
        int bad = 0;
        var seen = new HashSet<string>();
        Action<string> report = s => { if (seen.Add(s)) { Console.WriteLine(s); bad++; } };

        foreach (var t in mod.GetTypeReferences())
        {
            if (Skip.Contains(t.Scope.Name.Replace(".dll", ""))) continue;
            try { if (t.Resolve() == null) report("TYPE   " + t.FullName + "  [" + t.Scope.Name + "]"); }
            catch (Exception e) { report("TYPE   " + t.FullName + "  (" + e.GetType().Name + ")"); }
        }
        foreach (var m in mod.GetMemberReferences())
        {
            var scope = m.DeclaringType.GetElementType().Scope.Name.Replace(".dll", "");
            if (Skip.Contains(scope) || m.DeclaringType.Module == mod && m.DeclaringType.Scope == mod) continue;
            try
            {
                object r = m is MethodReference ? (object)((MethodReference)m).Resolve() : ((FieldReference)m).Resolve();
                if (r == null) report("MEMBER " + m.FullName + "  [" + scope + "]");
            }
            catch (Exception e) { report("MEMBER " + m.FullName + "  (" + e.GetType().Name + ")"); }
        }

        foreach (var t in AllTypes(mod.Types))
        {
            var cls = PatchArgs(t.CustomAttributes);
            if (cls == null) continue;
            var methodAttrs = t.Methods.SelectMany(x => x.CustomAttributes).Where(x => x.AttributeType.Name == "HarmonyPatch");
            var target = cls;
            foreach (var ma in methodAttrs) target = Merge(target, PatchArgs(new[] { ma }));
            if (target.Item1 == null || target.Item2 == null) continue;
            TypeDefinition td;
            try { td = target.Item1.Resolve(); } catch { td = null; }
            if (td == null) { report("PATCH  " + t.FullName + " -> missing type " + target.Item1.FullName); continue; }
            var name = target.Item2;
            List<MethodDefinition> cands;
            if (target.Item3 != null)
            {
                var want = target.Item3.Select(x => x.FullName).ToArray();
                cands = Hier(td).SelectMany(h => h.Methods).Where(x => x.Name == name && x.Parameters.Select(p => p.ParameterType.FullName).SequenceEqual(want)).ToList();
            }
            else
                cands = Hier(td).SelectMany(h => h.Methods).Where(x => x.Name == name).ToList();
            var props = Hier(td).SelectMany(h => h.Properties).Where(p => p.Name == name).ToList();
            foreach (var p in props) { if (p.GetMethod != null) cands.Add(p.GetMethod); if (p.SetMethod != null) cands.Add(p.SetMethod); }
            if (cands.Count == 0) { report("PATCH  " + t.FullName + " -> " + td.FullName + "::" + name + (target.Item3 != null ? "(" + string.Join(",", target.Item3.Select(x => x.Name)) + ")" : "")); continue; }
            // Harmony binds patch params by NAME: plain names = original params, ___x = fields.
            foreach (var pm in t.Methods.Where(x => x.Name == "Prefix" || x.Name == "Postfix" || x.Name == "Finalizer"))
                foreach (var p in pm.Parameters)
                {
                    var n = p.Name;
                    if (n.StartsWith("___"))
                    {
                        var f = n.Substring(3);
                        if (!Hier(td).Any(h => h.Fields.Any(x => x.Name == f))) report("PARAM  " + t.FullName + "." + pm.Name + " " + n + " -> no field " + td.FullName + "." + f);
                    }
                    else if (!n.StartsWith("__") && !cands.Any(c => c.Parameters.Any(x => x.Name == n)))
                        report("PARAM  " + t.FullName + "." + pm.Name + " " + n + " -> not a param of " + td.FullName + "::" + name);
                }
        }

        // String-named reflection: ldtoken T ... ldstr "name" ... call AccessTools.X / Type.GetX.
        var lookup = new HashSet<string> { "Method", "DeclaredMethod", "Field", "DeclaredField", "Property", "DeclaredProperty",
            "PropertyGetter", "PropertySetter", "DeclaredPropertyGetter", "DeclaredPropertySetter", "FieldRefAccess", "StaticFieldRefAccess",
            "GetMethod", "GetField", "GetProperty", "Inner" };
        foreach (var t in AllTypes(mod.Types))
            foreach (var m in t.Methods.Where(x => x.HasBody))
            {
                var ins = m.Body.Instructions;
                for (int i = 0; i < ins.Count; i++)
                {
                    var mr = ins[i].Operand as MethodReference;
                    if (mr == null || (ins[i].OpCode.Code != Mono.Cecil.Cil.Code.Call && ins[i].OpCode.Code != Mono.Cecil.Cil.Code.Callvirt)) continue;
                    var dt = mr.DeclaringType.Name;
                    if (mr.Name == "TypeByName" && dt == "AccessTools")
                    {
                        var s = Back<string>(ins, i, Mono.Cecil.Cil.Code.Ldstr, 2);
                        if (s != null && FindType(mod, res, s) == null) report("REFL   " + t.Name + "." + m.Name + " TypeByName(\"" + s + "\") missing");
                        continue;
                    }
                    if (!lookup.Contains(mr.Name) || !(dt == "AccessTools" || dt == "Type" || dt == "Traverse")) continue;
                    var str = Back<string>(ins, i, Mono.Cecil.Cil.Code.Ldstr, 4);
                    if (str == null) continue;
                    TypeReference tr = null;
                    if (mr is GenericInstanceMethod && mr.Name.Contains("FieldRefAccess")) tr = ((GenericInstanceMethod)mr).GenericArguments[0];
                    else tr = Back<TypeReference>(ins, i, Mono.Cecil.Cil.Code.Ldtoken, 8);
                    if (tr == null) continue;
                    TypeDefinition td2;
                    try { td2 = tr.Resolve(); } catch { td2 = null; }
                    if (td2 == null || td2.Module == mod) continue;
                    bool has = Hier(td2).Any(h => h.Methods.Any(x => x.Name == str) || h.Fields.Any(x => x.Name == str) || h.Properties.Any(x => x.Name == str) || h.NestedTypes.Any(x => x.Name == str));
                    if (!has) report("REFL   " + t.Name + "." + m.Name + " " + mr.Name + "(" + td2.FullName + ", \"" + str + "\") missing");
                }
            }
        Console.WriteLine(bad == 0 ? "OK: all refs resolve" : bad + " unresolved");
        return bad == 0 ? 0 : 1;
    }

    static T Back<T>(IList<Mono.Cecil.Cil.Instruction> ins, int i, Mono.Cecil.Cil.Code code, int window) where T : class
    {
        for (int j = i - 1; j >= 0 && j >= i - window; j--)
            if (ins[j].OpCode.Code == code && ins[j].Operand is T) return (T)ins[j].Operand;
        return null;
    }

    static readonly string[] GameAsms = { "Assembly-CSharp", "Assembly-CSharp-firstpass", "UnityEngine.CoreModule", "UnityEngine.UI", "Unity.TextMeshPro", "RDTools", "SkyHook.Unity" };
    static TypeDefinition FindType(ModuleDefinition mod, IAssemblyResolver res, string full)
    {
        foreach (var a in GameAsms)
        {
            try
            {
                var asm = res.Resolve(new AssemblyNameReference(a, null));
                var t = asm.MainModule.GetType(full.Replace('+', '/'));
                if (t != null) return t;
            }
            catch { }
        }
        return mod.GetType(full);
    }

    static IEnumerable<TypeDefinition> Hier(TypeDefinition t)
    {
        for (; t != null; ) { yield return t; try { t = t.BaseType?.Resolve(); } catch { t = null; } }
    }

    static IEnumerable<TypeDefinition> AllTypes(IEnumerable<TypeDefinition> ts)
    {
        foreach (var t in ts) { yield return t; foreach (var n in AllTypes(t.NestedTypes)) yield return n; }
    }

    static Tuple<TypeReference, string, TypeReference[]> Merge(Tuple<TypeReference, string, TypeReference[]> a, Tuple<TypeReference, string, TypeReference[]> b)
    {
        if (b == null) return a;
        return Tuple.Create(b.Item1 ?? a.Item1, b.Item2 ?? a.Item2, b.Item3 ?? a.Item3);
    }

    static Tuple<TypeReference, string, TypeReference[]> PatchArgs(IEnumerable<CustomAttribute> attrs)
    {
        Tuple<TypeReference, string, TypeReference[]> acc = null;
        foreach (var ca in attrs.Where(x => x.AttributeType.Name == "HarmonyPatch"))
        {
            TypeReference type = null; string name = null; TypeReference[] args = null;
            foreach (var arg in ca.ConstructorArguments)
            {
                if (arg.Value is TypeReference) type = (TypeReference)arg.Value;
                else if (arg.Value is string) name = (string)arg.Value;
                else if (arg.Value is CustomAttributeArgument[])
                {
                    var arr = (CustomAttributeArgument[])arg.Value;
                    if (arr.All(x => x.Value is TypeReference)) args = arr.Select(x => (TypeReference)x.Value).ToArray();
                }
            }
            var cur = Tuple.Create(type, name, args);
            acc = acc == null ? cur : Merge(acc, cur);
        }
        return acc;
    }
}
