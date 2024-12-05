[HttpPost("run-jobs")]
public IActionResult RunJobsWithUnloadDetection([FromServices] DllStorageService dllStorage)
{
    var dllFiles = dllStorage.GetAllDlls();
    if (dllFiles == null || !dllFiles.Any())
    {
        return BadRequest("No DLL files are available to run.");
    }

    var executionResults = new List<string>();

    foreach (var dllFile in dllFiles)
    {
        // Create a unique friendly name for the AppDomain
        string domainName = $"JobExecutionDomain_{Guid.NewGuid()}";

        try
        {
            // Create a new AppDomain with shadow copying disabled
            AppDomainSetup setup = new AppDomainSetup
            {
                ShadowCopyFiles = "false"
            };

            // Create the new AppDomain
            AppDomain jobDomain = AppDomain.CreateDomain(domainName, null, setup);

            try
            {
                // Use a worker class that can be marshaled between AppDomains
                var jobExecutor = (JobExecutor)jobDomain.CreateInstanceAndUnwrap(
                    typeof(JobExecutor).Assembly.FullName, 
                    typeof(JobExecutor).FullName
                );

                // Execute jobs in the new AppDomain
                var domainResults = jobExecutor.ExecuteJobs(dllFile.Value);
                executionResults.AddRange(domainResults);
            }
            catch (Exception ex)
            {
                executionResults.Add($"Error in job execution for {dllFile.Key}: {ex.Message}");
            }
            finally
            {
                // Unload the AppDomain
                AppDomain.Unload(jobDomain);
                executionResults.Add($"AppDomain for {dllFile.Key} was unloaded.");
            }
        }
        catch (Exception ex)
        {
            executionResults.Add($"Critical error processing {dllFile.Key}: {ex.Message}");
        }
    }

    return Ok(new
    {
        Message = "Job execution completed.",
        Results = executionResults
    });
}

// Serializable worker class to execute jobs
public class JobExecutor : MarshalByRefObject
{
    public List<string> ExecuteJobs(byte[] dllBytes)
    {
        var results = new List<string>();

        try
        {
            // Load the assembly in the new AppDomain
            using (var assemblyStream = new MemoryStream(dllBytes))
            {
                var assembly = Assembly.Load(assemblyStream.ToArray());

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
                        results.Add($"Executed job: {jobType.FullName}");
                    }
                    catch (Exception ex)
                    {
                        results.Add($"Error executing job: {jobType.FullName}. Error: {ex.Message}");
                    }
                }

                if (!jobTypes.Any())
                {
                    results.Add("No jobs found in the assembly.");
                }
            }
        }
        catch (Exception ex)
        {
            results.Add($"Error loading or processing assembly: {ex.Message}");
        }

        return results;
    }
}
