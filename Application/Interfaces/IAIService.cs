namespace Application.Interfaces
{
    public interface IAIService
    {
        Task<string> GenerateNostalgicTextAsync(string prompt);
    }
}