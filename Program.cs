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
