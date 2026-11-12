using System;
using Xunit;
using Nexus.App.ViewModels;

namespace Nexus.Integration.Tests
{
    public enum TestFormatEnum
    {
        Jpeg,
        Png,
        Webp
    }

    public class RelayCommandTests
    {
        [Fact]
        public void RelayCommandInt_StringParameter_ShouldConvertAndExecute()
        {
            int receivedValue = 0;
            var command = new RelayCommand<int>(val => receivedValue = val);

            command.Execute("72");
            Assert.Equal(72, receivedValue);

            command.Execute("300");
            Assert.Equal(300, receivedValue);
        }

        [Fact]
        public void RelayCommandDouble_StringParameter_ShouldConvertAndExecute()
        {
            double receivedValue = 0.0;
            var command = new RelayCommand<double>(val => receivedValue = val);

            command.Execute("144.5");
            Assert.Equal(144.5, receivedValue);
        }

        [Fact]
        public void RelayCommandBool_StringParameter_ShouldConvertAndExecute()
        {
            bool receivedValue = false;
            var command = new RelayCommand<bool>(val => receivedValue = val);

            command.Execute("true");
            Assert.True(receivedValue);

            command.Execute("False");
            Assert.False(receivedValue);
        }

        [Fact]
        public void RelayCommandEnum_StringParameter_ShouldConvertAndExecute()
        {
            TestFormatEnum receivedValue = TestFormatEnum.Jpeg;
            var command = new RelayCommand<TestFormatEnum>(val => receivedValue = val);

            command.Execute("Png");
            Assert.Equal(TestFormatEnum.Png, receivedValue);

            command.Execute("webp");
            Assert.Equal(TestFormatEnum.Webp, receivedValue);
        }

        [Fact]
        public void RelayCommandNullableInt_StringParameter_ShouldConvertAndExecute()
        {
            int? receivedValue = null;
            var command = new RelayCommand<int?>(val => receivedValue = val);

            command.Execute("600");
            Assert.Equal(600, receivedValue);

            command.Execute(null);
            Assert.Null(receivedValue);
        }

        [Fact]
        public void RelayCommandInt_InvalidString_ShouldNotThrowException()
        {
            int receivedValue = -1;
            var command = new RelayCommand<int>(val => receivedValue = val);

            // Should not throw InvalidCastException or FormatException
            command.Execute("not_a_number");
            Assert.Equal(-1, receivedValue);
        }
    }
}
