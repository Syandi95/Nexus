namespace Nexus.Foundation.Config
{
    public class AppConfigRecord
    {
        public string Theme { get; set; } = "Dark";
        public string DownloadsFolder { get; set; } = string.Empty;
        public string ModelRootFolder { get; set; } = @".\Models";
        public string EngineRootFolder { get; set; } = @".\Engine";
        public string? ChatSystemPromptOverride { get; set; }
        public string? AiCheckSystemPromptOverride { get; set; }
        public bool ConfirmExit { get; set; } = true;
        public bool ConfirmModule { get; set; } = true;
        public string DefaultCmykProfilePath { get; set; } = string.Empty;

        public string LastActivityModule { get; set; } = string.Empty;
        public int LastActivityFileCount { get; set; } = 0;
        public long LastActivityTotalSize { get; set; } = 0;
        public string LastActivityTimestamp { get; set; } = string.Empty;

        public string EngineStatus { get; set; } = "Standby";
        public string EngineState { get; set; } = "READY";
        public string ActiveFolderPath { get; set; } = "Downloads";
    }
}
