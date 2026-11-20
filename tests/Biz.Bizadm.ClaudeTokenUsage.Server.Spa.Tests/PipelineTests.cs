using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using System.Net;
using System.Net.Http.Headers;
using Biz.Bizadm.ClaudeTokenUsage.Server.Core;

namespace Biz.Bizadm.ClaudeTokenUsage.Server.Spa.Tests;

[TestClass]
public sealed class PipelineTests
{
    private const string IndexHtml = "<!doctype html><title>index</title>";
    private const string AppJs = "console.log('app');";
    private static string tempRoot = null!;

    [ClassInitialize]
    public static void ClassInitialize(TestContext context)
    {
        tempRoot = Path.Combine(Path.GetTempPath(), "Biz.Bizadm.ClaudeTokenUsage.Server.Spa.Tests." + Guid.NewGuid().ToString("N"));
        string clientApp = Path.Combine(tempRoot, "ClientApp");
        Directory.CreateDirectory(clientApp);
        File.WriteAllText(Path.Combine(clientApp, "index.html"), IndexHtml);
        File.WriteAllText(Path.Combine(clientApp, "app.js"), AppJs);
    }

    [ClassCleanup]
    public static void ClassCleanup()
    {
        if (Directory.Exists(tempRoot))
            Directory.Delete(tempRoot, recursive: true);
    }

    private static async Task<WebApplication> StartAsync(Action<WebApplication>? configureApp = null)
    {
        AppConfiguration appConfiguration = new()
        {
            ContentRootPath = tempRoot,
            Port = 5000,
            WebRootPath = "ClientApp",
            DatabaseFilePath = Path.Combine("data", Guid.NewGuid().ToString("N") + ".duckdb"),
            OsKms = new OsKmsConfiguration(),
        };

        WebApplication app = Program.CreateWebApplication(["--environment", "Production"], appConfiguration,
            Path.Combine(tempRoot, "log"), builder => builder.WebHost.UseTestServer());
        configureApp?.Invoke(app);
        await app.StartAsync();
        return app;
    }

    [TestMethod]
    public async Task StaticFile_Returns200WithContent()
    {
        await using WebApplication app = await StartAsync();
        HttpResponseMessage response = await app.GetTestClient().GetAsync("/app.js");
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        Assert.AreEqual(AppJs, await response.Content.ReadAsStringAsync());
    }

    [TestMethod]
    public async Task HtmlFallback_ReturnsIndexHtml()
    {
        await using WebApplication app = await StartAsync();
        HttpRequestMessage request = new(HttpMethod.Get, "/some/client/route");
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/html"));

        HttpResponseMessage response = await app.GetTestClient().SendAsync(request);

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        Assert.AreEqual(IndexHtml, await response.Content.ReadAsStringAsync());
    }

    [TestMethod]
    public async Task NonHtmlMissingRoute_Returns404ProblemDetails()
    {
        await using WebApplication app = await StartAsync();
        HttpRequestMessage request = new(HttpMethod.Get, "/missing");
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        HttpResponseMessage response = await app.GetTestClient().SendAsync(request);

        Assert.AreEqual(HttpStatusCode.NotFound, response.StatusCode);
        Assert.AreEqual("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [TestMethod]
    public async Task Health_Returns200()
    {
        await using WebApplication app = await StartAsync();
        HttpResponseMessage response = await app.GetTestClient().GetAsync("/health");
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
    }

    [TestMethod]
    public async Task UnhandledException_Returns500ProblemDetailsWithoutStackTrace()
    {
        await using WebApplication app = await StartAsync(a =>
            a.MapGet("/test-throw", (Func<string>)(() => throw new InvalidOperationException("test-secret-detail"))));

        HttpResponseMessage response = await app.GetTestClient().GetAsync("/test-throw");
        string body = await response.Content.ReadAsStringAsync();

        Assert.AreEqual(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.AreEqual("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.DoesNotContain("test-secret-detail", body);
        Assert.DoesNotContain("   at ", body);
    }
}
