using System.Text.Json;
using CFCAgent.Backend;

namespace CFCAgent.Backend.Tests;

public class ElicitationApprovalTests
{
    private static IReadOnlyDictionary<string, JsonElement> Answer(object value)
    {
        return new Dictionary<string, JsonElement> { ["confirm"] = JsonSerializer.SerializeToElement(value) };
    }

    [Fact]
    public void AcceptWithTrue_IsApproved()
    {
        Assert.True(ElicitationApproval.IsApproved("accept", Answer(true)));
    }

    [Fact]
    public void AcceptWithFalse_IsNotApproved()
    {
        Assert.False(ElicitationApproval.IsApproved("accept", Answer(false)));
    }

    [Fact]
    public void AcceptWithNoContent_IsNotApproved()
    {
        Assert.False(ElicitationApproval.IsApproved("accept", null));
        Assert.False(ElicitationApproval.IsApproved("accept", new Dictionary<string, JsonElement>()));
    }

    [Theory]
    [InlineData("true")]
    [InlineData("yes")]
    public void AcceptWithTruthyString_IsNotApproved(string text)
    {
        Assert.False(ElicitationApproval.IsApproved("accept", Answer(text)));
    }

    [Fact]
    public void AcceptWithNumberOne_IsNotApproved()
    {
        Assert.False(ElicitationApproval.IsApproved("accept", Answer(1)));
    }

    [Fact]
    public void AcceptWhereOneAnswerIsNotTrue_IsNotApproved()
    {
        Dictionary<string, JsonElement> content = new Dictionary<string, JsonElement>
        {
            ["a"] = JsonSerializer.SerializeToElement(true),
            ["b"] = JsonSerializer.SerializeToElement(false),
        };
        Assert.False(ElicitationApproval.IsApproved("accept", content));
    }

    [Theory]
    [InlineData("decline")]
    [InlineData("cancel")]
    [InlineData("ACCEPT")]
    [InlineData("")]
    [InlineData(null)]
    public void AnyActionOtherThanAccept_IsNotApproved(string? action)
    {
        Assert.False(ElicitationApproval.IsApproved(action, Answer(true)));
    }
}
