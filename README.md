[HttpPost("upload-zip")]
public async Task<IActionResult> UploadZip(IFormFile file, [FromServices] DllStorageService dllStorage)
{
    if (file == null || file.Length == 0 || Path.GetExtension(file.FileName).ToLower() != ".zip")
    {
        return BadRequest("Please upload a valid .zip file.");
    }

    // Save the .zip file to wwwroot/uploads
    var uploadsFolder = Path.Combine(_environment.WebRootPath, "uploads");
    Directory.CreateDirectory(uploadsFolder); // Ensure the directory exists
    var filePath = Path.Combine(uploadsFolder, file.FileName);

    await using (var stream = new FileStream(filePath, FileMode.Create))
    {
        await file.CopyToAsync(stream);
    }

    // Extract DLLs and store in memory
    using (var zipStream = file.OpenReadStream())
    {
        using (var zipArchive = new ZipArchive(zipStream))
        {
            foreach (var entry in zipArchive.Entries.Where(e => e.FullName.EndsWith(".dll", System.StringComparison.OrdinalIgnoreCase)))
            {
                await using (var entryStream = entry.Open())
                {
                    using (var memoryStream = new MemoryStream())
                    {
                        await entryStream.CopyToAsync(memoryStream);
                        dllStorage.AddOrUpdateDll(entry.FullName, memoryStream.ToArray());
                    }
                }
            }
        }
    }

    return Ok(new
    {
        Message = $"{file.FileName} uploaded successfully.",
        DllCount = dllStorage.GetAllDlls().Count,
        ExtractedDlls = dllStorage.GetAllDlls().Keys
    });
}
