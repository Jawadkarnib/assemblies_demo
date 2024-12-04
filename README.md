[HttpPost("run-jobs")]
public IActionResult RunJobsWithUnload([FromServices] DllStorageService dllStorage)
{
    var dllFiles = dllStorage.GetAllDlls();
    if (dllFiles == null || !dllFiles.Any())
    {
        return BadRequest("No DLL files are available to run.");
    }

    var executionResults = new List<string>();

    foreach (var dllFile in dllFiles)
    {
        // Use a custom AssemblyLoadContext for loading and unloading
        var loadContext = new UnloadableAssemblyLoadContext();

        try
        {
            // Load the assembly into the custom AssemblyLoadContext
            using (var assemblyStream = new MemoryStream(dllFile.Value))
            {
                var assembly = loadContext.LoadFromStream(assemblyStream);

                // Find types that implement the IJob interface
                var jobTypes = assembly.GetTypes()
                    .Where(t => typeof(IJob).IsAssignableFrom(t) && !t.IsInterface && !t.IsAbstract);

                foreach (var jobType in jobTypes)
                {
                    try
                    {
                        // Create an instance of the job and execute it
                        var jobInstance = (IJob)Activator.CreateInstance(jobType);
                        jobInstance.Execute();
                        executionResults.Add($"Executed job: {jobType.FullName} from {dllFile.Key}");
                    }
                    catch (Exception ex)
                    {
                        executionResults.Add($"Error executing job: {jobType.FullName} from {dllFile.Key}. Error: {ex.Message}");
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
        finally
        {
            // Unload the assembly to free memory
            loadContext.Unload();

            // Force garbage collection to reclaim memory immediately
            GC.Collect();
            GC.WaitForPendingFinalizers();

            // Additional GC pass to ensure full cleanup of the load context
            GC.Collect();
            GC.WaitForPendingFinalizers();
        }
    }

    return Ok(new
    {
        Message = "Job execution completed.",
        Results = executionResults
    });
}
