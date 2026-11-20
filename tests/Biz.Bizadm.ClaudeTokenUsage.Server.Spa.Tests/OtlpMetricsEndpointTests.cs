using Google.Protobuf;
using OpenTelemetry.Proto.Collector.Metrics.V1;
using OpenTelemetry.Proto.Metrics.V1;
using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using Biz.Bizadm.ClaudeTokenUsage.Core;
using Biz.Bizadm.ClaudeTokenUsage.Infrastructure.Storage;

namespace Biz.Bizadm.ClaudeTokenUsage.Server.Spa.Tests;

[TestClass]
public sealed class OtlpMetricsEndpointTests
{
    private static readonly DateTime Now = new(2026, 11, 20, 14, 37, 12);
    private static readonly DateTime PointTime = new(2026, 11, 20, 14, 36, 30);

    private static ByteArrayContent Protobuf(ExportMetricsServiceRequest request)
    {
        ByteArrayContent content = new(request.ToByteArray());
        content.Headers.ContentType = new MediaTypeHeaderValue("application/x-protobuf");
        return content;
    }

    private static StringContent Json(ExportMetricsServiceRequest request)
        => new(JsonFormatter.Default.Format(request), System.Text.Encoding.UTF8, "application/json");

    private static async Task<long> MinuteTotalAsync(TestApp app)
    {
        UsageSeries series = await app.Store.QuerySeriesAsync(new UsageQuery(UsageUnit.Minute, Now.AddHours(-1), Now.AddMinutes(1), UsageGroupBy.TokenType, [], [], []));
        return series.TotalTokens;
    }

    [TestMethod]
    public async Task Protobuf_Returns200WithProtobufResponse_AndStores()
    {
        await using TestApp app = await TestApp.StartAsync(Now);

        HttpResponseMessage response = await app.Client.PostAsync("/v1/metrics", Protobuf(TestApp.Request(PointTime, 120, cost: 0.5)));

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        Assert.AreEqual("application/x-protobuf", response.Content.Headers.ContentType?.MediaType);
        ExportMetricsServiceResponse body = ExportMetricsServiceResponse.Parser.ParseFrom(await response.Content.ReadAsByteArrayAsync());
        Assert.IsNull(body.PartialSuccess);
        Assert.AreEqual(120, await MinuteTotalAsync(app));
    }

    [TestMethod]
    public async Task Json_Returns200WithEmptyObject_AndStores()
    {
        await using TestApp app = await TestApp.StartAsync(Now);

        HttpResponseMessage response = await app.Client.PostAsync("/v1/metrics", Json(TestApp.Request(PointTime, 33)));

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        Assert.AreEqual("application/json", response.Content.Headers.ContentType?.MediaType);
        Assert.AreEqual("{}", await response.Content.ReadAsStringAsync());
        Assert.AreEqual(33, await MinuteTotalAsync(app));
    }

    [TestMethod]
    public async Task GzipProtobuf_Returns200_AndStores()
    {
        await using TestApp app = await TestApp.StartAsync(Now);
        using MemoryStream compressed = new();
        await using (GZipStream gzip = new(compressed, CompressionLevel.Fastest, leaveOpen: true))
            TestApp.Request(PointTime, 9).WriteTo(gzip);
        ByteArrayContent content = new(compressed.ToArray());
        content.Headers.ContentType = new MediaTypeHeaderValue("application/x-protobuf");
        content.Headers.ContentEncoding.Add("gzip");

        HttpResponseMessage response = await app.Client.PostAsync("/v1/metrics", content);

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        Assert.AreEqual(9, await MinuteTotalAsync(app));
    }

    [TestMethod]
    public async Task BrokenBody_Returns400ProblemDetails()
    {
        await using TestApp app = await TestApp.StartAsync(Now);
        ByteArrayContent broken = new([0xFF, 0xFF, 0xFF, 0xFF]);
        broken.Headers.ContentType = new MediaTypeHeaderValue("application/x-protobuf");

        HttpResponseMessage protobuf = await app.Client.PostAsync("/v1/metrics", broken);
        HttpResponseMessage json = await app.Client.PostAsync("/v1/metrics", new StringContent("not json", System.Text.Encoding.UTF8, "application/json"));

        Assert.AreEqual(HttpStatusCode.BadRequest, protobuf.StatusCode);
        Assert.AreEqual("application/problem+json", protobuf.Content.Headers.ContentType?.MediaType);
        Assert.AreEqual(HttpStatusCode.BadRequest, json.StatusCode);
    }

    [TestMethod]
    public async Task UnsupportedContentType_Returns415()
    {
        await using TestApp app = await TestApp.StartAsync(Now);

        HttpResponseMessage response = await app.Client.PostAsync("/v1/metrics", new StringContent("x", System.Text.Encoding.UTF8, "text/plain"));

        Assert.AreEqual(HttpStatusCode.UnsupportedMediaType, response.StatusCode);
        Assert.AreEqual("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [TestMethod]
    public async Task OtherMetricAndCumulative_Return200_ButAreNotStored()
    {
        await using TestApp app = await TestApp.StartAsync(Now);

        HttpResponseMessage other = await app.Client.PostAsync("/v1/metrics", Protobuf(TestApp.Request(PointTime, 5, metricName: "claude_code.session.count")));
        HttpResponseMessage cumulative = await app.Client.PostAsync("/v1/metrics", Protobuf(TestApp.Request(PointTime, 7, temporality: AggregationTemporality.Cumulative)));

        Assert.AreEqual(HttpStatusCode.OK, other.StatusCode);
        Assert.AreEqual(HttpStatusCode.OK, cumulative.StatusCode);
        Assert.AreEqual(0, await MinuteTotalAsync(app));
    }
}
