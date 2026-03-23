namespace TeamWare.Web.Services;

public interface IFileStorageService
{
    Task SaveFileAsync(Stream stream, string storedFileName);

    Task<Stream> GetFileStreamAsync(string storedFileName);

    Task DeleteFileAsync(string storedFileName);
}
