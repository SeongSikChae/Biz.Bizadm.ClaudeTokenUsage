namespace Biz.Bizadm.ClaudeTokenUsage.Server.Core.Tests;

[TestClass]
public sealed class OsKmsConfigurationTests
{
    private static OsKmsConfiguration Create(string credentialService) => new()
    {
        CredentialService = credentialService,
        CredentialAccount = "account",
        Iterations = 100000,
        SecretsDekFilePath = "secrets.dek",
    };

    [TestMethod]
    public void Validate_NonUriCredentialService_Throws()
    {
        OsKmsConfiguration configuration = Create("not-a-uri");
        ArgumentException exception = Assert.ThrowsExactly<ArgumentException>(configuration.Validate);
        Assert.AreEqual(nameof(OsKmsConfiguration.CredentialService), exception.ParamName);
    }

    [TestMethod]
    public void Validate_AbsoluteUriCredentialService_Passes()
    {
        Create("https://biz-bizadm-claudetokenusage.local").Validate();
    }
}
