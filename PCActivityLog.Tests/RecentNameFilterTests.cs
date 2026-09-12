using PCActivityLog.Watchers;
using Xunit;

namespace PCActivityLog.Tests;

public class RecentNameFilterTests
{
    [Fact]
    public void ContainsRecent_IsTrueWithinWindow()
    {
        var f = new RecentNameFilter();
        f.Add("Firefox");
        Assert.True(f.ContainsRecent("Firefox", TimeSpan.FromMinutes(10)));
    }

    [Fact]
    public void ContainsRecent_NormalizesCaseAndWhitespace()
    {
        // 标准化约定：去首尾空白、压缩内部空白、忽略大小写
        var f = new RecentNameFilter();
        f.Add("  Firefox   Setup ");
        Assert.True(f.ContainsRecent("firefox setup", TimeSpan.FromMinutes(10)));
        Assert.True(f.ContainsRecent("FIREFOX  SETUP", TimeSpan.FromMinutes(10)));
    }

    [Fact]
    public void ContainsRecent_IsFalseOutsideWindow()
    {
        var f = new RecentNameFilter();
        f.Add("Firefox");
        Thread.Sleep(150);
        Assert.False(f.ContainsRecent("Firefox", TimeSpan.FromMilliseconds(50)));
        Assert.True(f.ContainsRecent("Firefox", TimeSpan.FromMinutes(1)));
    }

    [Fact]
    public void ContainsRecent_IsFalseForUnknownName()
    {
        var f = new RecentNameFilter();
        f.Add("Firefox");
        Assert.False(f.ContainsRecent("Chrome", TimeSpan.FromMinutes(10)));
    }

    [Fact]
    public void Clear_RemovesAllEntries()
    {
        var f = new RecentNameFilter();
        f.Add("Firefox");
        f.Clear();
        Assert.False(f.ContainsRecent("Firefox", TimeSpan.FromMinutes(10)));
    }
}
