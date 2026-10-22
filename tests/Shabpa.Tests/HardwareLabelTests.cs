using Shabpa.Core;

namespace Shabpa.Tests;

public class HardwareLabelTests
{
    [Theory]
    [InlineData("12th Gen Intel(R) Core(TM) i7-12700K", "Core i7-12700K")]
    [InlineData("Intel(R) Core(TM) i7-12700K CPU @ 3.60GHz", "Core i7-12700K")]
    [InlineData("AMD Ryzen 7 5800X 8-Core Processor", "Ryzen 7 5800X")]
    [InlineData("AMD Ryzen 9 7950X3D 16-Core Processor", "Ryzen 9 7950X3D")]
    [InlineData("", "")]
    public void Cpu_names_are_shortened(string raw, string expected) => Assert.Equal(expected, HardwareLabel.Cpu(raw));

    [Theory]
    [InlineData("AMD Radeon RX 6900 XT", "RX 6900 XT")]
    [InlineData("NVIDIA GeForce RTX 4070 SUPER", "RTX 4070 SUPER")]
    [InlineData("Intel(R) UHD Graphics 770", "UHD 770")]
    public void Gpu_names_are_shortened(string raw, string expected) => Assert.Equal(expected, HardwareLabel.Gpu(raw));

    [Fact]
    public void Title_adds_the_name_only_when_there_is_one()
    {
        Assert.Equal("Processor · Ryzen 7 5800X", HardwareLabel.Title("Processor", "Ryzen 7 5800X"));
        Assert.Equal("Processor", HardwareLabel.Title("Processor", ""));
    }
}
