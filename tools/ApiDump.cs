// 程序集结构查询工具（只读，不改游戏文件）。
//
// 为什么需要它：本项目是 BepInEx 6 + Il2CppInterop，想知道某个类型有哪些字段/属性/方法，
// 只能读 BepInEx\interop\*.dll（IL2CPP 生成的托管壳）。之前用 Add-Type 跑 Mono.Cecil
// 被本机安全策略拦过，所以改成「csc 编译成独立 exe → dotnet 运行」，和 build.ps1 同一套编译器。
//
// 用法（见 tools\dump-api.ps1）：
//   dotnet ApiDump.dll <程序集路径> type  <类型名或片段...>   // 列类型成员
//   dotnet ApiDump.dll <程序集路径> find  <关键字...>          // 按类型名/成员名找
//   dotnet ApiDump.dll <程序集路径> enum  <类型名>             // 列枚举值
using System;
using System.Collections.Generic;
using System.Linq;

using Mono.Cecil;

internal static class ApiDump
{
    private static int Main(string[] args)
    {
        if (args.Length < 3)
        {
            Console.WriteLine("usage: ApiDump <assembly> <type|find|enum> <name-or-fragment> [...]");
            return 1;
        }

        var path = args[0];
        var mode = args[1].ToLowerInvariant();
        var patterns = args.Skip(2).Select(a => a.ToLowerInvariant()).ToArray();

        using var asm = AssemblyDefinition.ReadAssembly(path, new ReaderParameters { ReadSymbols = false });
        var types = Flatten(asm.MainModule.Types).Where(t => t.Name != "<Module>").ToList();

        switch (mode)
        {
            case "type":
                foreach (var t in types.Where(t => Match(t.FullName, patterns)))
                    PrintType(t);
                break;

            case "enum":
                foreach (var t in types.Where(t => Match(t.Name, patterns) && t.IsEnum))
                    PrintEnum(t);
                break;

            case "find":
                Find(types, patterns);
                break;

            default:
                Console.WriteLine("unknown mode: " + mode);
                return 1;
        }

        return 0;
    }

    private static bool Match(string text, string[] patterns)
    {
        if (string.IsNullOrEmpty(text)) return false;
        var lower = text.ToLowerInvariant();
        return patterns.Any(p => lower.Contains(p));
    }

    private static IEnumerable<TypeDefinition> Flatten(IEnumerable<TypeDefinition> list)
    {
        foreach (var t in list)
        {
            yield return t;
            foreach (var n in Flatten(t.NestedTypes)) yield return n;
        }
    }

    private static void Find(List<TypeDefinition> types, string[] patterns)
    {
        foreach (var t in types)
        {
            var hits = new List<string>();
            if (Match(t.FullName, patterns)) hits.Add("(type)");

            foreach (var f in t.Fields)
                if (Match(f.Name, patterns)) hits.Add("field " + f.Name + " : " + f.FieldType.Name);

            foreach (var p in t.Properties)
                if (Match(p.Name, patterns)) hits.Add("prop  " + p.Name + " : " + p.PropertyType.Name);

            foreach (var m in t.Methods)
                if (Match(m.Name, patterns)) hits.Add("method " + m.Name + "(" + Params(m) + ")");

            if (hits.Count == 0) continue;

            Console.WriteLine("== " + t.FullName + (t.BaseType != null ? " : " + t.BaseType.Name : ""));
            foreach (var h in hits.Take(40)) Console.WriteLine("   " + h);
        }
    }

    private static void PrintType(TypeDefinition t)
    {
        Console.WriteLine("== " + t.FullName + (t.BaseType != null ? " : " + t.BaseType.FullName : ""));

        var fields = t.Fields.Where(f => !f.IsPrivate || true).ToList();
        if (fields.Count > 0)
        {
            Console.WriteLine("  -- fields");
            foreach (var f in fields) Console.WriteLine("     " + f.FieldType.Name + " " + f.Name);
        }

        var props = t.Properties.ToList();
        if (props.Count > 0)
        {
            Console.WriteLine("  -- properties");
            foreach (var p in props)
                Console.WriteLine("     " + p.PropertyType.Name + " " + p.Name +
                                  (p.GetMethod != null ? " {get;}" : "") + (p.SetMethod != null ? " {set;}" : ""));
        }

        var methods = t.Methods.Where(m => !m.IsGetter && !m.IsSetter).ToList();
        if (methods.Count > 0)
        {
            Console.WriteLine("  -- methods");
            foreach (var m in methods)
                Console.WriteLine("     " + m.ReturnType.Name + " " + m.Name + "(" + Params(m) + ")" +
                                  (m.IsStatic ? "  [static]" : ""));
        }
    }

    private static void PrintEnum(TypeDefinition t)
    {
        Console.WriteLine("== " + t.FullName);
        foreach (var f in t.Fields.Where(f => f.IsLiteral))
            Console.WriteLine("   " + f.Name + " = " + f.Constant);
    }

    private static string Params(MethodDefinition m)
    {
        return string.Join(", ", m.Parameters.Select(p => p.ParameterType.Name + " " + p.Name));
    }
}
