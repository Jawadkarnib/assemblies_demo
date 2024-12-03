# Create a new directory for the project
$projectName = "DynamicAssemblyLoader"
New-Item -ItemType Directory -Path $projectName
Set-Location $projectName

# Create the project
dotnet new console

# Remove the default Program.cs
Remove-Item -Path "Program.cs"

# Create the DynamicAssemblyManager.cs file
@"
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
"@ | Out-File -FilePath "DynamicAssemblyManager.cs"

# Create the Program.cs file
@"
using System;
using System.IO;

class Program
{
    static void Main(string[] args)
    {
        var assemblyManager = new DynamicAssemblyManager();

        try
        {
            // Example usage
            if (args.Length > 0)
            {
                string zipPath = args[0];
                Console.WriteLine($"Loading assemblies from: {zipPath}");
                assemblyManager.LoadAssembliesFromZip(zipPath);

                // Optional: Pack assemblies back to a new zip
                if (args.Length > 1)
                {
                    string outputZipPath = args[1];
                    assemblyManager.PackAssembliesToZip(outputZipPath);
                }
            }
            else
            {
                Console.WriteLine("Please provide a zip file path as an argument.");
                Console.WriteLine("Usage: dotnet run <input_zip_path> [output_zip_path]");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"An error occurred: {ex.Message}");
        }
    }
}
"@ | Out-File -FilePath "Program.cs"

# Add System.IO.Compression package
dotnet add package System.IO.Compression.FileSystem

# Restore and build the project
dotnet restore
dotnet build

# Create a README with usage instructions
@"
# Dynamic Assembly Loader

## Usage

1. Create a zip file containing the DLLs you want to load
2. Run the application with the zip file path:

```powershell
dotnet run path/to/your/assemblies.zip [optional_output_zip_path]
```

## Example

```powershell
# Load assemblies from MyAssemblies.zip
dotnet run MyAssemblies.zip

# Load assemblies and pack them to a new zip
dotnet run MyAssemblies.zip OutputAssemblies.zip
```
"@ | Out-File -FilePath "README.md"

# Print completion message
Write-Host "Project '$projectName' created successfully!" -ForegroundColor Green
Write-Host "To run the project, use: dotnet run <zip_file_path>" -ForegroundColor Cyan
