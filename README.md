public class DiagnosticAssemblyLoadContext : AssemblyLoadContext
{
    private readonly List<object> _trackedObjects = new List<object>();
    private readonly List<WeakReference> _weakReferences = new List<WeakReference>();

    public DiagnosticAssemblyLoadContext() : base(isCollectible: true)
    {
        // Register resolving event to track potential resource holding
        Resolving += OnResolving;
    }

    private Assembly OnResolving(AssemblyLoadContext context, AssemblyName assemblyName)
    {
        Console.WriteLine($"Resolving assembly: {assemblyName}");
        return null;
    }

    // Override LoadFromStream to track loaded assemblies
    public new Assembly LoadFromStream(Stream stream)
    {
        var assembly = base.LoadFromStream(stream);
        
        // Create weak reference to track potential memory leaks
        _weakReferences.Add(new WeakReference(assembly));
        
        Console.WriteLine($"Loaded assembly: {assembly.FullName}");
        
        return assembly;
    }

    // Method to track objects that might prevent unloading
    public void TrackObject(object obj)
    {
        if (obj != null)
        {
            _trackedObjects.Add(obj);
            _weakReferences.Add(new WeakReference(obj));
            Console.WriteLine($"Tracking object: {obj.GetType().FullName}");
        }
    }

    // Diagnostic method to check what's preventing unloading
    public void DiagnoseUnloadingIssues()
    {
        Console.WriteLine("Diagnosing potential unloading issues:");
        
        // Check tracked objects
        for (int i = 0; i < _trackedObjects.Count; i++)
        {
            Console.WriteLine($"Tracked Object {i}: {_trackedObjects[i]?.GetType().FullName ?? "null"}");
        }

        // Check weak references
        Console.WriteLine("\nWeak Reference Status:");
        for (int i = 0; i < _weakReferences.Count; i++)
        {
            bool isAlive = _weakReferences[i].IsAlive;
            object target = _weakReferences[i].Target;
            
            Console.WriteLine($"Reference {i}: " +
                $"IsAlive: {isAlive}, " +
                $"Type: {target?.GetType().FullName ?? "null"}");
        }
    }

    protected override Assembly Load(AssemblyName assemblyName)
    {
        Console.WriteLine($"Attempting to load assembly: {assemblyName}");
        return null;
    }
}

[HttpPost("run-jobs")]
public async Task<IActionResult> RunJobsWithUnloadDetection([FromServices] DllStorageService dllStorage)
{
    var dllFiles = dllStorage.GetAllDlls();
    if (dllFiles == null || !dllFiles.Any())
    {
        return BadRequest("No DLL files are available to run.");
    }

    var executionResults = new List<string>();

    foreach (var dllFile in dllFiles)
    {
        // Create a custom, unloadable AssemblyLoadContext
        var loadContext = new DiagnosticAssemblyLoadContext();
        
        // Create a weak reference to track unloading
        var weakReference = new WeakReference(loadContext);

        try
        {
            // Load the assembly into the custom context
            Assembly assembly;
            using (var memoryStream = new MemoryStream(dllFile.Value))
            {
                assembly = loadContext.LoadFromStream(memoryStream);
            }

            // Find job types
            var jobTypes = assembly.GetTypes()
                .Where(t => typeof(IJob).IsAssignableFrom(t) && 
                            !t.IsInterface && 
                            !t.IsAbstract)
                .ToList();

            // Execute jobs
            foreach (var jobType in jobTypes)
            {
                try
                {
                    // Create and execute job
                    var jobInstance = (IJob)Activator.CreateInstance(jobType);
                    
                    // Track the job instance
                    (loadContext as DiagnosticAssemblyLoadContext)?.TrackObject(jobInstance);

                    // Use a cancellation token to potentially cancel long-running jobs
                    var cancellationTokenSource = new CancellationTokenSource(TimeSpan.FromMinutes(5));
                    
                    await Task.Run(() => 
                    {
                        jobInstance.Execute();
                    }, cancellationTokenSource.Token);

                    executionResults.Add($"Executed job: {jobType.FullName}");
                }
                catch (Exception ex)
                {
                    executionResults.Add($"Error executing job {jobType.FullName}: {ex.Message}");
                }
            }

            if (!jobTypes.Any())
            {
                executionResults.Add($"No jobs found in {dllFile.Key}");
            }
        }
        catch (Exception ex)
        {
            executionResults.Add($"Error loading assembly {dllFile.Key}: {ex.Message}");
        }
        finally
        {
            // Attempt to unload with detailed diagnostics
            for (int attempt = 0; attempt < 3; attempt++)
            {
                try
                {
                    // Unload the AssemblyLoadContext
                    loadContext.Unload();

                    // Trigger aggressive garbage collection
                    for (int i = 0; i < 3; i++)
                    {
                        GC.Collect(2, GCCollectionMode.Aggressive);
                        GC.WaitForPendingFinalizers();
                        GC.Collect(2, GCCollectionMode.Aggressive);
                    }

                    // Check if unloading was successful
                    if (!weakReference.IsAlive)
                    {
                        executionResults.Add($"Successfully unloaded AssemblyLoadContext for {dllFile.Key} on attempt {attempt + 1}");
                        break;
                    }
                    else
                    {
                        // Diagnose why it's not unloading
                        (loadContext as DiagnosticAssemblyLoadContext)?.DiagnoseUnloadingIssues();
                    }
                }
                catch (Exception unloadEx)
                {
                    executionResults.Add($"Unload attempt {attempt + 1} failed: {unloadEx.Message}");
                }

                // Small delay between attempts
                await Task.Delay(500);
            }

            // Final check
            if (weakReference.IsAlive)
            {
                executionResults.Add($"Failed to unload AssemblyLoadContext for {dllFile.Key} after multiple attempts");
            }
        }
    }

    return Ok(new
    {
        Message = "Job execution completed.",
        Results = executionResults
    });
}
