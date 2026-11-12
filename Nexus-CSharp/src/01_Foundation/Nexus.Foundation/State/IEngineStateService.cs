namespace Nexus.Foundation.State
{
    public interface IEngineStateService
    {
        bool IsBusy { get; set; }
        string ActiveModule { get; set; }
        string LastError { get; set; }
    }
}
