using CFCAgent.App.Core;
using CFCAgent.Backend;
using Microsoft.Extensions.AI;

namespace CFCAgent.App.Core.Tests;

// How the chat view-model behaves while connecting to a backend: success, failure, retry, cancel, shutdown.
// A fake connector stands in for the real one, so none of these need a database or the MCP server.
public class ConnectionTests
{
    private sealed class FakeConnector : IChatConnector
    {
        private readonly Queue<Func<ChatConnection>> _outcomes;

        public FakeConnector(params Func<ChatConnection>[] outcomes)
        {
            _outcomes = new Queue<Func<ChatConnection>>(outcomes);
        }

        public int Attempts { get; private set; }

        public Task<ChatConnection> ConnectAsync(CancellationToken cancellationToken)
        {
            Attempts++;
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(_outcomes.Dequeue()());
        }
    }

    private sealed class DisposeCounter
    {
        public int Count { get; private set; }

        public ValueTask Dispose()
        {
            Count++;
            return ValueTask.CompletedTask;
        }
    }

    private static ChatConnection Connected(DisposeCounter counter, string description = "tenant 'test', 5 tools, read-only")
    {
        ChatSession session = new ChatSession(new StubChatClient(), Array.Empty<AITool>(), null);
        return new ChatConnection(session, description, Array.Empty<AITool>(), new ServerLogBuffer(), counter.Dispose);
    }

    private static Func<ChatConnection> Fails(string message)
    {
        return () => throw new InvalidOperationException(message);
    }

    [Fact]
    public async Task ConnectAsync_Success_EnablesSendingAndShowsTheDescription()
    {
        ChatViewModel viewModel = new ChatViewModel();
        viewModel.Input = "hello";
        Assert.False(viewModel.SendCommand.CanExecute(null));

        await viewModel.ConnectAsync(new FakeConnector(() => Connected(new DisposeCounter())));

        Assert.True(viewModel.IsConnected);
        Assert.False(viewModel.IsConnecting);
        Assert.Equal("tenant 'test', 5 tools, read-only", viewModel.ConnectionStatus);
        Assert.True(viewModel.SendCommand.CanExecute(null));

        await viewModel.SendCommand.ExecuteAsync(null);
        Assert.Equal("[stub model, no ANTHROPIC_API_KEY] You said: hello", viewModel.Messages[1].Text);
    }

    [Fact]
    public async Task ConnectAsync_Failure_ShowsANoticeAndKeepsSendingOff()
    {
        ChatViewModel viewModel = new ChatViewModel();
        viewModel.Input = "hello";

        await viewModel.ConnectAsync(new FakeConnector(Fails("the database password was rejected")));

        Assert.False(viewModel.IsConnected);
        Assert.False(viewModel.IsConnecting);
        Assert.Equal("Not connected", viewModel.ConnectionStatus);
        Assert.False(viewModel.SendCommand.CanExecute(null));
        ChatMessageViewModel notice = Assert.Single(viewModel.Messages);
        Assert.Equal(ChatAuthor.Notice, notice.Author);
        Assert.Contains("the database password was rejected", notice.Text);
    }

    [Fact]
    public async Task Retry_AfterAFailure_ConnectsWithTheSameConnector()
    {
        DisposeCounter counter = new DisposeCounter();
        FakeConnector connector = new FakeConnector(Fails("not yet"), () => Connected(counter));
        ChatViewModel viewModel = new ChatViewModel();

        await viewModel.ConnectAsync(connector);
        Assert.True(viewModel.RetryConnectionCommand.CanExecute(null));

        await viewModel.RetryConnectionCommand.ExecuteAsync(null);

        Assert.Equal(2, connector.Attempts);
        Assert.True(viewModel.IsConnected);
        Assert.False(viewModel.RetryConnectionCommand.CanExecute(null), "no retry once connected");
    }

    [Fact]
    public void Retry_BeforeAnyAttempt_IsNotAvailable()
    {
        ChatViewModel viewModel = new ChatViewModel();

        Assert.False(viewModel.RetryConnectionCommand.CanExecute(null));
    }

    [Fact]
    public async Task ConnectAsync_Cancelled_SaysSoWithoutANotice()
    {
        ChatViewModel viewModel = new ChatViewModel();
        using CancellationTokenSource cancelled = new CancellationTokenSource();
        cancelled.Cancel();

        await viewModel.ConnectAsync(new FakeConnector(() => Connected(new DisposeCounter())), cancelled.Token);

        Assert.False(viewModel.IsConnected);
        Assert.Equal("Not connected (cancelled)", viewModel.ConnectionStatus);
        Assert.Empty(viewModel.Messages);
    }

    [Fact]
    public async Task DisposeAsync_ShutsTheConnectionDownOnce()
    {
        DisposeCounter counter = new DisposeCounter();
        ChatViewModel viewModel = new ChatViewModel();
        await viewModel.ConnectAsync(new FakeConnector(() => Connected(counter)));

        await viewModel.DisposeAsync();
        await viewModel.DisposeAsync();

        Assert.Equal(1, counter.Count);
    }

    [Fact]
    public async Task DisposeAsync_WithNothingConnected_DoesNothing()
    {
        ChatViewModel viewModel = new ChatViewModel();

        await viewModel.DisposeAsync();
    }

    [Fact]
    public async Task ChatConnection_DisposedTwice_RunsItsShutdownOnce()
    {
        DisposeCounter counter = new DisposeCounter();
        ChatConnection connection = Connected(counter);

        await connection.DisposeAsync();
        await connection.DisposeAsync();

        Assert.Equal(1, counter.Count);
    }

    [Fact]
    public void ServerLogBuffer_KeepsAuditAndWarningsButNotRoutineLines()
    {
        ServerLogBuffer log = new ServerLogBuffer();

        log.Add("info: Microsoft.Hosting.Lifetime[0] Application started");
        log.Add("[Audit] ingredient D066: owncost 0.41 -> 0.43 (approved by user via elicitation)");
        log.Add("warn: something odd");

        Assert.Equal(2, log.ImportantLines().Count);
        Assert.DoesNotContain(log.ImportantLines(), line => line.StartsWith("info:"));
        Assert.Equal(3, log.RecentLines(10).Count);
    }
}
