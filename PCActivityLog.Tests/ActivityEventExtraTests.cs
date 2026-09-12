using System.Text.Json.Nodes;
using PCActivityLog.Models;
using Xunit;

namespace PCActivityLog.Tests;

/// <summary>Extra JSON 辅助方法：事件关联 id 的读写就靠它（曾有 GetValue 的坑，值得守住）。</summary>
public class ActivityEventExtraTests
{
    [Fact]
    public void SetExtra_MergesExistingKeys()
    {
        var e = new ActivityEvent();
        e.SetExtra("linkedDownloadId", JsonValue.Create(11));
        e.SetExtra("linkedInstallId", JsonValue.Create(22));

        Assert.Equal(11, e.GetExtraLong("linkedDownloadId"));
        Assert.Equal(22, e.GetExtraLong("linkedInstallId"));
    }

    [Fact]
    public void GetExtraLong_ReadsNumberAndNumericString()
    {
        var e = new ActivityEvent { Extra = """{"a": 123, "b": "456"}""" };
        Assert.Equal(123, e.GetExtraLong("a"));
        Assert.Equal(456, e.GetExtraLong("b")); // 兼容旧数据里的字符串形式
        Assert.Null(e.GetExtraLong("missing"));
    }

    [Fact]
    public void GetExtraString_ReadsStringAndNonStringNodes()
    {
        var e = new ActivityEvent { Extra = """{"s": "hello", "n": 1.5, "flag": true}""" };
        Assert.Equal("hello", e.GetExtraString("s"));
        Assert.Equal("1.5", e.GetExtraString("n"));
        Assert.Equal("true", e.GetExtraString("flag"));
    }

    [Fact]
    public void Extra_Handlers_TolerateNullOrInvalidJson()
    {
        var e = new ActivityEvent { Extra = null };
        Assert.Null(e.GetExtraString("k"));
        Assert.Null(e.GetExtraLong("k"));

        e.Extra = "{ broken";
        Assert.Null(e.GetExtraString("k"));
        Assert.Null(e.GetExtraLong("k"));
    }
}
