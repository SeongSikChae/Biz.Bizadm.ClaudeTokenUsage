using Google.Protobuf;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;

namespace Biz.Bizadm.ClaudeTokenUsage.Server.Spa.Tests;

[TestClass]
public sealed class UsageApiTests
{
    private static readonly DateTime Now = new(2026, 11, 20, 14, 37, 12);

    private static async Task<TestApp> SeededAsync()
    {
        TestApp app = await TestApp.StartAsync(Now);
        await PostAsync(app, new DateTime(2026, 11, 20, 10, 5, 0), 100, "input", 0.25);
        await PostAsync(app, new DateTime(2026, 11, 20, 10, 6, 0), 30, "output");
        await PostAsync(app, new DateTime(2026, 11, 20, 12, 0, 0), 7, "input", 0.5);
        await app.Store.RunMaintenanceAsync(); // 1분 주기 유지 작업 한 번
        return app;
    }

    private static async Task PostAsync(TestApp app, DateTime kst, long tokens, string type, double? cost = null)
    {
        ByteArrayContent content = new(TestApp.Request(kst, tokens, type, cost).ToByteArray());
        content.Headers.ContentType = new MediaTypeHeaderValue("application/x-protobuf");
        HttpResponseMessage response = await app.Client.PostAsync("/v1/metrics", content);
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
    }

