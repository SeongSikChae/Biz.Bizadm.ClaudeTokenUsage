namespace Biz.Bizadm.ClaudeTokenUsage.Core.Tests;

[TestClass]
public sealed class UsageAttributesTests
{
    private static readonly Dictionary<string, string> Empty = [];

    [TestMethod]
    public void ResolveUser_PrefersEmailThenAccountThenId()
    {
        Dictionary<string, string> point = new() { ["user.id"] = "id-1", ["user.account_uuid"] = "acc-1" };
        Dictionary<string, string> resource = new() { ["user.email"] = "a@example.com" };

        Assert.AreEqual("a@example.com", UsageAttributes.ResolveUser(point, resource));
        Assert.AreEqual("acc-1", UsageAttributes.ResolveUser(point, Empty));
        Assert.AreEqual("id-1", UsageAttributes.ResolveUser(new Dictionary<string, string> { ["user.id"] = "id-1" }, Empty));
    }

    [TestMethod]
    public void ResolveUser_PointAttributeWinsOverResourceForSameKey()
    {
        Dictionary<string, string> point = new() { ["user.email"] = "point@example.com" };
        Dictionary<string, string> resource = new() { ["user.email"] = "resource@example.com" };
        Assert.AreEqual("point@example.com", UsageAttributes.ResolveUser(point, resource));
    }

    [TestMethod]
    public void Resolve_MissingOrEmpty_ReturnsUnknown()
    {
        Assert.AreEqual("unknown", UsageAttributes.ResolveUser(Empty, Empty));
        Assert.AreEqual("unknown", UsageAttributes.ResolveModel(new Dictionary<string, string> { ["model"] = "" }, Empty));
        Assert.AreEqual("unknown", UsageAttributes.ResolveTokenType(Empty, Empty));
    }

    [TestMethod]
    public void ResolveModelAndType_FallBackToResource()
    {
        Dictionary<string, string> resource = new() { ["model"] = "claude-opus-5-5", ["type"] = "input" };
        Assert.AreEqual("claude-opus-5-5", UsageAttributes.ResolveModel(Empty, resource));
        Assert.AreEqual("input", UsageAttributes.ResolveTokenType(Empty, resource));
    }
}
