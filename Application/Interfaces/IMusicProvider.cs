using Application.DTOs;

namespace Application.Interfaces
{
    public interface IMusicProvider
    {
        IReadOnlyList<string> AvailableMoods { get; }
        Task<MusicTrack?> GetTrackAsync(string? mood);
    }
}
