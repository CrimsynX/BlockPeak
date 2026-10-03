using Mono.Cecil;

// usage: Publicizer <in.dll> <out.dll>
if (args.Length < 2)
{
    Console.Error.WriteLine("usage: Publicizer <in.dll> <out.dll>");
    return 1;
}
var input = args[0];
var output = args[1];
var resolver = new DefaultAssemblyResolver();
resolver.AddSearchDirectory(Path.GetDirectoryName(Path.GetFullPath(input))!);
using var asm = AssemblyDefinition.ReadAssembly(input, new ReaderParameters { AssemblyResolver = resolver });
int n = 0;
foreach (var t in asm.MainModule.GetTypes())
{
    if (t.IsNested) t.IsNestedPublic = true; else t.IsPublic = true;
    foreach (var m in t.Methods) { if (!m.IsPublic) { m.IsPublic = true; n++; } }
    foreach (var f in t.Fields)
    {
        // Event backing fields share the event's name; making them public would clash.
        if (t.Events.Any(e => e.Name == f.Name)) continue;
        if (!f.IsPublic) { f.IsPublic = true; n++; }
    }
}
Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output))!);
asm.Write(output);
Console.WriteLine($"publicized {n} members -> {output}");
return 0;
