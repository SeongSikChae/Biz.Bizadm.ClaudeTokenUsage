using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace Biz.Bizadm.ClaudeTokenUsage.Server.Core.Tests;

[TestClass]
public sealed class KestrelTlsTests
{
    private const string Password = "test-password";
    private static string tempRoot = null!;
    private static string thumbprint = null!;

    [ClassInitialize]
    public static void ClassInitialize(TestContext context)
    {
        tempRoot = Path.Combine(Path.GetTempPath(), "Biz.Bizadm.ClaudeTokenUsage.Server.Core.Tests." + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);

        using RSA rsa = RSA.Create(2048);
        CertificateRequest request = new("CN=localhost", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        SubjectAlternativeNameBuilder san = new();
        san.AddDnsName("localhost");
        request.CertificateExtensions.Add(san.Build());
        using X509Certificate2 certificate = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1));
        thumbprint = certificate.Thumbprint;
        File.WriteAllBytes(Path.Combine(tempRoot, "server.pfx"), certificate.Export(X509ContentType.Pkcs12, Password));
    }

    [ClassCleanup]
    public static void ClassCleanup()
    {
        if (Directory.Exists(tempRoot))
            Directory.Delete(tempRoot, recursive: true);
    }

    /// <summary>
    /// HTTP/3(QUIC)가 같은 UDP 포트를 쓰므로 TCP와 UDP가 모두 비어 있는 포트를 찾는다.
    /// </summary>
    private static ushort FindFreePort()
    {
        for (int attempt = 0; attempt < 20; attempt++)
        {
            using TcpListener tcp = new(IPAddress.IPv6Any, 0);
            tcp.Server.DualMode = true;
            tcp.Start();
            int port = ((IPEndPoint)tcp.LocalEndpoint).Port;
            try
            {
                using Socket udp = new(AddressFamily.InterNetworkV6, SocketType.Dgram, ProtocolType.Udp) { DualMode = true };
                udp.Bind(new IPEndPoint(IPAddress.IPv6Any, port));
                return (ushort)port;
            }
            catch (SocketException)
            {
            }
        }
        throw new InvalidOperationException("No free TCP+UDP port.");
    }

    [TestMethod]
    [DataRow(SslProtocols.Tls12)]
    [DataRow(SslProtocols.Tls13)]
    public async Task ConfigureServerKestrel_WithTls_NegotiatesProtocolWithServerCertificate(SslProtocols protocol)
    {
        ushort port = FindFreePort();
        TlsConfiguration tls = new() { UseTls = true, ServerCertificate = "server.pfx", ServerCertificatePassword = Password };

        WebApplicationBuilder builder = WebApplication.CreateBuilder(new WebApplicationOptions { ContentRootPath = tempRoot });
        builder.WebHost.ConfigureServerKestrel(port, tls, tempRoot);
        await using WebApplication app = builder.Build();
        await app.StartAsync();

        using TcpClient client = new();
        await client.ConnectAsync(IPAddress.Loopback, port);
        await using SslStream ssl = new(client.GetStream(), false, (_, _, _, _) => true);
        await ssl.AuthenticateAsClientAsync(new SslClientAuthenticationOptions
        {
            TargetHost = "localhost",
            EnabledSslProtocols = protocol,
        });

        Assert.AreEqual(protocol, ssl.SslProtocol);
        Assert.AreEqual(thumbprint, X509CertificateLoader.LoadCertificate(ssl.RemoteCertificate!.GetRawCertData()).Thumbprint);

        await app.StopAsync();
    }
}
