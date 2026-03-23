namespace TeamWare.Web.Services;

public class FileStorageService : IFileStorageService
{
    private readonly IGlobalConfigurationService _configService;

    public FileStorageService(IGlobalConfigurationService configService)
    {
        _configService = configService;
    }

    public async Task SaveFileAsync(Stream stream, string storedFileName)
    {
        var directory = await GetAttachmentDirectoryAsync();
        Directory.CreateDirectory(directory);

        var filePath = Path.Combine(directory, storedFileName);
        using var fileStream = new FileStream(filePath, FileMode.Create, FileAccess.Write);
        await stream.CopyToAsync(fileStream);
    }

    public async Task<Stream> GetFileStreamAsync(string storedFileName)
    {
        var directory = await GetAttachmentDirectoryAsync();
        var filePath = Path.Combine(directory, storedFileName);

        if (!File.Exists(filePath))
            throw new FileNotFoundException("Attachment file not found.", filePath);

        return new FileStream(filePath, FileMode.Open, FileAccess.Read);
    }

    public async Task DeleteFileAsync(string storedFileName)
    {
        var directory = await GetAttachmentDirectoryAsync();
        var filePath = Path.Combine(directory, storedFileName);

        if (File.Exists(filePath))
            File.Delete(filePath);
    }

    private async Task<string> GetAttachmentDirectoryAsync()
    {
        var result = await _configService.GetByKeyAsync("ATTACHMENT_DIR");

        if (!result.Succeeded || result.Data is null)
            throw new InvalidOperationException("ATTACHMENT_DIR configuration is not set.");

        return result.Data.Value;
    }
}