    private static async Task<JsonElement> GetJsonAsync(TestApp app, string url)
    {
        HttpResponseMessage response = await app.Client.GetAsync(url);
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode, await response.Content.ReadAsStringAsync());
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
    }

    private static string[] Starts(JsonElement json)
        => [.. json.GetProperty("buckets").EnumerateArray().Select(b => b.GetProperty("start").GetString()!)];

    [TestMethod]
    public async Task Series_RoundsFromDownAndToUp()
    {
        await using TestApp app = await SeededAsync();

        JsonElement json = await GetJsonAsync(app, "/api/usage/series?unit=hour&from=2026-11-20T10:30:00&to=2026-11-20T12:00:01&groupBy=tokenType");

        Assert.AreEqual("2026-11-20T10:00:00+09:00", json.GetProperty("from").GetString());
        Assert.AreEqual("2026-11-20T13:00:00+09:00", json.GetProperty("to").GetString());
        CollectionAssert.AreEqual(new[] { "2026-11-20T10:00:00+09:00", "2026-11-20T11:00:00+09:00", "2026-11-20T12:00:00+09:00" }, Starts(json));
    }

    [TestMethod]
    public async Task Series_AlignedToIsNotRoundedUp()
    {
        await using TestApp app = await SeededAsync();

        JsonElement json = await GetJsonAsync(app, "/api/usage/series?unit=hour&from=2026-11-20T10:00:00&to=2026-11-20T12:00:00&groupBy=tokenType");

        Assert.AreEqual("2026-11-20T12:00:00+09:00", json.GetProperty("to").GetString());
        CollectionAssert.AreEqual(new[] { "2026-11-20T10:00:00+09:00", "2026-11-20T11:00:00+09:00" }, Starts(json));
    }

    [TestMethod]
    public async Task Series_FillsZero_AndReturnsTotalsAndCost()
    {
        await using TestApp app = await SeededAsync();

        JsonElement json = await GetJsonAsync(app, "/api/usage/series?unit=hour&from=2026-11-20T09:00:00&to=2026-11-20T13:00:00&groupBy=tokenType");

        long[] totals = [.. json.GetProperty("buckets").EnumerateArray().Select(b => b.GetProperty("totalTokens").GetInt64())];
        CollectionAssert.AreEqual(new long[] { 0, 130, 0, 7 }, totals);
        JsonElement tenOClock = json.GetProperty("buckets")[1];
        Assert.AreEqual(100, tenOClock.GetProperty("tokens").GetProperty("input").GetInt64());
        Assert.AreEqual(0.25, tenOClock.GetProperty("costUsd").GetDouble(), 1e-9);
        Assert.AreEqual(137, json.GetProperty("totals").GetProperty("tokens").GetInt64());
        Assert.AreEqual(0.75, json.GetProperty("totals").GetProperty("costUsd").GetDouble(), 1e-9);
        Assert.AreEqual("tokenType", json.GetProperty("groupBy").GetString());
    }

    [TestMethod]
    public async Task Series_ExcludesFutureAndOutOfRetentionBuckets()
    {
        await using TestApp app = await SeededAsync();

        JsonElement future = await GetJsonAsync(app, "/api/usage/series?unit=hour&from=2026-11-20T13:00:00&to=2026-11-22T00:00:00&groupBy=model");
        CollectionAssert.AreEqual(new[] { "2026-11-20T13:00:00+09:00", "2026-11-20T14:00:00+09:00" }, Starts(future));

        JsonElement clipped = await GetJsonAsync(app, "/api/usage/series?unit=minute&from=2026-11-18T00:00:00&to=2026-11-19T14:39:00&groupBy=model");
        CollectionAssert.AreEqual(new[] { "2026-11-19T14:37:00+09:00", "2026-11-19T14:38:00+09:00" }, Starts(clipped));
    }

    [TestMethod]
    public async Task Series_EntirelyOutsideRetention_Returns200WithEmptyBuckets()
    {
        await using TestApp app = await SeededAsync();

        JsonElement json = await GetJsonAsync(app, "/api/usage/series?unit=minute&from=2026-11-01T00:00:00&to=2026-11-02T00:00:00&groupBy=tokenType");

        Assert.AreEqual(0, json.GetProperty("buckets").GetArrayLength());
        Assert.AreEqual(0, json.GetProperty("totals").GetProperty("tokens").GetInt64());
    }

    [TestMethod]
    public async Task Series_OffsetIsConvertedToKst()
    {
        await using TestApp app = await SeededAsync();

        // 01:00Z = 10:00 KST, 03:00Z = 12:00 KST
        JsonElement json = await GetJsonAsync(app, "/api/usage/series?unit=hour&from=2026-11-20T01:00:00Z&to=2026-11-20T03:00:00%2B00:00&groupBy=tokenType");

        CollectionAssert.AreEqual(new[] { "2026-11-20T10:00:00+09:00", "2026-11-20T11:00:00+09:00" }, Starts(json));
        Assert.AreEqual(130, json.GetProperty("totals").GetProperty("tokens").GetInt64());
    }

    [TestMethod]
    [DataRow("unit=days&from=2026-11-20T00:00:00&to=2026-11-21T00:00:00&groupBy=tokenType")]
    [DataRow("unit=day&from=2026-11-20T00:00:00&to=2026-11-21T00:00:00&groupBy=session")]
    [DataRow("unit=day&from=yesterday&to=2026-11-21T00:00:00&groupBy=tokenType")]
    [DataRow("unit=day&from=2026-11-20T00:00:00&groupBy=tokenType")]
    [DataRow("unit=day&from=2026-11-21T00:00:00&to=2026-11-21T00:00:00&groupBy=tokenType")]
    [DataRow("unit=day&from=2026-11-22T00:00:00&to=2026-11-21T00:00:00&groupBy=tokenType")]
    public async Task Series_InvalidInput_Returns400(string query)
    {
        await using TestApp app = await TestApp.StartAsync(Now);

        HttpResponseMessage response = await app.Client.GetAsync("/api/usage/series?" + query);

        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.AreEqual("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [TestMethod]
    public async Task Series_TokenTypeFilter_ReturnsNullCost()
    {
        await using TestApp app = await SeededAsync();

        JsonElement json = await GetJsonAsync(app, "/api/usage/series?unit=day&from=2026-11-20T00:00:00&to=2026-11-21T00:00:00&groupBy=model&tokenType=input");

        Assert.AreEqual(107, json.GetProperty("totals").GetProperty("tokens").GetInt64());
        Assert.AreEqual(JsonValueKind.Null, json.GetProperty("totals").GetProperty("costUsd").ValueKind);
        Assert.AreEqual(JsonValueKind.Null, json.GetProperty("buckets")[0].GetProperty("costUsd").ValueKind);
    }

    [TestMethod]
    public async Task Series_GroupByTokenType_ReturnsSingleCostTotalPerBucket()
    {
        await using TestApp app = await SeededAsync();

        JsonElement json = await GetJsonAsync(app, "/api/usage/series?unit=day&from=2026-11-20T00:00:00&to=2026-11-21T00:00:00&groupBy=tokenType");

        JsonElement bucket = json.GetProperty("buckets")[0];
        Assert.AreEqual(0.75, bucket.GetProperty("costUsd").GetDouble(), 1e-9);
        Assert.AreEqual(107, bucket.GetProperty("tokens").GetProperty("input").GetInt64());
        Assert.AreEqual(30, bucket.GetProperty("tokens").GetProperty("output").GetInt64());
    }

    [TestMethod]
    public async Task Series_AfterOneMaintenance_AllUnitsReflectReceivedValues()
    {
        await using TestApp app = await SeededAsync();

        foreach (string unit in new[] { "minute", "hour", "day", "week", "month", "year" })
        {
            string from = unit == "minute" ? "2026-11-20T10:00:00" : "2026-01-01T00:00:00";
            JsonElement json = await GetJsonAsync(app, $"/api/usage/series?unit={unit}&from={from}&to=2026-12-01T00:00:00&groupBy=tokenType");
            Assert.AreEqual(137, json.GetProperty("totals").GetProperty("tokens").GetInt64(), unit);
        }
    }

    [TestMethod]
    public async Task Meta_ReturnsEarliestAndFilterValues()
    {
        await using TestApp app = await SeededAsync();

        JsonElement json = await GetJsonAsync(app, "/api/usage/meta");

        Assert.AreEqual("2026-11-20T14:37:12+09:00", json.GetProperty("now").GetString());
        JsonElement[] units = [.. json.GetProperty("units").EnumerateArray()];
        Assert.AreEqual("2025-11-17T00:00:00+09:00", units.Single(u => u.GetProperty("unit").GetString() == "week").GetProperty("earliest").GetString());
        Assert.AreEqual(JsonValueKind.Null, units.Single(u => u.GetProperty("unit").GetString() == "year").GetProperty("earliest").ValueKind);
        Assert.AreEqual("a@example.com", json.GetProperty("users")[0].GetString());
        Assert.AreEqual(2, json.GetProperty("tokenTypes").GetArrayLength());
    }
}
