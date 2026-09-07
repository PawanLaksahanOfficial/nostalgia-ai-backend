namespace Application.Exceptions
{
    public class AIServiceException : Exception
    {
        public AIServiceException(string message, Exception? innerException = null)
            : base(message, innerException)
        {
        }
    }
}
