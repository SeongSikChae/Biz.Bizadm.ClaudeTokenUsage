using Google.Protobuf;
using OpenTelemetry.Proto.Collector.Metrics.V1;
using OpenTelemetry.Proto.Metrics.V1;
using Biz.Bizadm.ClaudeTokenUsage.Core;
using Biz.Bizadm.ClaudeTokenUsage.Infrastructure.Otlp;
using static Biz.Bizadm.ClaudeTokenUsage.Infrastructure.Tests.OtlpRequests;

namespace Biz.Bizadm.ClaudeTokenUsage.Infrastructure.Tests;

[TestClass]
public sealed class OtlpMetricsParserTests
{
    private static readonly DateTimeOffset Time = new(2026, 11, 20, 5, 37, 12, TimeSpan.Zero);

    private static ExportMetricsServiceRequest SampleRequest() => Request(
        [Attr("service.name", "claude-code")],
        Sum(UsageAttributes.TokenUsageMetric, AggregationTemporality.Delta,
            IntPoint(Time, 120, Attr("user.email", "a@example.com"), Attr("model", "claude-opus-5-5"), Attr("type", "input")),
            IntPoint(Time, 40, Attr("user.email", "a@example.com"), Attr("model", "claude-opus-5-5"), Attr("type", "output"))),
        Sum(UsageAttributes.CostUsageMetric, AggregationTemporality.Delta,
            DoublePoint(Time, 0.0125, Attr("user.email", "a@example.com"), Attr("model", "claude-opus-5-5"))));

    [TestMethod]
    public void Extract_DeltaTargets_ReturnsTokenAndCostPoints()
    {
        OtlpMetricsBatch batch = OtlpMetricsParser.Extract(SampleRequest());

        Assert.HasCount(2, batch.Tokens);
        Assert.AreEqual(new TokenUsagePoint(Time, "a@example.com", "claude-opus-5-5", "input", 120), batch.Tokens[0]);
        Assert.AreEqual(40, batch.Tokens[1].Tokens);
        Assert.HasCount(1, batch.Costs);
        Assert.AreEqual(0.0125, batch.Costs[0].CostUsd, 1e-12);
        Assert.AreEqual(0, batch.CumulativePointCount);
    }

    [TestMethod]
    public void ParseProtobufAndJson_ProduceSameBatch()
    {
        ExportMetricsServiceRequest request = SampleRequest();
        OtlpMetricsBatch fromProtobuf = OtlpMetricsParser.Extract(OtlpMetricsParser.ParseProtobuf(request.ToByteArray()));
        OtlpMetricsBatch fromJson = OtlpMetricsParser.Extract(OtlpMetricsParser.ParseJson(JsonFormatter.Default.Format(request)));

        CollectionAssert.AreEqual(fromProtobuf.Tokens.ToArray(), fromJson.Tokens.ToArray());
        CollectionAssert.AreEqual(fromProtobuf.Costs.ToArray(), fromJson.Costs.ToArray());
    }

    [TestMethod]
    public void ParseJson_OtlpStyleWithHexExemplarIds_Parses()
    {
        // OTLP JSON 규약: lowerCamelCase, int64는 문자열, enum은 정수, exemplar의 traceId/spanId는 hex.
        string json = """
            {"resourceMetrics":[{"resource":{"attributes":[{"key":"user.email","value":{"stringValue":"a@example.com"}}]},
              "scopeMetrics":[{"metrics":[{"name":"claude_code.token.usage","unit":"tokens",
                "sum":{"aggregationTemporality":1,"isMonotonic":true,"dataPoints":[
                  {"attributes":[{"key":"type","value":{"stringValue":"input"}},{"key":"model","value":{"stringValue":"claude-sonnet-5-5"}}],
                   "startTimeUnixNano":"1795145772000000000","timeUnixNano":"1795145832000000000","asInt":"77",
                   "exemplars":[{"timeUnixNano":"1795145832000000000","asInt":"1","traceId":"5b8efff798038103d269b633813fc60c","spanId":"eee19b7ec3c1b174"}]}]}}]}]}]}
            """;

        OtlpMetricsBatch batch = OtlpMetricsParser.Extract(OtlpMetricsParser.ParseJson(json));

        Assert.HasCount(1, batch.Tokens);
        Assert.AreEqual("a@example.com", batch.Tokens[0].User);
        Assert.AreEqual("claude-sonnet-5-5", batch.Tokens[0].Model);
        Assert.AreEqual("input", batch.Tokens[0].TokenType);
        Assert.AreEqual(77, batch.Tokens[0].Tokens);
        Assert.AreEqual(DateTimeOffset.FromUnixTimeSeconds(1795145832), batch.Tokens[0].Time);
    }

    [TestMethod]
    public void Extract_OtherMetricsAndCumulative_AreNotReturned()
    {
        ExportMetricsServiceRequest request = Request(
            Sum("claude_code.session.count", AggregationTemporality.Delta, IntPoint(Time, 1)),
            Sum(UsageAttributes.TokenUsageMetric, AggregationTemporality.Cumulative, IntPoint(Time, 10), IntPoint(Time, 20)),
            new Metric { Name = UsageAttributes.TokenUsageMetric, Gauge = new Gauge() });

        OtlpMetricsBatch batch = OtlpMetricsParser.Extract(request);

        Assert.IsEmpty(batch.Tokens);
        Assert.IsEmpty(batch.Costs);
        Assert.AreEqual(2, batch.CumulativePointCount);
    }

    [TestMethod]
    public void Extract_ResourceAttributesFallback_AndUnknown()
    {
        ExportMetricsServiceRequest request = Request(
            [Attr("user.account_uuid", "acc-1"), Attr("model", "resource-model")],
            Sum(UsageAttributes.TokenUsageMetric, AggregationTemporality.Delta, IntPoint(Time, 5)));

        TokenUsagePoint point = OtlpMetricsParser.Extract(request).Tokens.Single();

        Assert.AreEqual("acc-1", point.User);
        Assert.AreEqual("resource-model", point.Model);
        Assert.AreEqual("unknown", point.TokenType);
    }

    [TestMethod]
    public void Extract_DoubleTokenValue_IsRounded()
    {
        ExportMetricsServiceRequest request = Request(
            Sum(UsageAttributes.TokenUsageMetric, AggregationTemporality.Delta, DoublePoint(Time, 41.5, Attr("type", "output"))));

        Assert.AreEqual(42, OtlpMetricsParser.Extract(request).Tokens.Single().Tokens);
    }

    [TestMethod]
    public void Extract_PointWithoutTime_IsSkipped()
    {
        NumberDataPoint point = IntPoint(Time, 5);
        point.TimeUnixNano = 0;
        ExportMetricsServiceRequest request = Request(Sum(UsageAttributes.TokenUsageMetric, AggregationTemporality.Delta, point));

        Assert.IsEmpty(OtlpMetricsParser.Extract(request).Tokens);
    }

    [TestMethod]
    public void ParseProtobuf_Garbage_Throws()
        => Assert.ThrowsExactly<OtlpPayloadException>(() => OtlpMetricsParser.ParseProtobuf([0xFF, 0xFF, 0xFF, 0xFF]));

    [TestMethod]
    [DataRow("not json")]
    [DataRow("[1,2]")]
    [DataRow("{\"resourceMetrics\":\"x\"}")]
    public void ParseJson_Invalid_Throws(string body)
        => Assert.ThrowsExactly<OtlpPayloadException>(() => OtlpMetricsParser.ParseJson(body));
}
