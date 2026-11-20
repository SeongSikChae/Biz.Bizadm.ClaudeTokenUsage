using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using OpenTelemetry.Proto.Collector.Metrics.V1;
using OpenTelemetry.Proto.Common.V1;
using OpenTelemetry.Proto.Metrics.V1;
using OpenTelemetry.Proto.Resource.V1;
using Biz.Bizadm.ClaudeTokenUsage.Core;
using Biz.Bizadm.ClaudeTokenUsage.Infrastructure.Storage;
using Biz.Bizadm.ClaudeTokenUsage.Server.Core;

namespace Biz.Bizadm.ClaudeTokenUsage.Server.Spa.Tests;

/// <summary>
/// 임시 콘텐츠 루트·DB와 가짜 시계로 서버를 TestServer에 띄운다.
/// </summary>
internal sealed class TestApp : IAsyncDisposable
{
    private readonly string root;

    private TestApp(string root, WebApplication app, FakeTimeProvider time)
    {
        this.root = root;
        App = app;
        Time = time;
    }

    public WebApplication App { get; }

    public FakeTimeProvider Time { get; }

    public HttpClient Client => App.GetTestClient();

    public DuckDbUsageStore Store => App.Services.GetRequiredService<DuckDbUsageStore>();

    public static async Task<TestApp> StartAsync(DateTime nowKst)
    {
        string root = Path.Combine(Path.GetTempPath(), "Biz.Bizadm.ClaudeTokenUsage.Server.Spa.Tests." + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        FakeTimeProvider time = new(KstClock.FromKst(nowKst));

        AppConfiguration appConfiguration = new()
        {
            ContentRootPath = root,
            Port = 5000,
            WebRootPath = "ClientApp",
            DatabaseFilePath = "data/usage.duckdb",
            OsKms = new OsKmsConfiguration(),
        };

        WebApplication app = Program.CreateWebApplication(["--environment", "Production"], appConfiguration, Path.Combine(root, "log"),
            builder =>
            {
                builder.WebHost.UseTestServer();
                builder.Services.AddSingleton<TimeProvider>(time);
            });
        await app.StartAsync();
        return new TestApp(root, app, time);
    }

    public void SetNow(DateTime nowKst) => Time.SetUtcNow(KstClock.FromKst(nowKst));

    public async ValueTask DisposeAsync()
    {
        await App.StopAsync();
        await App.DisposeAsync();
        if (Directory.Exists(root))
            Directory.Delete(root, recursive: true);
    }

    public static KeyValue Attr(string key, string value) => new() { Key = key, Value = new AnyValue { StringValue = value } };

    /// <summary>
    /// 토큰 포인트 하나(필요하면 비용 포인트 하나)를 담은 OTLP 요청.
    /// </summary>
    public static ExportMetricsServiceRequest Request(DateTime kst, long tokens, string type = "input", double? cost = null,
        AggregationTemporality temporality = AggregationTemporality.Delta, string metricName = UsageAttributes.TokenUsageMetric)
    {
        ulong nano = (ulong)(KstClock.FromKst(kst).ToUnixTimeMilliseconds() * 1_000_000);
        NumberDataPoint tokenPoint = new() { TimeUnixNano = nano, StartTimeUnixNano = nano - 60_000_000_000, AsInt = tokens };
        tokenPoint.Attributes.Add(Attr("model", "claude-opus-5-5"));
        tokenPoint.Attributes.Add(Attr("type", type));
        Metric tokenMetric = new() { Name = metricName, Sum = new Sum { AggregationTemporality = temporality, IsMonotonic = true } };
        tokenMetric.Sum.DataPoints.Add(tokenPoint);

        ScopeMetrics scope = new();
        scope.Metrics.Add(tokenMetric);
        if (cost is not null)
        {
            NumberDataPoint costPoint = new() { TimeUnixNano = nano, StartTimeUnixNano = nano - 60_000_000_000, AsDouble = cost.Value };
            costPoint.Attributes.Add(Attr("model", "claude-opus-5-5"));
            Metric costMetric = new() { Name = UsageAttributes.CostUsageMetric, Sum = new Sum { AggregationTemporality = temporality } };
            costMetric.Sum.DataPoints.Add(costPoint);
            scope.Metrics.Add(costMetric);
        }

        Resource resource = new();
        resource.Attributes.Add(Attr("user.email", "a@example.com"));
        ResourceMetrics resourceMetrics = new() { Resource = resource };
        resourceMetrics.ScopeMetrics.Add(scope);
        ExportMetricsServiceRequest request = new();
        request.ResourceMetrics.Add(resourceMetrics);
        return request;
    }
}
