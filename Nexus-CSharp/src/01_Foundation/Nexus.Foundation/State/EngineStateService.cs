namespace Nexus.Foundation.State
{
    public class EngineStateService : IEngineStateService
    {
        public bool IsBusy { get; set; }
        public string ActiveModule { get; set; } = string.Empty;
        public string LastError { get; set; } = string.Empty;
    }
}
