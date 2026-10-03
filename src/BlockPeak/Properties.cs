using System.Runtime.CompilerServices;

// The game assemblies are referenced in "publicized" form (everything public) so BlockPeak can reach PEAK's
// internals. Mono does not check access at runtime; these attributes make that explicit for runtimes that do.
[assembly: IgnoresAccessChecksTo("Assembly-CSharp")]
[assembly: IgnoresAccessChecksTo("PhotonUnityNetworking")]
[assembly: IgnoresAccessChecksTo("Zorro.Core.Runtime")]
[assembly: IgnoresAccessChecksTo("Zorro.PhotonUtility")]
[assembly: IgnoresAccessChecksTo("Zorro.Settings.Runtime")]

namespace System.Runtime.CompilerServices
{
    [AttributeUsage(AttributeTargets.Assembly, AllowMultiple = true)]
    internal sealed class IgnoresAccessChecksToAttribute : Attribute
    {
        public IgnoresAccessChecksToAttribute(string assemblyName) { AssemblyName = assemblyName; }
        public string AssemblyName { get; }
    }
}
