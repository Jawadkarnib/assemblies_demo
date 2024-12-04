using System.Collections.Concurrent;

public class DllStorageService
{
    // Thread-safe dictionary to store DLLs
    private readonly ConcurrentDictionary<string, byte[]> _dllFiles = new();

    // Add or update a DLL file in memory
    public void AddOrUpdateDll(string fileName, byte[] content)
    {
        _dllFiles[fileName] = content;
    }

    // Retrieve all DLL files
    public IDictionary<string, byte[]> GetAllDlls()
    {
        return _dllFiles;
    }

    // Clear stored DLLs
    public void ClearDlls()
    {
        _dllFiles.Clear();
    }
}
