namespace Nexus.Foundation.Logging
{
    public interface ILoggerService
    {
        void LogInfo(string category, string message);
        void LogWarning(string category, string message);
        void LogError(string category, string message);
    }
}
