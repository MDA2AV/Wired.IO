using Xunit;
using Wired.IO.Utilities;

public class PooledDictionaryTests
{
    [Fact]
    public void AddAndContainsKey_Works()
    {
        var dict = new PooledDictionary<string, int>(4, EqualityComparer<string>.Default);
        dict.Add("a", 1);
        Assert.True(dict.ContainsKey("a"));
        Assert.Equal(1, dict["a"]);
    }
}