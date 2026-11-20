using Google.Protobuf;
using OpenTelemetry.Proto.Collector.Metrics.V1;
using OpenTelemetry.Proto.Common.V1;
using OpenTelemetry.Proto.Metrics.V1;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Biz.Bizadm.ClaudeTokenUsage.Core;

namespace Biz.Bizadm.ClaudeTokenUsage.Infrastructure.Otlp
{
    /// <summary>
    /// OTLP 지표 요청에서 꺼낸 저장 대상 포인트.
    /// </summary>
    /// <param name="CumulativePointCount">대상 지표이지만 누적 시간성이라 저장하지 않은 포인트 수.</param>
    public sealed record OtlpMetricsBatch(IReadOnlyList<TokenUsagePoint> Tokens, IReadOnlyList<CostUsagePoint> Costs, int CumulativePointCount);

    /// <summary>
    /// OTLP/HTTP 지표 요청(protobuf·JSON)을 해석해 <c>claude_code.token.usage</c>·<c>claude_code.cost.usage</c>의
    /// 델타 Sum 포인트만 꺼낸다.
    /// </summary>
    public static class OtlpMetricsParser
    {
        private static readonly JsonParser JsonMessageParser = new(JsonParser.Settings.Default.WithIgnoreUnknownFields(true));

        /// <summary>
        /// protobuf 본문을 해석한다.
        /// </summary>
        public static ExportMetricsServiceRequest ParseProtobuf(byte[] body)
        {
            try
            {
                return ExportMetricsServiceRequest.Parser.ParseFrom(body);
            }
            catch (InvalidProtocolBufferException ex)
            {
                throw new OtlpPayloadException("Invalid protobuf payload.", ex);
            }
        }

        /// <summary>
        /// JSON 본문을 해석한다. OTLP JSON은 exemplar의 trace/span id를 hex로 쓰지만 proto3 JSON 매핑은 base64를 기대하므로,
        /// 쓰지 않는 <c>exemplars</c>를 지운 뒤 읽는다.
        /// </summary>
        public static ExportMetricsServiceRequest ParseJson(string body)
        {
            try
            {
                JsonNode? root = JsonNode.Parse(body);
                if (root is not JsonObject)
                    throw new OtlpPayloadException("JSON payload must be an object.");
                RemoveExemplars(root);
                return JsonMessageParser.Parse<ExportMetricsServiceRequest>(root.ToJsonString());
            }
            catch (OtlpPayloadException)
            {
                throw;
            }
            catch (Exception ex) when (ex is JsonException or InvalidJsonException or InvalidProtocolBufferException or FormatException)
            {
                throw new OtlpPayloadException("Invalid JSON payload.", ex);
            }
        }

        private static void RemoveExemplars(JsonNode? node)
        {
            switch (node)
            {
                case JsonObject obj:
                    obj.Remove("exemplars");
                    foreach (KeyValuePair<string, JsonNode?> property in obj)
                        RemoveExemplars(property.Value);
                    break;
                case JsonArray array:
                    foreach (JsonNode? item in array)
                        RemoveExemplars(item);
                    break;
            }
        }

        /// <summary>
        /// 요청에서 대상 지표 두 개의 델타 Sum 포인트를 꺼낸다. 다른 지표·다른 데이터 형식은 건너뛰고,
        /// 대상 지표의 누적 포인트는 저장하지 않고 개수만 센다. 끝 시각이 없는(0) 포인트는 건너뛴다.
        /// </summary>
        public static OtlpMetricsBatch Extract(ExportMetricsServiceRequest request)
        {
            List<TokenUsagePoint> tokens = [];
            List<CostUsagePoint> costs = [];
            int cumulative = 0;

            foreach (ResourceMetrics resourceMetrics in request.ResourceMetrics)
            {
                Dictionary<string, string> resourceAttributes = ToDictionary(resourceMetrics.Resource?.Attributes);
                foreach (ScopeMetrics scopeMetrics in resourceMetrics.ScopeMetrics)
                {
                    foreach (Metric metric in scopeMetrics.Metrics)
                    {
                        bool isToken = metric.Name == UsageAttributes.TokenUsageMetric;
                        bool isCost = metric.Name == UsageAttributes.CostUsageMetric;
                        if ((!isToken && !isCost) || metric.DataCase != Metric.DataOneofCase.Sum)
                            continue;

                        if (metric.Sum.AggregationTemporality != AggregationTemporality.Delta)
                        {
                            cumulative += metric.Sum.DataPoints.Count;
                            continue;
                        }

                        foreach (NumberDataPoint point in metric.Sum.DataPoints)
                        {
                            if (point.TimeUnixNano == 0 || point.ValueCase == NumberDataPoint.ValueOneofCase.None)
                                continue;

                            DateTimeOffset time = FromUnixNano(point.TimeUnixNano);
                            Dictionary<string, string> pointAttributes = ToDictionary(point.Attributes);
                            string user = UsageAttributes.ResolveUser(pointAttributes, resourceAttributes);
                            string model = UsageAttributes.ResolveModel(pointAttributes, resourceAttributes);

                            if (isToken)
                            {
                                long value = point.ValueCase == NumberDataPoint.ValueOneofCase.AsInt
                                    ? point.AsInt
                                    : (long)Math.Round(point.AsDouble, MidpointRounding.AwayFromZero);
                                string tokenType = UsageAttributes.ResolveTokenType(pointAttributes, resourceAttributes);
                                tokens.Add(new TokenUsagePoint(time, user, model, tokenType, value));
                            }
                            else
                            {
                                double value = point.ValueCase == NumberDataPoint.ValueOneofCase.AsInt ? point.AsInt : point.AsDouble;
                                costs.Add(new CostUsagePoint(time, user, model, value));
                            }
                        }
                    }
                }
            }

            return new OtlpMetricsBatch(tokens, costs, cumulative);
        }

        internal static DateTimeOffset FromUnixNano(ulong unixNano)
            => DateTimeOffset.UnixEpoch.AddTicks((long)(unixNano / 100));

        private static Dictionary<string, string> ToDictionary(IEnumerable<KeyValue>? attributes)
        {
            Dictionary<string, string> result = new(StringComparer.Ordinal);
            if (attributes is null)
                return result;
            foreach (KeyValue attribute in attributes)
            {
                string? value = ToText(attribute.Value);
                if (value is not null)
                    result.TryAdd(attribute.Key, value);
            }
            return result;
        }

        private static string? ToText(AnyValue? value) => value?.ValueCase switch
        {
            AnyValue.ValueOneofCase.StringValue => value.StringValue,
            AnyValue.ValueOneofCase.IntValue => value.IntValue.ToString(CultureInfo.InvariantCulture),
            AnyValue.ValueOneofCase.DoubleValue => value.DoubleValue.ToString(CultureInfo.InvariantCulture),
            AnyValue.ValueOneofCase.BoolValue => value.BoolValue ? "true" : "false",
            _ => null,
        };
    }
}
