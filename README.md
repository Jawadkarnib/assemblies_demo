public class JobRunner
{
    public void RunJob(byte[] assemblyData)
    {
        var loadContext = new UnloadableAssemblyLoadContext();
        var weakRef = new WeakReference(loadContext);

        try
        {
            using (var memoryStream = new MemoryStream(assemblyData))
            {
                var assembly = loadContext.LoadFromStream(memoryStream);

                // Get all types in the assembly
                var jobTypes = assembly.GetTypes()
                    .Where(t => t.GetInterfaces().Any(i => i.Name == "IJob") && !t.IsInterface && !t.IsAbstract);

                foreach (var jobType in jobTypes)
                {
                    // Create an instance of the type using reflection
                    var jobInstance = Activator.CreateInstance(jobType);

                    // Get the "Execute" method using reflection
                    var executeMethod = jobType.GetMethod("Execute");
                    if (executeMethod != null)
                    {
                        // Invoke the method dynamically
                        executeMethod.Invoke(jobInstance, null);
                    }

                    // Clear any references to the instance
                    jobInstance = null;
                }
            }
        }
        finally
        {
            // Unload the load context
            loadContext.Unload();

            // Force garbage collection to finalize and collect the context
            GC.Collect();
            GC.WaitForPendingFinalizers();

            // Check if the AssemblyLoadContext has been unloaded
            if (weakRef.IsAlive)
            {
                Console.WriteLine("AssemblyLoadContext was NOT unloaded.");
            }
            else
            {
                Console.WriteLine("AssemblyLoadContext was unloaded successfully.");
            }
        }
    }
}
