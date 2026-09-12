using PCActivityLog.Models;
using Xunit;

namespace PCActivityLog.Tests;

public class EventTypeTests
{
    public static IEnumerable<object[]> AllEventTypes() =>
        Enum.GetValues<EventType>().Select(t => new object[] { t });

    [Theory]
    [MemberData(nameof(AllEventTypes))]
    public void DbString_RoundTrips_AllTypes(EventType t)
    {
        Assert.Equal(t, EventTypeExtensions.FromDbString(t.ToDbString()));
    }

    [Fact]
    public void ToDbString_UsesExpectedSnakeCase()
    {
        Assert.Equal("download", EventType.Download.ToDbString());
        Assert.Equal("file_delete", EventType.FileDelete.ToDbString());
        Assert.Equal("im_file", EventType.ImFile.ToDbString());
        Assert.Equal("browse", EventType.Browse.ToDbString());
    }

    [Fact]
    public void FromDbString_Unknown_FallsBackToManual()
    {
        // 容错约定：脏数据/未来新增类型未知时不崩溃
        Assert.Equal(EventType.Manual, EventTypeExtensions.FromDbString("nonexistent"));
        Assert.Equal(EventType.Manual, EventTypeExtensions.FromDbString(null));
    }

    [Fact]
    public void ToGroup_MapsByCategory()
    {
        Assert.Equal(EventGroup.Download, EventType.Download.ToGroup());
        Assert.Equal(EventGroup.FileOps, EventType.FileRename.ToGroup());
        Assert.Equal(EventGroup.App, EventType.Uninstall.ToGroup());
        Assert.Equal(EventGroup.System, EventType.Wake.ToGroup());
        Assert.Equal(EventGroup.Manual, EventType.Manual.ToGroup());
    }

    [Fact]
    public void ToDbTypeList_StringsAreAllRecognizable()
    {
        // 每个大类的类型串都必须能被 FromDbString 识别（防手写错别字；
        // manual 本身就映射回 Manual，属正常）
        foreach (var g in new[] { EventGroup.Download, EventGroup.FileOps, EventGroup.ImFile,
                                  EventGroup.App, EventGroup.System, EventGroup.Manual })
        {
            foreach (var t in EventGroupExtensions.ToDbTypeList(g))
            {
                var parsed = EventTypeExtensions.FromDbString(t);
                Assert.True(parsed != EventType.Manual || t == "manual",
                    $"类型串『{t}』（大类 {g}）无法被 FromDbString 识别");
            }
        }
    }

    [Fact]
    public void ToDisplayName_AllTypesHaveChineseName()
    {
        foreach (var t in Enum.GetValues<EventType>())
            Assert.NotEqual(t.ToString(), t.ToDisplayName());
    }
}
