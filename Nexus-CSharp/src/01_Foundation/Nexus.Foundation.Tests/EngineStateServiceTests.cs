using Xunit;
using Nexus.Foundation.State;

namespace Nexus.Foundation.Tests
{
    public class EngineStateServiceTests
    {
        [Fact]
        public void EngineState_DefaultValues()
        {
            var service = new EngineStateService();
            Assert.False(service.IsBusy);
            Assert.Equal(string.Empty, service.ActiveModule);
            Assert.Equal(string.Empty, service.LastError);
        }

        [Fact]
        public void EngineState_TransitionsWork()
        {
            var service = new EngineStateService();
            
            service.IsBusy = true;
            service.ActiveModule = "TestModule";
            
            Assert.True(service.IsBusy);
            Assert.Equal("TestModule", service.ActiveModule);
        }
    }
}
