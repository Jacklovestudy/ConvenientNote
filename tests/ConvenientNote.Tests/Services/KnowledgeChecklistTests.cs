using ConvenientNote.Services;
using Xunit;

namespace ConvenientNote.Tests.Services;

public sealed class KnowledgeChecklistTests
{
    [Fact]
    public void SuppliedChecklistKeepsAllGroupsItemsAndOriginalChecks()
    {
        var rows = KnowledgeChecklist.Parse(KnowledgeChecklist.DefaultText);
        Assert.Equal(19, rows.Count(r => r.IsHeading));
        Assert.Equal(149, rows.Count(r => r.HasCheck));
        Assert.Equal(35, rows.Count(r => r.IsChecked));
        Assert.Contains("优先复习清单", KnowledgeChecklist.DefaultText);
        Assert.Contains("还没有系统考核", KnowledgeChecklist.DefaultText);
    }

    [Fact]
    public void CheckingUpdatesOnlyThatLineAndItsGroupCount()
    {
        const string text = "### 一、类型　已掌握 0/2\r\n\r\n1. `ref`　☐\r\n2. 复制　☐\r\n### 二、泛型　已掌握 1/1\r\n1. 类型安全　☑\r\n普通文字";
        var changed = KnowledgeChecklist.Toggle(text, 2, true);
        Assert.Equal("### 一、类型　已掌握 1/2\r\n\r\n1. `ref`　☑\r\n2. 复制　☐\r\n### 二、泛型　已掌握 1/1\r\n1. 类型安全　☑\r\n普通文字", changed);
        Assert.Equal(text, KnowledgeChecklist.Toggle(changed, 2, false));
        Assert.Empty(KnowledgeChecklist.Parse(""));
    }
}
