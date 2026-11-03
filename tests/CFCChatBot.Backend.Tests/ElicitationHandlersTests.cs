using CFCChatBot.Backend;
using ModelContextProtocol.Protocol;

namespace CFCChatBot.Backend.Tests;

public class ElicitationHandlersTests
{
    private static readonly ElicitRequestParams Request = new ElicitRequestParams { Message = "Apply change?" };

    [Fact]
    public async Task ConfirmedCallback_ProducesResultThatPassesStrictApproval()
    {
        Func<ElicitRequestParams?, CancellationToken, ValueTask<ElicitResult>> handler = ElicitationHandlers.Create((message, ct) => Task.FromResult(true));

        ElicitResult result = await handler(Request, CancellationToken.None);

        Assert.True(ElicitationApproval.IsApproved(result.Action, result.Content));
    }

    [Fact]
    public async Task RejectedCallback_IsDeclinedAndNotApproved()
    {
        Func<ElicitRequestParams?, CancellationToken, ValueTask<ElicitResult>> handler = ElicitationHandlers.Create((message, ct) => Task.FromResult(false));

        ElicitResult result = await handler(Request, CancellationToken.None);

        Assert.Equal("decline", result.Action);
        Assert.False(ElicitationApproval.IsApproved(result.Action, result.Content));
    }

    [Fact]
    public async Task CancelledCallback_IsCancelledAndNotApproved()
    {
        Func<ElicitRequestParams?, CancellationToken, ValueTask<ElicitResult>> handler = ElicitationHandlers.Create((message, ct) => throw new OperationCanceledException());

        ElicitResult result = await handler(Request, CancellationToken.None);

        Assert.Equal("cancel", result.Action);
        Assert.False(ElicitationApproval.IsApproved(result.Action, result.Content));
    }
}
