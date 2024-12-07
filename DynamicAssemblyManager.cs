using System;
using System.Collections.Concurrent;
using System.IO;
using System.Reflection;
using System.Runtime.Loader;
using System.IO.Compression;

public class DynamicAssemblyManager
{
    // Stores loaded assemblies with their context
    private ConcurrentDictionary<string, AssemblyLoadContext> _loadedAssemblies 
        = new ConcurrentDictionary<string, AssemblyLoadContext>();

    /// <summary>
    /// Load assemblies from a zip file
    /// </summary>
    /// <param name="zipPath">Path to the zip file containing assemblies</param>
    public void LoadAssembliesFromZip(string zipPath)
    {
        // Ensure the zip exists
        if (!File.Exists(zipPath))
            throw new FileNotFoundException("Zip file not found", zipPath);

        // Create a temporary directory to extract assemblies
        string tempExtractPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(tempExtractPath);

        try
        {
            // Extract zip contents
            ZipFile.ExtractToDirectory(zipPath, tempExtractPath);

            // Load each DLL in the extracted directory
            foreach (var dllPath in Directory.GetFiles(tempExtractPath, "*.dll"))
            {
                var assemblyName = Path.GetFileNameWithoutExtension(dllPath);
                
                // Create a new AssemblyLoadContext for each assembly
                var context = new AssemblyLoadContext(assemblyName, true);
                
                // Load the assembly
                using (var fs = new FileStream(dllPath, FileMode.Open))
                {
                    var assembly = context.LoadFromStream(fs);
                    
                    // Store the context for potential unloading
                    _loadedAssemblies[assemblyName] = context;
                    
                    Console.WriteLine($"Loaded assembly: {assemblyName}");
                }
            }
        }
        finally
        {
            // Clean up temporary extraction directory
            Directory.Delete(tempExtractPath, true);
        }
    }

    /// <summary>
    /// Unload a specific assembly
    /// </summary>
    /// <param name="assemblyName">Name of the assembly to unload</param>
    public void UnloadAssembly(string assemblyName)
    {
        if (_loadedAssemblies.TryRemove(assemblyName, out var context))
        {
            context.Unload();
            Console.WriteLine($"Unloaded assembly: {assemblyName}");
        }
    }

    /// <summary>
    /// Pack loaded assemblies back into a zip
    /// </summary>
    /// <param name="outputZipPath">Path to save the output zip</param>
    public void PackAssembliesToZip(string outputZipPath)
    {
        // Create a temporary directory to collect assemblies
        string tempAssemblyPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(tempAssemblyPath);

        try
        {
            // Copy loaded assemblies to temp directory
            foreach (var assemblyName in _loadedAssemblies.Keys)
            {
                var assembly = Assembly.Load(assemblyName);
                var assemblyPath = assembly.Location;
                
                // Copy to temp directory
                File.Copy(assemblyPath, Path.Combine(tempAssemblyPath, Path.GetFileName(assemblyPath)));
            }

            // Create zip from temporary directory
            ZipFile.CreateFromDirectory(tempAssemblyPath, outputZipPath);
            Console.WriteLine($"Packed assemblies to: {outputZipPath}");
        }
        finally
        {
            // Clean up temporary assembly directory
            Directory.Delete(tempAssemblyPath, true);
        }
    }

    /// <summary>
    /// Get an instance of a type from a loaded assembly
    /// </summary>
    public object CreateInstanceFromAssembly(string assemblyName, string typeName)
    {
        var assembly = Assembly.Load(assemblyName);
        var type = assembly.GetType(typeName);
        return Activator.CreateInstance(type);
    }
}
