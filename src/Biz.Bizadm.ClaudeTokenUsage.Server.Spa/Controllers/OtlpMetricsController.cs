using Google.Protobuf;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Net.Http.Headers;
using OpenTelemetry.Proto.Collector.Metrics.V1;
using System.IO.Compression;
using System.Text;
using Biz.Bizadm.ClaudeTokenUsage.Infrastructure.Otlp;
using Biz.Bizadm.ClaudeTokenUsage.Infrastructure.Storage;

namespace Biz.Bizadm.ClaudeTokenUsage.Server.Spa.Controllers
{
    /// <summary>
    /// OTLP/HTTP 지표 받기. protobuf·JSON 본문(선택적으로 gzip)을 받아 Claude Code 토큰·비용 델타 포인트를 저장한다.
    /// </summary>
    [ApiController]
    [Route("v1/metrics")]
    public sealed class OtlpMetricsController : ControllerBase
    {
        private const string ProtobufMediaType = "application/x-protobuf";
        private const string JsonMediaType = "application/json";

        private readonly DuckDbUsageStore store;
        private readonly ILogger<OtlpMetricsController> logger;

        public OtlpMetricsController(DuckDbUsageStore store, ILogger<OtlpMetricsController> logger)
        {
            this.store = store;
            this.logger = logger;
        }

        [HttpPost]
        public async Task<IActionResult> PostAsync(CancellationToken cancellationToken)
        {
            string? mediaType = MediaTypeHeaderValue.TryParse(Request.ContentType, out MediaTypeHeaderValue? contentType)
                ? contentType.MediaType.Value
                : null;
            bool isProtobuf = string.Equals(mediaType, ProtobufMediaType, StringComparison.OrdinalIgnoreCase);
            bool isJson = string.Equals(mediaType, JsonMediaType, StringComparison.OrdinalIgnoreCase);
            if (!isProtobuf && !isJson)
                return Problem(statusCode: StatusCodes.Status415UnsupportedMediaType,
                    detail: $"Content-Type must be {ProtobufMediaType} or {JsonMediaType}.");

            ExportMetricsServiceRequest request;
            try
            {
                byte[] body = await ReadBodyAsync(cancellationToken);
                request = isProtobuf
                    ? OtlpMetricsParser.ParseProtobuf(body)
                    : OtlpMetricsParser.ParseJson(Encoding.UTF8.GetString(body));
            }
            catch (Exception ex) when (ex is OtlpPayloadException or InvalidDataException)
            {
                return Problem(statusCode: StatusCodes.Status400BadRequest, detail: ex.Message);
            }

            OtlpMetricsBatch batch = OtlpMetricsParser.Extract(request);
            if (batch.CumulativePointCount > 0)
            {
                logger.LogWarning(
                    "Ignored {Count} cumulative data points. Set OTEL_EXPORTER_OTLP_METRICS_TEMPORALITY_PREFERENCE to delta (Claude Code default).",
                    batch.CumulativePointCount);
            }

            await store.IngestAsync(batch.Tokens, batch.Costs, cancellationToken);

            return isProtobuf
                ? File(new ExportMetricsServiceResponse().ToByteArray(), ProtobufMediaType)
                : Content("{}", JsonMediaType);
        }

        private async Task<byte[]> ReadBodyAsync(CancellationToken cancellationToken)
        {
            bool gzip = Request.Headers.ContentEncoding.Any(value => string.Equals(value?.Trim(), "gzip", StringComparison.OrdinalIgnoreCase));
            using MemoryStream buffer = new();
            if (gzip)
            {
                await using GZipStream decompressed = new(Request.Body, CompressionMode.Decompress, leaveOpen: true);
                await decompressed.CopyToAsync(buffer, cancellationToken);
            }
            else
            {
                await Request.Body.CopyToAsync(buffer, cancellationToken);
            }
            return buffer.ToArray();
        }
    }
}
