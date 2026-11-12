using Nexus.Foundation.Config;

namespace Nexus.Core.HomeSettings.Tests
{
    public class FakeConfigService : IConfigService
    {
        private AppConfigRecord _config = new AppConfigRecord();

        public bool ShouldFailSave { get; set; }
        public int SaveCount { get; private set; }

        public AppConfigRecord GetConfig()
        {
            return _config;
        }

        public bool SaveConfig(AppConfigRecord config)
        {
            SaveCount++;
            if (ShouldFailSave)
            {
                return false;
            }

            _config = config;
            return true;
        }
    }
}
