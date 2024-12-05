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
        var loadContext = new UnloadableAssemblyLoadContext();
        
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
                            !t.IsAbstract);

            // Execute jobs
            foreach (var jobType in jobTypes)
            {
                try
                {
                    // Create and execute job
                    var jobInstance = (IJob)Activator.CreateInstance(jobType);
                    
                    // Use a cancellation token to potentially cancel long-running jobs
                    var cancellationTokenSource = new CancellationTokenSource(TimeSpan.FromMinutes(5));
                    
                    await Task.Run(() => 
                    {
                        jobInstance.Execute();
                    }, cancellationTokenSource.Token);

                    executionResults.Add($"Executed job: {jobType.FullName}");
                }
                catch (OperationCanceledException)
                {
                    executionResults.Add($"Job {jobType.FullName} was cancelled due to timeout.");
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
            // Attempt to unload with multiple strategies
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
