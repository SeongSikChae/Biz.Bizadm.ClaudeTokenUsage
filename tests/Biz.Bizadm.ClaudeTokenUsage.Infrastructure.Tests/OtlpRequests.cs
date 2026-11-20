using OpenTelemetry.Proto.Collector.Metrics.V1;
using OpenTelemetry.Proto.Common.V1;
using OpenTelemetry.Proto.Metrics.V1;
using OpenTelemetry.Proto.Resource.V1;

namespace Biz.Bizadm.ClaudeTokenUsage.Infrastructure.Tests;

/// <summary>
/// 테스트용 OTLP 요청 생성기.
/// </summary>
internal static class OtlpRequests
{
    public static ulong UnixNano(DateTimeOffset time) => (ulong)(time.ToUnixTimeMilliseconds() * 1_000_000);

    public static KeyValue Attr(string key, string value) => new() { Key = key, Value = new AnyValue { StringValue = value } };

    public static NumberDataPoint IntPoint(DateTimeOffset time, long value, params KeyValue[] attributes)
    {
        NumberDataPoint point = new() { TimeUnixNano = UnixNano(time), StartTimeUnixNano = UnixNano(time.AddMinutes(-1)), AsInt = value };
        point.Attributes.AddRange(attributes);
        return point;
    }

    public static NumberDataPoint DoublePoint(DateTimeOffset time, double value, params KeyValue[] attributes)
    {
        NumberDataPoint point = new() { TimeUnixNano = UnixNano(time), StartTimeUnixNano = UnixNano(time.AddMinutes(-1)), AsDouble = value };
        point.Attributes.AddRange(attributes);
        return point;
    }

    public static Metric Sum(string name, AggregationTemporality temporality, params NumberDataPoint[] points)
    {
        Metric metric = new() { Name = name, Sum = new Sum { AggregationTemporality = temporality, IsMonotonic = true } };
        metric.Sum.DataPoints.AddRange(points);
        return metric;
    }

    public static ExportMetricsServiceRequest Request(IEnumerable<KeyValue> resourceAttributes, params Metric[] metrics)
    {
        Resource resource = new();
        resource.Attributes.AddRange(resourceAttributes);
        ScopeMetrics scope = new() { Scope = new InstrumentationScope { Name = "com.anthropic.claude_code" } };
        scope.Metrics.AddRange(metrics);
        ResourceMetrics resourceMetrics = new() { Resource = resource };
        resourceMetrics.ScopeMetrics.Add(scope);
        ExportMetricsServiceRequest request = new();
        request.ResourceMetrics.Add(resourceMetrics);
        return request;
    }

    public static ExportMetricsServiceRequest Request(params Metric[] metrics) => Request([], metrics);
}
