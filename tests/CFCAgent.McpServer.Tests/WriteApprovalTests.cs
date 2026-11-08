using System.Text.Json;
using CFCAgent.McpServer.Tools;

namespace CFCAgent.McpServer.Tests;

public class WriteApprovalTests
{
    private static JsonElement Json(string text)
    {
        return JsonDocument.Parse(text).RootElement.Clone();
    }

    private static Dictionary<string, JsonElement> Answer(string confirmJson)
    {
        return new Dictionary<string, JsonElement> { [WriteApproval.ConfirmField] = Json(confirmJson) };
    }

    [Fact]
    public void IsApproved_AcceptWithConfirmTrue_IsApproved()
    {
        Assert.True(WriteApproval.IsApproved("accept", Answer("true")));
    }

    [Theory]
    [InlineData("decline")]
    [InlineData("cancel")]
    [InlineData("Accept")]
    [InlineData("")]
    [InlineData(null)]
    public void IsApproved_AnyActionOtherThanAccept_IsNotApproved(string? action)
    {
        Assert.False(WriteApproval.IsApproved(action, Answer("true")));
    }

    [Theory]
    [InlineData("false")]
    [InlineData("\"true\"")]
    [InlineData("1")]
    [InlineData("null")]
    [InlineData("\"yes\"")]
    public void IsApproved_ConfirmThatIsNotAnExplicitTrue_IsNotApproved(string confirmJson)
    {
        Assert.False(WriteApproval.IsApproved("accept", Answer(confirmJson)));
    }

    [Fact]
    public void IsApproved_MissingOrEmptyContent_IsNotApproved()
    {
        Assert.False(WriteApproval.IsApproved("accept", null));
        Assert.False(WriteApproval.IsApproved("accept", new Dictionary<string, JsonElement>()));
    }

    [Fact]
    public void IsApproved_AnswerWithoutTheConfirmField_IsNotApproved()
    {
        Dictionary<string, JsonElement> other = new Dictionary<string, JsonElement> { ["something_else"] = Json("true") };

        Assert.False(WriteApproval.IsApproved("accept", other));
    }

    [Fact]
    public void IsApproved_ExtraAnswerThatIsNotTrue_IsNotApproved()
    {
        Dictionary<string, JsonElement> answer = Answer("true");
        answer["extra"] = Json("false");

        Assert.False(WriteApproval.IsApproved("accept", answer));
    }

    [Fact]
    public async Task RequestApprovalAsync_NoServer_ReturnsAReasonAndSaysNothingWasChanged()
    {
        string? reason = await WriteApproval.RequestApprovalAsync(null, "preview", CancellationToken.None);

        Assert.NotNull(reason);
        Assert.Contains("not applied", reason);
        Assert.Contains("Nothing was changed", reason);
    }
}
