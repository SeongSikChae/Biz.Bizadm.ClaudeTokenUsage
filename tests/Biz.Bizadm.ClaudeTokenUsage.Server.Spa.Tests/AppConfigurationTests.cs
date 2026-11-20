using System.Configuration;
using YamlDotNet.Serialization;

namespace Biz.Bizadm.ClaudeTokenUsage.Server.Spa.Tests;

[TestClass]
public sealed class AppConfigurationTests
{
    private const string ValidYaml = """
        ContentRootPath: C:/apps/claude-token-usage
        Port: 4318
        OsKms:
          CredentialService: https://biz-bizadm-claudetokenusage.local
          CredentialAccount: claudetokenusage
          Iterations: 100000
          SecretsDekFilePath: secrets.dek
        """;

    private static AppConfiguration Parse(string yaml) => new Deserializer().Deserialize<AppConfiguration>(yaml);

    [TestMethod]
    public void Validate_MissingOsKms_ThrowsArgumentNullException()
    {
        AppConfiguration configuration = Parse("""
            ContentRootPath: C:/apps/claude-token-usage
            Port: 4318
            """);

        ArgumentNullException exception = Assert.ThrowsExactly<ArgumentNullException>(() => ConfigurationValidator.Validate(configuration));
        Assert.AreEqual(nameof(AppConfiguration.OsKms), exception.ParamName);
    }

    [TestMethod]
    public void Validate_ValidYaml_FillsDefaults()
    {
        AppConfiguration configuration = Parse(ValidYaml);

        ConfigurationValidator.Validate(configuration);

        Assert.AreEqual("ClientApp", configuration.WebRootPath);
        Assert.AreEqual("data/token-usage.duckdb", configuration.DatabaseFilePath);
        Assert.AreEqual((ushort)4318, configuration.Port);
    }

    [TestMethod]
    public void Validate_ExplicitDatabaseFilePath_IsKept()
    {
        AppConfiguration configuration = Parse(ValidYaml + "\nDatabaseFilePath: db/usage.duckdb\n");

        ConfigurationValidator.Validate(configuration);

        Assert.AreEqual("db/usage.duckdb", configuration.DatabaseFilePath);
    }
}
