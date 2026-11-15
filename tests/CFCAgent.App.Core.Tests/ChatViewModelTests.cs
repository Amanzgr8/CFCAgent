using CFCAgent.App.Core;
using CFCAgent.Backend;
using Microsoft.Extensions.AI;

namespace CFCAgent.App.Core.Tests;

public class ChatViewModelTests
{
    private sealed class FailingChatClient : IChatClient
    {
        public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
        {
            throw new InvalidOperationException("the model is unreachable");
        }

        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
        {
            throw new InvalidOperationException("the model is unreachable");
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose()
        {
        }
    }

    private static ChatViewModel NewViewModel(TimeSpan streamDelay)
    {
        return ChatViewModel.CreateDemo(streamDelay);
    }

    [Fact]
    public async Task Send_ShowsTheUserMessageThenTheCompleteStreamedReply()
    {
        ChatViewModel viewModel = NewViewModel(TimeSpan.Zero);
        viewModel.Input = "hello there";

        await viewModel.SendCommand.ExecuteAsync(null);

        Assert.Equal(2, viewModel.Messages.Count);
        Assert.Equal(ChatAuthor.User, viewModel.Messages[0].Author);
        Assert.Equal("hello there", viewModel.Messages[0].Text);
        Assert.Equal(ChatAuthor.Assistant, viewModel.Messages[1].Author);
        Assert.Equal("[stub model, no ANTHROPIC_API_KEY] You said: hello there", viewModel.Messages[1].Text);
        Assert.False(viewModel.Messages[1].IsStreaming);
    }

    [Fact]
    public async Task Send_ClearsTheInputAndIsNotBusyAfterwards()
    {
        ChatViewModel viewModel = NewViewModel(TimeSpan.Zero);
        viewModel.Input = "  question  ";

        await viewModel.SendCommand.ExecuteAsync(null);

        Assert.Equal("", viewModel.Input);
        Assert.False(viewModel.IsBusy);
        Assert.Equal("question", viewModel.Messages[0].Text);
    }

    [Fact]
    public void CanSend_IsFalseForEmptyOrBlankInput()
    {
        ChatViewModel viewModel = NewViewModel(TimeSpan.Zero);

        Assert.False(viewModel.SendCommand.CanExecute(null));

        viewModel.Input = "   ";
        Assert.False(viewModel.SendCommand.CanExecute(null));

        viewModel.Input = "a question";
        Assert.True(viewModel.SendCommand.CanExecute(null));
    }

    [Fact]
    public async Task WhileAReplyIsArriving_TheViewModelIsBusyAndTheReplyIsMarkedStreaming()
    {
        ChatViewModel viewModel = NewViewModel(TimeSpan.FromMilliseconds(20));
        viewModel.Input = "slow one";

        Task send = viewModel.SendCommand.ExecuteAsync(null);
        await WaitUntilAsync(() => viewModel.Messages.Count == 2 && viewModel.Messages[1].Text.Length > 0);

        Assert.True(viewModel.IsBusy);
        Assert.True(viewModel.Messages[1].IsStreaming);
        Assert.False(viewModel.SendCommand.CanExecute(null));

        await send;
        Assert.False(viewModel.IsBusy);
    }

    [Fact]
    public async Task Stop_CancelsTheReplyAndKeepsWhatHadArrived()
    {
        ChatViewModel viewModel = NewViewModel(TimeSpan.FromMilliseconds(20));
        viewModel.Input = "a longer question so there are plenty of words to wait for";

        Task send = viewModel.SendCommand.ExecuteAsync(null);
        await WaitUntilAsync(() => viewModel.Messages.Count == 2 && viewModel.Messages[1].Text.Length > 0);
        viewModel.SendCancelCommand.Execute(null);
        await send;

        Assert.EndsWith("(stopped)", viewModel.Messages[1].Text);
        Assert.False(viewModel.Messages[1].IsStreaming);
        Assert.False(viewModel.IsBusy);
    }

    [Fact]
    public async Task AFailure_BecomesANoticeInTheConversationAndTheAppKeepsWorking()
    {
        ChatSession session = new ChatSession(new FailingChatClient(), Array.Empty<AITool>(), null);
        ChatViewModel viewModel = new ChatViewModel(session, "test");
        viewModel.Input = "anything";

        await viewModel.SendCommand.ExecuteAsync(null);

        Assert.Equal(2, viewModel.Messages.Count);
        Assert.Equal(ChatAuthor.User, viewModel.Messages[0].Author);
        Assert.Equal(ChatAuthor.Notice, viewModel.Messages[1].Author);
        Assert.Contains("the model is unreachable", viewModel.Messages[1].Text);
        Assert.False(viewModel.IsBusy);

        viewModel.Input = "try again";
        Assert.True(viewModel.SendCommand.CanExecute(null));
    }

    [Fact]
    public async Task ASecondQuestion_AddsAnotherPairOfMessages()
    {
        ChatViewModel viewModel = NewViewModel(TimeSpan.Zero);

        viewModel.Input = "one";
        await viewModel.SendCommand.ExecuteAsync(null);
        viewModel.Input = "two";
        await viewModel.SendCommand.ExecuteAsync(null);

        Assert.Equal(4, viewModel.Messages.Count);
        Assert.Contains("two", viewModel.Messages[3].Text);
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        for (int attempt = 0; attempt < 200; attempt++)
        {
            if (condition())
            {
                return;
            }

            await Task.Delay(10);
        }

        Assert.Fail("The condition was not met in time.");
    }
}
