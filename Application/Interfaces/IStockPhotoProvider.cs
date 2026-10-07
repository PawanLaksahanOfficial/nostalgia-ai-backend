using Application.DTOs;

namespace Application.Interfaces
{
    public interface IStockPhotoProvider
    {
        // The site credited in the video, as in "Photos by Ana Silva on Pixabay".
        string SourceName { get; }
        bool IsEnabled { get; }
        Task<IReadOnlyList<StockPhoto>> SearchAsync(string query, int count, CancellationToken cancellationToken = default);
        Task<bool> DownloadAsync(StockPhoto photo, string destinationPath, CancellationToken cancellationToken = default);
    }
}
