public class DllStorage : IDllStorage, IDisposable
{
    private UnloadableAssemblyLoadContext _loadContext;
    private readonly ConcurrentDictionary<string, byte[]> _dllFiles = new();

    public DllStorage()
    {
        _loadContext = new UnloadableAssemblyLoadContext();
    }

    public void AddOrUpdateDll(string fileName, byte[] content)
    {
        _dllFiles[fileName] = content;
    }

    public List<string> LoadDllAsync()
    {
        var executionResults = new List<string>();
        var dllFiles = this.GetAllDlls();
        
        if (dllFiles == null || !dllFiles.Any())
        {
            executionResults.Add("No Dll Files found. Please add one or more assemblies.");
            return executionResults;
        }

        foreach (var dllFile in dllFiles)
        {
            try
            {
                using (var assemblyStream = new MemoryStream(dllFile.Value))
                {
                    var assembly = _loadContext.LoadFromStream(assemblyStream);
                    var jobTypes = assembly.GetTypes()
                        .Where(t => typeof(IJob).IsAssignableFrom(t) && !t.IsInterface && !t.IsAbstract);
                    
                    foreach (var jobType in jobTypes)
                    {
                        try
                        {
                            var jobInstance = (IJob)Activator.CreateInstance(jobType);
                            var output = new StringWriter();
                            Console.SetOut(output);
                            jobInstance.ExecuteAsync();
                            executionResults.Add(
                                $"Executed job: {jobType.FullName} from {dllFile.Key} with output: {output.ToString()}");
                        }
                        catch (Exception ex)
                        {
                            executionResults.Add(
                                $"Error executing job: {jobType.FullName} from {dllFile.Key}. Error: {ex.Message}");
                        }
                    }

                    if (!jobTypes.Any())
                    {
                        executionResults.Add($"No jobs found in {dllFile.Key}.");
                    }
                }
            }
            catch (Exception ex)
            {
                executionResults.Add($"Error loading assembly {dllFile.Key}: {ex.Message}");
            }
        }

        return executionResults;
    }

    public void Unload()
    {
        try
        {
            _loadContext.Unload();
            
            // Create a new load context for future use
            _loadContext = new UnloadableAssemblyLoadContext();
            
            // Force garbage collection
            GC.Collect();
            GC.WaitForPendingFinalizers();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error unloading assembly context: {ex.Message}");
        }
    }

    public void Dispose()
    {
        Unload();
        GC.SuppressFinalize(this);
    }

    // Other methods like GetAllDlls(), GetDll(), etc. remain the same
}


// Using pattern to ensure proper disposal
using (var dllStorage = new DllStorage())
{
    dllStorage.AddOrUpdateDll("MyAssembly.dll", dllBytes);
    var results = dllStorage.LoadDllAsync();
    
    // Explicitly unload if needed before disposing
    dllStorage.Unload();
}

// Or with more explicit control
var dllStorage = new DllStorage();
try 
{
    dllStorage.AddOrUpdateDll("MyAssembly.dll", dllBytes);
    var results = dllStorage.LoadDllAsync();
}
finally
{
    dllStorage.Dispose();
}
