using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace LolDataExtractor;

/// <summary>
/// The published builds keep every native library in one folder (<c>runtimes\&lt;rid&gt;\</c>)
/// instead of scattering dlls beside the executable. This runs before any native library is
/// touched: it registers that folder as an extra DLL directory and installs a P/Invoke resolver
/// for every loaded assembly, so WPF's rendering dlls and the mp3 encoder are found there.
/// Whisper.net locates <c>runtimes\&lt;rid&gt;\</c> on its own.
/// </summary>
internal static class NativeSearchPath
{
    private static readonly string Directory =
        Path.Combine(AppContext.BaseDirectory, "runtimes", RuntimeInformation.RuntimeIdentifier);

    [ModuleInitializer]
    internal static void Register()
    {
        if (!System.IO.Directory.Exists(Directory)) return;

        try
        {
            SetDllDirectory(Directory);
        }
        catch (Exception)
        {
            // An extra load path is a convenience; the resolver below still covers P/Invokes.
        }

        AppDomain.CurrentDomain.AssemblyLoad += (_, arguments) => Hook(arguments.LoadedAssembly);
        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies()) Hook(assembly);
    }

    private static void Hook(Assembly assembly)
    {
        try
        {
            NativeLibrary.SetDllImportResolver(assembly, Resolve);
        }
        catch (ArgumentException)
        {
            // The assembly already has a resolver of its own; leave it alone.
        }
        catch (InvalidOperationException)
        {
        }
    }

    private static IntPtr Resolve(string name, Assembly assembly, DllImportSearchPath? searchPath)
    {
        var file = name.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) ? name : name + ".dll";
        var candidate = Path.Combine(Directory, file);
        if (!File.Exists(candidate)) return IntPtr.Zero;

        // Zero means "keep looking"; the default probing then reports the usual error.
        return NativeLibrary.Load(candidate);
    }

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool SetDllDirectory(string path);
}
