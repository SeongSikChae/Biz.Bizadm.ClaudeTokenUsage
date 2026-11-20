using System.Net.Security;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace Biz.Bizadm.ClaudeTokenUsage.Server.Core.Tests;

[TestClass]
public sealed class ServerKestrelExtensionsTests
{
    private static X509Certificate2 CreatePublicCertificate(string commonName)
    {
        using RSA rsa = RSA.Create(2048);
        CertificateRequest request = new($"CN={commonName}", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        using X509Certificate2 withKey = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1));
        return X509CertificateLoader.LoadCertificate(withKey.RawData);
    }

    [TestMethod]
    public void BuildCertificateChain_FollowsConfiguredOrder()
    {
        X509Certificate2Collection collection = [CreatePublicCertificate("Root"), CreatePublicCertificate("Intermediate")];
        ListLogger logger = new();

        X509Certificate2Collection chain = ServerKestrelExtensions.BuildCertificateChain(collection, "Intermediate, Root, Missing", logger);

        Assert.HasCount(2, chain);
        Assert.AreEqual("Intermediate", chain[0].GetNameInfo(X509NameType.SimpleName, false));
        Assert.AreEqual("Root", chain[1].GetNameInfo(X509NameType.SimpleName, false));
        Assert.AreEqual(0, logger.WarningCount);
    }

    [TestMethod]
    public void BuildCertificateChain_DuplicateName_UsesFirstAndWarns()
    {
        X509Certificate2 first = CreatePublicCertificate("Same");
        X509Certificate2 second = CreatePublicCertificate("Same");
        ListLogger logger = new();

        X509Certificate2Collection chain = ServerKestrelExtensions.BuildCertificateChain([first, second], "Same", logger);

        Assert.HasCount(1, chain);
        Assert.AreEqual(first.Thumbprint, chain[0].Thumbprint);
        Assert.AreEqual(1, logger.WarningCount);
    }

    [TestMethod]
    public void ParseCipherSuites_UnknownName_IsExcludedWithWarning()
    {
        ListLogger logger = new();

        IReadOnlyCollection<TlsCipherSuite> suites = ServerKestrelExtensions.ParseCipherSuites("TLS_AES_128_GCM_SHA256, NOT_A_SUITE", logger);

        CollectionAssert.AreEquivalent(new[] { TlsCipherSuite.TLS_AES_128_GCM_SHA256 }, suites.ToArray());
        Assert.AreEqual(1, logger.WarningCount);
    }

    [TestMethod]
    public void WarnIfNoTls13CipherSuite_OnlyTls12Suites_Warns()
    {
        ListLogger logger = new();
        ServerKestrelExtensions.WarnIfNoTls13CipherSuite([TlsCipherSuite.TLS_ECDHE_RSA_WITH_AES_128_GCM_SHA256], logger);
        Assert.AreEqual(1, logger.WarningCount);
    }

    [TestMethod]
    public void WarnIfNoTls13CipherSuite_WithTls13Suite_DoesNotWarn()
    {
        ListLogger logger = new();
        ServerKestrelExtensions.WarnIfNoTls13CipherSuite([TlsCipherSuite.TLS_AES_256_GCM_SHA384], logger);
        Assert.AreEqual(0, logger.WarningCount);
    }

    [TestMethod]
    public void CreateCipherSuitesPolicy_OnWindows_ReturnsNullWithWarning()
    {
        if (!OperatingSystem.IsWindows())
            Assert.Inconclusive("Windows-only branch.");

        ListLogger logger = new();
        CipherSuitesPolicy? policy = ServerKestrelExtensions.CreateCipherSuitesPolicy("TLS_AES_128_GCM_SHA256", logger);

        Assert.IsNull(policy);
        Assert.AreEqual(1, logger.WarningCount);
    }

    [TestMethod]
    public void CreateCipherSuitesPolicy_OnNonWindows_ReturnsPolicy()
    {
        if (OperatingSystem.IsWindows())
            Assert.Inconclusive("Non-Windows-only branch.");

        ListLogger logger = new();
        CipherSuitesPolicy? policy = ServerKestrelExtensions.CreateCipherSuitesPolicy("TLS_AES_128_GCM_SHA256", logger);

        Assert.IsNotNull(policy);
    }

    [TestMethod]
    public void CreateCipherSuitesPolicy_Empty_ReturnsNull()
    {
        Assert.IsNull(ServerKestrelExtensions.CreateCipherSuitesPolicy(" ", new ListLogger()));
    }
}
