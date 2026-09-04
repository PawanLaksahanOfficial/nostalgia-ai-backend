using Application.DTOs;

namespace Application.Interfaces
{
    public interface ITextToSpeech
    {
        bool IsEnabled { get; }
        Task<SpeechResult?> SynthesizeAsync(string text, string outputFilePath);
    }
}
