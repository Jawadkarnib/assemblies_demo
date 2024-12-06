public class DllStorage : IDllStorage
{
    // Singleton implementation
    private static readonly Lazy<DllStorage> _instance = new Lazy<DllStorage>(() => new DllStorage());
    public static DllStorage Instance => _instance.Value;

    // Heap-level unloadable context
    private static UnloadableAssemblyLoadContext _loadContext;
    private WeakReference<UnloadableAssemblyLoadContext> _weakLoadContext;

    private readonly ConcurrentDictionary<string, byte[]> _dllFiles = new();

    // Private constructor for singleton
    private DllStorage()
    {
        // Initialize the load context at the class level
        _loadContext = new UnloadableAssemblyLoadContext();
        _weakLoadContext = new WeakReference<UnloadableAssemblyLoadContext>(_loadContext);
    }

    public void AddOrUpdateDll(string fileName, byte[] content)
    {
        _dllFiles[fileName] = content;
    }

    public IDictionary<string, byte[]> GetAllDlls()
    {
        return _dllFiles;
    }

    public byte[] GetDll(string fileName)
    {
        return _dllFiles.TryGetValue(fileName, out var result) ? result : null;
    }

    public void ClearDlls()
    {
        _dllFiles.Clear();
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

    public void UnloadDllAsync()
    {
        try
        {
            // Unload the current context
            _loadContext.Unload();

            // Verify weak reference
            bool isAlive = _weakLoadContext.TryGetTarget(out _);
            
            // Create a new load context for future use
            _loadContext = new UnloadableAssemblyLoadContext();
            _weakLoadContext = new WeakReference<UnloadableAssemblyLoadContext>(_loadContext);

            // Force garbage collection
            GC.Collect();
            GC.WaitForPendingFinalizers();
        }
        catch (Exception ex)
        {
            // Log or handle the exception as needed
            Console.WriteLine($"Error unloading assembly context: {ex.Message}");
        }
    }
}


Usage example:
csharpCopy// Add DLLs
DllStorage.Instance.AddOrUpdateDll("MyAssembly.dll", dllBytes);

// Load and execute
var results = DllStorage.Instance.LoadDllAsync();

// Unload context when done
DllStorage.Instance.UnloadDllAsync();
