[HttpPost("run-jobs")]
public async Task<IActionResult> RunJobsWithUnloadDetection(
    [FromServices] DllStorageService dllStorage,
    [FromServices] ILogger<YourControllerName> logger)
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

            logger.LogInformation($"Found {jobTypes.Count} jobs in {dllFile.Key}");

            // Execute jobs
            foreach (var jobType in jobTypes)
            {
                try
                {
                    logger.LogInformation($"Attempting to create and execute job: {jobType.FullName}");

                    // Create and execute job
                    var jobInstance = (IJob)Activator.CreateInstance(jobType);
                    
                    // Use a cancellation token to potentially cancel long-running jobs
                    var cancellationTokenSource = new CancellationTokenSource(TimeSpan.FromMinutes(5));
                    
                    await Task.Run(() => 
                    {
                        jobInstance.Execute();
                    }, cancellationTokenSource.Token);

                    executionResults.Add($"Executed job: {jobType.FullName}");
                    logger.LogInformation($"Successfully executed job: {jobType.FullName}");
                }
                catch (OperationCanceledException)
                {
                    logger.LogWarning($"Job {jobType.FullName} was cancelled due to timeout.");
                    executionResults.Add($"Job {jobType.FullName} was cancelled due to timeout.");
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, $"Error executing job {jobType.FullName}");
                    executionResults.Add($"Error executing job {jobType.FullName}: {ex.Message}");
                }
            }

            if (!jobTypes.Any())
            {
                executionResults.Add($"No jobs found in {dllFile.Key}");
                logger.LogWarning($"No jobs found in {dllFile.Key}");
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, $"Error loading assembly {dllFile.Key}");
            executionResults.Add($"Error loading assembly {dllFile.Key}: {ex.Message}");
        }
        finally
        {
            // Detailed unloading attempt
            for (int attempt = 0; attempt < 3; attempt++)
            {
                try
                {
                    logger.LogInformation($"Attempting to unload AssemblyLoadContext for {dllFile.Key}, attempt {attempt + 1}");

                    // Explicit cleanup
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
                        logger.LogInformation($"Successfully unloaded AssemblyLoadContext for {dllFile.Key} on attempt {attempt + 1}");
                        executionResults.Add($"Successfully unloaded AssemblyLoadContext for {dllFile.Key} on attempt {attempt + 1}");
                        break;
                    }
                    else
                    {
                        logger.LogWarning($"AssemblyLoadContext for {dllFile.Key} is still alive after unload attempt {attempt + 1}");
                    }
                }
                catch (Exception unloadEx)
                {
                    logger.LogError(unloadEx, $"Unload attempt {attempt + 1} failed for {dllFile.Key}");
                    executionResults.Add($"Unload attempt {attempt + 1} failed: {unloadEx.Message}");
                }

                // Small delay between attempts
                await Task.Delay(500);
            }

            // Final check and logging
            if (weakReference.IsAlive)
            {
                logger.LogError($"Failed to unload AssemblyLoadContext for {dllFile.Key} after multiple attempts");
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

// Diagnostic AssemblyLoadContext with additional logging
public class DiagnosticAssemblyLoadContext : AssemblyLoadContext
{
    private readonly ILogger<DiagnosticAssemblyLoadContext> _logger;

    public DiagnosticAssemblyLoadContext() : base(isCollectible: true)
    {
        // You might need to inject ILogger or use a different logging mechanism
        // This is a placeholder - adjust logging as per your application's logging setup
        _logger = LoggerFactory.Create(builder => builder.AddConsole())
            .CreateLogger<DiagnosticAssemblyLoadContext>();
    }

    protected override Assembly Load(AssemblyName assemblyName)
    {
        _logger.LogInformation($"Attempting to load assembly: {assemblyName}");
        return null;
    }

    // Optionally override Resolving event for more detailed tracking
    protected override IntPtr LoadUnmanagedDll(string unmanagedDllName)
    {
        _logger.LogInformation($"Attempting to load unmanaged dll: {unmanagedDllName}");
        return base.LoadUnmanagedDll(unmanagedDllName);
    }
}
```

Key diagnostic additions:
1. Comprehensive logging throughout the process
2. Detailed tracking of job execution and unloading attempts
3. More aggressive garbage collection
4. Diagnostic `AssemblyLoadContext` with additional logging

Potential reasons for unloading failure:
1. Long-running threads created by jobs
2. Static references holding onto loaded assemblies
3. Unmanaged resources not being properly disposed
4. Singleton or static instances preventing garbage collection

Recommendations:
1. Review your `IJob` implementations:
   - Ensure no static references
   - No long-running background threads
   - Proper disposal of resources
2. Consider implementing `IDisposable` in your jobs
3. Avoid creating static or singleton instances in dynamically loaded assemblies

Example of a more disposal-friendly job:
```csharp
public class MyJob : IJob, IDisposable
{
    private bool _disposed = false;

    public void Execute()
    {
        // Your job logic
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (!_disposed)
        {
            if (disposing)
            {
                // Dispose managed resources
            }

            // Dispose unmanaged resources

            _disposed = true;
        }
    }

    ~MyJob()
    {
        Dispose(false);
    }
}
