namespace Application.Interfaces
{
    public interface IFileStorage
    {
        Task<string> UploadAsync(string key, Stream content, string contentType);
        Task<Stream?> DownloadAsync(string key);
        Task<bool> DeleteAsync(string key);
        string GetPublicUrl(string key);
    }
}