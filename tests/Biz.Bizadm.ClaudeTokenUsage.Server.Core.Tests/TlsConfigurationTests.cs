namespace Biz.Bizadm.ClaudeTokenUsage.Server.Core.Tests;

[TestClass]
public sealed class TlsConfigurationTests
{
    [TestMethod]
    public void Validate_UseTlsWithoutCertificate_Throws()
    {
        TlsConfiguration configuration = new() { UseTls = true };
        Assert.ThrowsExactly<ArgumentNullException>(configuration.Validate);
    }

    [TestMethod]
    public void Validate_UseTlsFalseWithoutCertificate_Passes()
    {
        new TlsConfiguration { UseTls = false }.Validate();
    }

    [TestMethod]
    public void Validate_UseTlsWithCertificateAndPassword_Passes()
    {
        new TlsConfiguration { UseTls = true, ServerCertificate = "server.pfx", ServerCertificatePassword = "secret" }.Validate();
    }
}
