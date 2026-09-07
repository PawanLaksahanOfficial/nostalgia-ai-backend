using Application.DTOs;

namespace Application.Interfaces
{
    public interface IVideoComposer
    {
        Task<bool> IsAvailableAsync();
        Task<ComposedVideo> ComposeAsync(VideoCompositionRequest request);
    }
}
