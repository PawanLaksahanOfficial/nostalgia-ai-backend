using Application.DTOs;
using Application.Interfaces;

namespace Infrastructure.Services
{
    public class NoOpTextToSpeech : ITextToSpeech
    {
        public bool IsEnabled => false;

        public Task<SpeechResult?> SynthesizeAsync(string text, string outputFilePath) =>
            Task.FromResult<SpeechResult?>(null);
    }
}
