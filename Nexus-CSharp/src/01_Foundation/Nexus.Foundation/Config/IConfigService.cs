namespace Nexus.Foundation.Config
{
    public interface IConfigService
    {
        AppConfigRecord GetConfig();
        bool SaveConfig(AppConfigRecord config);
    }
}
