using ConvenientNote.Views;
using Xunit;

namespace ConvenientNote.Tests.Views;

public sealed class DocumentWordCounterTests
{
    [Theory]
    [InlineData("", 0)]
    [InlineData("这是测试", 4)]
    [InlineData("hello world 123", 3)]
    [InlineData("GC 方法，test 2。", 5)]
    [InlineData("nai\u0308ve", 1)]
    [InlineData("カ\u3099・ナ", 2)]
    [InlineData("\U00030000\U00030001", 2)]
    [InlineData("\U0002F800\U0002F801", 2)]
    public void CountTreatsCjkCharactersAndLatinWordsLikeWord(string text, int expected)
    {
        Assert.Equal(expected, DocumentWordCounter.Count(text));
    }
}
