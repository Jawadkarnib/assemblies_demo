[HttpPost("run")]
public IActionResult RunAllDlls([FromServices] DllStorageService dllStorage)
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

            // Find a type that contains a method to execute (e.g., a class with Main())
            var entryType = assembly.GetTypes()
                .FirstOrDefault(t => t.GetMethod("Main", BindingFlags.Public | BindingFlags.Static) != null);

            if (entryType != null)
            {
                var mainMethod = entryType.GetMethod("Main", BindingFlags.Public | BindingFlags.Static);
                if (mainMethod != null)
                {
                    // Invoke the Main() method
                    var result = mainMethod.Invoke(null, null);
                    executionResults.Add($"Executed {dllFile.Key}: Result = {result}");
                }
                else
                {
                    executionResults.Add($"No suitable entry point found in {dllFile.Key}.");
                }
            }
            else
            {
                executionResults.Add($"No class with a 'Main' method found in {dllFile.Key}.");
            }
        }
        catch (Exception ex)
        {
            executionResults.Add($"Error executing {dllFile.Key}: {ex.Message}");
        }
    }

    return Ok(new
    {
        Message = "Execution completed.",
        Results = executionResults
    });
}
