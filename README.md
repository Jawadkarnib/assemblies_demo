[HttpPost("run-jobs")]
public IActionResult RunJobs([FromServices] DllStorageService dllStorage)
{
    var dllFiles = dllStorage.GetAllDlls();
    if (dllFiles == null || !dllFiles.Any())
    {
        return BadRequest("No DLL files are available to run.");
    }

    var executionResults = new List<string>();

    foreach (var dllFile in dllFiles)
    {
        try
        {
            // Load the DLL from memory
            var assembly = Assembly.Load(dllFile.Value);

            // Find types that implement the IJob interface
            var jobTypes = assembly.GetTypes()
                .Where(t => typeof(IJob).IsAssignableFrom(t) && !t.IsInterface && !t.IsAbstract);

            foreach (var jobType in jobTypes)
            {
                try
                {
                    // Create an instance of the type
                    var jobInstance = (IJob)Activator.CreateInstance(jobType);

                    // Execute the job
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
        catch (Exception ex)
        {
            executionResults.Add($"Error loading assembly {dllFile.Key}: {ex.Message}");
        }
    }

    return Ok(new
    {
        Message = "Job execution completed.",
        Results = executionResults
    });
}
