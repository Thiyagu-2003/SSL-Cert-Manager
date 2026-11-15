using System.IO;
using Xunit;

namespace CertManager.UnitTests;

public class LogoGeneratorTests
{
    [Fact]
    public void VerifyCertFlowAssetsExist()
    {
        string assetsDir = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../src/CertManager.Desktop/Assets"));
        
        string svgPath = Path.Combine(assetsDir, "certflow_logo.svg");
        string icoPath = Path.Combine(assetsDir, "certflow.ico");

        Assert.True(File.Exists(svgPath), "certflow_logo.svg should exist");
        Assert.True(File.Exists(icoPath), "certflow.ico should exist");
        Assert.True(new FileInfo(svgPath).Length > 0, "certflow_logo.svg should not be empty");
        Assert.True(new FileInfo(icoPath).Length > 0, "certflow.ico should not be empty");
    }
}
