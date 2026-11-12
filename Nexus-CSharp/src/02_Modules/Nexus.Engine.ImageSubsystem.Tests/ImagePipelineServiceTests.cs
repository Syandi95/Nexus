using System.IO;
using Nexus.Engine.ImageSubsystem.Services;
using Xunit;

namespace Nexus.Engine.ImageSubsystem.Tests
{
    public class ImagePipelineServiceTests
    {
        [Fact]
        public void CreatePipeline_SetsStateAndReturnsRoot()
        {
            using var service = new ImagePipelineService();
            var root = "C:\\test\\pipeline";
            var result = service.CreatePipeline(root);
            
            Assert.Equal(root, result);
            Assert.Equal(ImagePipelineLifecycleState.Idle, service.CurrentState);
        }

        [Fact]
        public void SetInput_ChangesStateToImageLoaded()
        {
            using var service = new ImagePipelineService();
            service.SetInput("C:\\test\\image.png");
            
            Assert.Equal(ImagePipelineLifecycleState.ImageLoaded, service.CurrentState);
        }

        [Theory]
        [InlineData("C:\\test\\image.png", "c:\\TEST\\image.png", true)]
        [InlineData("C:\\test\\image.png", "C:\\test\\other.png", false)]
        [InlineData("C:\\test\\..\\test\\image.png", "C:\\test\\image.png", true)]
        public void IsSamePath_ComparesCorrectly(string path1, string path2, bool expected)
        {
            using var service = new ImagePipelineService();
            var result = service.IsSamePath(path1, path2);
            
            Assert.Equal(expected, result);
        }
    }
}
