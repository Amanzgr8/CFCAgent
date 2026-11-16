using System.Globalization;
using CFCAgent.App.Core;
using CFCAgent.Backend;
using Microsoft.Extensions.AI;

namespace CFCAgent.App.Core.Tests;

// New chat and the list of recent chats: what is saved and when, what a reopened chat shows, and what the
// model is given when the next question is asked in it. A stub model, a fake connection and a SQLite file in
// a temporary folder: no server, no database, and never the user's real saved chats.
public class RecentChatsTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "cfcagent-recent-tests-" + Guid.NewGuid().ToString("N"));

    private string DatabasePath => Path.Combine(_folder, "conversations.db");

    public void Dispose()
    {
        if (Directory.Exists(_folder))
        {
            Directory.Delete(_folder, recursive: true);
        }
    }

    // A clock the test moves by hand, in UTC, so "Today" and "Yesterday" do not depend on when the test runs.
    private sealed class TestClock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = new DateTimeOffset(2026, 11, 15, 14, 5, 0, TimeSpan.Zero);

        public override DateTimeOffset GetUtcNow() => Now;

        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
    }

    private sealed class FakeBackend
    {
        public List<ChatSession> Sessions { get; } = new List<ChatSession>();

        public int Disposed { get; set; }

        public bool Fail { get; set; }

        public string? Source { get; set; } = "tenant 'default'";

        public TimeSpan ReplyDelay { get; set; } = TimeSpan.Zero;

        public IChatConnector Connector(bool allowChanges = false) => new FakeConnector(this, allowChanges);
    }

    private sealed class FakeConnector : IChatConnector
    {
        private readonly FakeBackend _backend;
        private readonly bool _allowChanges;

        public FakeConnector(FakeBackend backend, bool allowChanges)
        {
            _backend = backend;
            _allowChanges = allowChanges;
        }

        public Task<ChatConnection> ConnectAsync(CancellationToken cancellationToken)
        {
            if (_backend.Fail)
            {
                throw new InvalidOperationException("the server would not start");
            }

            ChatSession session = new ChatSession(
                new StubChatClient(_backend.ReplyDelay),
                Array.Empty<AITool>(),
                _allowChanges ? "system prompt: changes allowed" : "system prompt: read-only");
            _backend.Sessions.Add(session);
            ChatConnection connection = new ChatConnection(
                session,
                "connected",
                Array.Empty<AITool>(),
                new ServerLogBuffer(),
                () =>
                {
                    _backend.Disposed++;
                    return ValueTask.CompletedTask;
                })
            {
                Source = _backend.Source,
            };
            return Task.FromResult(connection);
        }
    }

    // What a session would send to the model, as "role: text" lines.
    private static List<string> Turns(ChatSession session)
    {
        return session.History.Select(message => message.Role.Value + ": " + message.Text).ToList();
    }

    private async Task<(ChatViewModel Chat, FakeBackend Backend, TestClock Clock)> ConnectedAsync(FakeBackend? backend = null, TestClock? clock = null)
    {
        backend ??= new FakeBackend();
        clock ??= new TestClock();
        ChatViewModel chat = new ChatViewModel();
        chat.EnableModeSwitch(backend.Connector);
        chat.AttachHistory(new ConversationStore(DatabasePath), clock);
        await chat.ConnectAsync(backend.Connector());
        return (chat, backend, clock);
    }

    private static async Task AskAsync(ChatViewModel chat, string question)
    {
        chat.Input = question;
        await chat.SendCommand.ExecuteAsync(null);
    }

    // ---- New chat ----

    [Fact]
    public async Task NewChat_EmptiesTheWindow_AndTheModelStartsFromNothing_OnTheSameConnection()
    {
        (ChatViewModel chat, FakeBackend backend, TestClock _) = await ConnectedAsync();
        await AskAsync(chat, "what does D066 cost?");
        Assert.Equal(2, chat.Messages.Count);

        chat.NewChatCommand.Execute(null);

        Assert.Empty(chat.Messages);
        Assert.True(chat.IsConnected);
        Assert.Single(backend.Sessions);
        Assert.Equal(0, backend.Disposed);
        Assert.Equal(new[] { "system: system prompt: read-only" }, Turns(backend.Sessions[0]));

        await AskAsync(chat, "list the binders");

        List<string> turns = Turns(backend.Sessions[0]);
        Assert.Equal(3, turns.Count);
        Assert.Equal("user: list the binders", turns[1]);
        Assert.DoesNotContain(turns, turn => turn.Contains("D066"));
    }

    [Fact]
    public async Task NewChat_WorksWithoutAStore_AndNothingIsListed()
    {
        ChatViewModel chat = ChatViewModel.CreateDemo(TimeSpan.Zero);
        await AskAsync(chat, "hello");

        chat.NewChatCommand.Execute(null);

        Assert.False(chat.HasHistory);
        Assert.Empty(chat.Messages);
        Assert.Empty(chat.RecentChats);
    }

    [Fact]
    public async Task NewChat_ClosesTheSettingsScreen()
    {
        (ChatViewModel chat, FakeBackend _, TestClock _) = await ConnectedAsync();
        SettingsViewModel settings = new SettingsViewModel(
            new TenantSettings(Path.Combine(_folder, "tenant.json"), new NoCredentials()),
            new NoTester(),
            Path.Combine(_folder, "schema-map.yaml"),
            "test store");
        chat.AttachSettings(settings);
        settings.Open();

        chat.NewChatCommand.Execute(null);

        Assert.False(settings.IsOpen);
    }

    private sealed class NoCredentials : ICredentialStore
    {
        public string? Get(string target) => null;

        public void Set(string target, string userName, string secret)
        {
        }

        public bool Delete(string target) => false;
    }

    private sealed class NoTester : IConnectionTester
    {
        public Task<ConnectionTestResult> TestAsync(string connectionString, string? productConfigPath, CancellationToken cancellationToken)
        {
            return Task.FromResult(new ConnectionTestResult(true, "ok"));
        }
    }

    // ---- Saving ----

    [Fact]
    public async Task ANewChat_IsNotSaved_UntilSomethingWasAsked()
    {
        (ChatViewModel chat, FakeBackend _, TestClock _) = await ConnectedAsync();

        chat.NewChatCommand.Execute(null);
        chat.NewChatCommand.Execute(null);

        Assert.True(chat.HasHistory);
        Assert.Empty(chat.RecentChats);
        Assert.Empty(new ConversationStore(DatabasePath).List());
    }

    [Fact]
    public async Task AfterAReply_TheChatIsSaved_AndListedUnderItsFirstQuestion()
    {
        (ChatViewModel chat, FakeBackend _, TestClock _) = await ConnectedAsync();

        await AskAsync(chat, "what does D066 cost?");

        RecentChatViewModel listed = Assert.Single(chat.RecentChats);
        Assert.Equal("what does D066 cost?", listed.Title);
        Assert.True(listed.IsCurrent);
        Assert.StartsWith("Today, ", listed.WhenText);

        SavedConversation? saved = new ConversationStore(DatabasePath).Load(listed.Id);
        Assert.NotNull(saved);
        Assert.Equal("tenant 'default'", saved.Source);
        Assert.Equal(new[] { "user", "assistant" }, saved.Turns.Select(turn => turn.Role.Value));
        Assert.Equal(2, saved.Transcript.Count);
        Assert.Equal(new TranscriptEntry(ChatAuthor.User, "what does D066 cost?"), saved.Transcript[0]);
    }

    [Fact]
    public async Task TheSystemPrompt_IsNotSaved()
    {
        // It belongs to the session, not the conversation: a reopened chat gets the prompt of the session it
        // is reopened in.
        (ChatViewModel chat, FakeBackend _, TestClock _) = await ConnectedAsync();

        await AskAsync(chat, "hello");

        SavedConversation? saved = new ConversationStore(DatabasePath).Load(chat.RecentChats[0].Id);
        Assert.NotNull(saved);
        Assert.DoesNotContain(saved.Turns, turn => turn.Role == ChatRole.System);
    }

    [Fact]
    public async Task ASecondQuestion_UpdatesTheSameSavedChat()
    {
        (ChatViewModel chat, FakeBackend _, TestClock _) = await ConnectedAsync();
        await AskAsync(chat, "what does D066 cost?");

        await AskAsync(chat, "and per ton?");

        RecentChatViewModel listed = Assert.Single(chat.RecentChats);
        Assert.Equal("what does D066 cost?", listed.Title);
        SavedConversation? saved = new ConversationStore(DatabasePath).Load(listed.Id);
        Assert.NotNull(saved);
        Assert.Equal(4, saved.Turns.Count);
        Assert.Equal(4, saved.Transcript.Count);
    }

    [Fact]
    public async Task ALongOrMultiLineFirstQuestion_GivesAShortOneLineTitle()
    {
        (ChatViewModel chat, FakeBackend _, TestClock _) = await ConnectedAsync();

        await AskAsync(chat, "Which of the formulas that use D066 would become infeasible if its stock ran out tomorrow?\nSecond line.");

        string title = Assert.Single(chat.RecentChats).Title;
        Assert.Equal("Which of the formulas that use D066 would become infeasible...", title);
    }

    [Fact]
    public async Task NewChat_LeavesTheOldChatInTheList_AndTheNextOneGoesOnTop()
    {
        (ChatViewModel chat, FakeBackend _, TestClock clock) = await ConnectedAsync();
        await AskAsync(chat, "first chat");

        chat.NewChatCommand.Execute(null);

        Assert.False(Assert.Single(chat.RecentChats).IsCurrent);

        clock.Now = clock.Now.AddMinutes(5);
        await AskAsync(chat, "second chat");

        Assert.Equal(new[] { "second chat", "first chat" }, chat.RecentChats.Select(listed => listed.Title));
        Assert.Equal(new[] { true, false }, chat.RecentChats.Select(listed => listed.IsCurrent));
    }

    [Fact]
    public async Task AStoppedOrFailedReply_IsStillSaved_AsTheWindowShowsIt()
    {
        FakeBackend backend = new FakeBackend { ReplyDelay = TimeSpan.FromMilliseconds(40) };
        (ChatViewModel chat, FakeBackend _, TestClock _) = await ConnectedAsync(backend);
        chat.Input = "a question that is stopped half way";
        Task sending = chat.SendCommand.ExecuteAsync(null);
        await Task.Delay(100);

        chat.SendCancelCommand.Execute(null);
        await sending;

        SavedConversation? saved = new ConversationStore(DatabasePath).Load(Assert.Single(chat.RecentChats).Id);
        Assert.NotNull(saved);
        Assert.Equal(2, saved.Transcript.Count);
        Assert.EndsWith("(stopped)", saved.Transcript[1].Text);

        // The model was never given the stopped turn, so it is not in what a reopened chat sends either.
        Assert.Empty(saved.Turns);
    }

    // ---- Reopening ----

    [Fact]
    public async Task OpeningASavedChat_ShowsItAgain_AndTheModelCarriesOnFromIt()
    {
        (ChatViewModel chat, FakeBackend backend, TestClock clock) = await ConnectedAsync();
        await AskAsync(chat, "what does D066 cost?");
        chat.NewChatCommand.Execute(null);
        await AskAsync(chat, "list the binders");
        RecentChatViewModel first = chat.RecentChats.Single(listed => listed.Title == "what does D066 cost?");
        clock.Now = clock.Now.AddDays(2);

        chat.OpenChatCommand.Execute(first);

        // On screen: the earlier question and its reply, nothing from the other chat.
        Assert.Equal(2, chat.Messages.Count);
        Assert.Equal("what does D066 cost?", chat.Messages[0].Text);
        Assert.Equal(ChatAuthor.Assistant, chat.Messages[1].Author);
        Assert.True(first.IsCurrent);
        Assert.False(chat.RecentChats.Single(listed => listed.Title == "list the binders").IsCurrent);

        // The server was not restarted. The model gets this session's system prompt, the saved turns, and a
        // note that time has passed.
        Assert.Single(backend.Sessions);
        Assert.Equal(0, backend.Disposed);
        List<string> turns = Turns(backend.Sessions[0]);
        Assert.Equal(4, turns.Count);
        Assert.Equal("system: system prompt: read-only", turns[0]);
        Assert.Equal("user: what does D066 cost?", turns[1]);
        Assert.StartsWith("assistant: ", turns[2]);
        Assert.StartsWith("user: " + ChatSession.AppNotePrefix, turns[3]);
        Assert.Contains("reopened", turns[3]);
        Assert.Contains("15 November 2026", turns[3]);
        Assert.Contains("read-only", turns[3]);
        Assert.Contains("look values up again", turns[3]);

        await AskAsync(chat, "and per ton?");

        turns = Turns(backend.Sessions[0]);
        Assert.Equal("user: and per ton?", turns[4]);
        Assert.Equal(4, chat.Messages.Count);

        // It is still the same saved chat, now the most recent one.
        Assert.Equal(2, chat.RecentChats.Count);
        Assert.Same(first, chat.RecentChats[0]);
        SavedConversation? saved = new ConversationStore(DatabasePath).Load(first.Id);
        Assert.NotNull(saved);
        Assert.Equal(4, saved.Transcript.Count);
    }

    [Fact]
    public async Task OpeningTheChatThatIsAlreadyOpen_ChangesNothing()
    {
        (ChatViewModel chat, FakeBackend backend, TestClock _) = await ConnectedAsync();
        await AskAsync(chat, "hello");
        ChatMessageViewModel question = chat.Messages[0];
        int turnsBefore = backend.Sessions[0].History.Count;

        chat.OpenChatCommand.Execute(chat.RecentChats[0]);

        Assert.Same(question, chat.Messages[0]);
        Assert.Equal(turnsBefore, backend.Sessions[0].History.Count);
    }

    [Fact]
    public async Task AfterTheAppIsRestarted_EarlierChatsAreListed_AndCanBeReopened()
    {
        (ChatViewModel before, FakeBackend _, TestClock clock) = await ConnectedAsync();
        await AskAsync(before, "what does D066 cost?");
        await before.DisposeAsync();

        // A new view-model and a new store on the same file: what a second launch of the app has.
        clock.Now = clock.Now.AddDays(1);
        (ChatViewModel after, FakeBackend backend, TestClock _) = await ConnectedAsync(clock: clock);

        RecentChatViewModel listed = Assert.Single(after.RecentChats);
        Assert.Equal("what does D066 cost?", listed.Title);
        Assert.Equal("Yesterday", listed.WhenText);
        Assert.False(listed.IsCurrent);
        Assert.Empty(after.Messages);

        after.OpenChatCommand.Execute(listed);

        Assert.Equal(2, after.Messages.Count);
        Assert.Equal("user: what does D066 cost?", Turns(backend.Sessions[0])[1]);
    }

    [Fact]
    public async Task AReopenedChat_ShowsItsNoticesAndApprovalRecords_AndTheRecordsCannotBeAnswered()
    {
        (ChatViewModel before, FakeBackend _, TestClock _) = await ConnectedAsync();
        await AskAsync(before, "set D066 to 0.43");
        ApprovalRequestViewModel approved = new ApprovalRequestViewModel("Ingredient D066\n  owncost: 0.41 -> 0.43");
        before.ShowApproval(approved);
        approved.ApplyCommand.Execute(null);
        ApprovalRequestViewModel cancelled = new ApprovalRequestViewModel("Ingredient D067\n  owncost: 0.50 -> 0.55");
        before.ShowApproval(cancelled);
        cancelled.CancelCommand.Execute(null);
        before.Messages.Add(new ChatMessageViewModel(ChatAuthor.Notice, "Something went wrong: timeout", isError: true));
        await AskAsync(before, "thanks");
        await before.DisposeAsync();

        (ChatViewModel after, FakeBackend _, TestClock _) = await ConnectedAsync();
        after.OpenChatCommand.Execute(after.RecentChats[0]);

        Assert.Equal(7, after.Messages.Count);
        ChatMessageViewModel firstCard = after.Messages[2];
        Assert.True(firstCard.IsApproval);
        Assert.NotNull(firstCard.Approval);
        Assert.Equal(ApprovalState.Approved, firstCard.Approval.State);
        Assert.Equal("Ingredient D066\n  owncost: 0.41 -> 0.43", firstCard.Approval.Message);
        Assert.False(firstCard.Approval.ApplyCommand.CanExecute(null));
        Assert.Equal(ApprovalState.Cancelled, after.Messages[3].Approval!.State);
        Assert.True(after.Messages[4].IsErrorNotice);

        // A record is not a question: nothing is waiting for an answer.
        Assert.Null(after.PendingApproval);
    }

    [Fact]
    public async Task AChatOpenedWhileNotConnected_IsHandedToTheNextConnection()
    {
        (ChatViewModel before, FakeBackend _, TestClock _) = await ConnectedAsync();
        await AskAsync(before, "what does D066 cost?");
        await before.DisposeAsync();

        FakeBackend backend = new FakeBackend { Fail = true };
        (ChatViewModel after, FakeBackend _, TestClock _) = await ConnectedAsync(backend);
        Assert.False(after.IsConnected);

        after.OpenChatCommand.Execute(after.RecentChats[0]);

        Assert.Equal("what does D066 cost?", after.Messages[0].Text);

        backend.Fail = false;
        await after.RetryConnectionCommand.ExecuteAsync(null);

        Assert.True(after.IsConnected);
        List<string> turns = Turns(backend.Sessions[0]);
        Assert.Equal("user: what does D066 cost?", turns[1]);
        Assert.Contains("reopened", turns[3]);
    }

    [Fact]
    public async Task AChatHeldAgainstAnotherSetup_SaysSoWhenReopened()
    {
        (ChatViewModel before, FakeBackend _, TestClock _) = await ConnectedAsync(new FakeBackend { Source = "tenant 'plant-a'" });
        await AskAsync(before, "what does D066 cost?");
        await before.DisposeAsync();

        FakeBackend backend = new FakeBackend { Source = "tenant 'plant-b'" };
        (ChatViewModel after, FakeBackend _, TestClock _) = await ConnectedAsync(backend);
        after.OpenChatCommand.Execute(after.RecentChats[0]);

        ChatMessageViewModel notice = after.Messages[^1];
        Assert.True(notice.IsInfoNotice);
        Assert.Contains("tenant 'plant-a'", notice.Text);
        Assert.Contains("tenant 'plant-b'", notice.Text);
        Assert.Contains("may be a different database", Turns(backend.Sessions[0])[^1]);
    }

    [Fact]
    public async Task AChatThatIsGoneFromTheFile_IsDroppedFromTheList()
    {
        (ChatViewModel chat, FakeBackend _, TestClock _) = await ConnectedAsync();
        await AskAsync(chat, "first chat");
        chat.NewChatCommand.Execute(null);
        RecentChatViewModel listed = chat.RecentChats[0];
        new ConversationStore(DatabasePath).Delete(listed.Id);

        chat.OpenChatCommand.Execute(listed);

        Assert.Empty(chat.RecentChats);
        Assert.Empty(chat.Messages);
    }

    // ---- The mode switch and settings ----

    [Fact]
    public async Task SwitchingTheMode_StaysTheSameSavedChat_AndSavesTheNotice()
    {
        (ChatViewModel chat, FakeBackend _, TestClock _) = await ConnectedAsync();
        await AskAsync(chat, "what does D066 cost?");

        await chat.ToggleChangesCommand.ExecuteAsync(null);
        await AskAsync(chat, "set it to 0.43");

        RecentChatViewModel listed = Assert.Single(chat.RecentChats);
        Assert.True(listed.IsCurrent);
        SavedConversation? saved = new ConversationStore(DatabasePath).Load(listed.Id);
        Assert.NotNull(saved);
        Assert.Equal(5, saved.Transcript.Count);
        Assert.Equal(ChatAuthor.Notice, saved.Transcript[2].Author);
        Assert.Contains("Changes are now allowed", saved.Transcript[2].Text);
    }

    [Fact]
    public async Task AChatReopenedWithChangesAllowed_TellsTheModelSo()
    {
        (ChatViewModel chat, FakeBackend backend, TestClock _) = await ConnectedAsync();
        await AskAsync(chat, "what does D066 cost?");
        chat.NewChatCommand.Execute(null);
        await chat.ToggleChangesCommand.ExecuteAsync(null);

        chat.OpenChatCommand.Execute(chat.RecentChats[0]);

        string note = Turns(backend.Sessions[1])[^1];
        Assert.Contains("Changes are allowed", note);
        Assert.DoesNotContain("read-only", note);
    }

    // ---- Deleting ----

    [Fact]
    public async Task DeletingTheOpenChat_RemovesItForGood_AndStartsANewOne()
    {
        (ChatViewModel chat, FakeBackend backend, TestClock _) = await ConnectedAsync();
        await AskAsync(chat, "what does D066 cost?");
        RecentChatViewModel listed = chat.RecentChats[0];

        chat.DeleteChatCommand.Execute(listed);

        Assert.Empty(chat.RecentChats);
        Assert.Empty(chat.Messages);
        Assert.Empty(new ConversationStore(DatabasePath).List());
        Assert.Single(Turns(backend.Sessions[0]));

        // The next question starts a chat of its own, not the deleted one again.
        await AskAsync(chat, "list the binders");
        Assert.NotEqual(listed.Id, Assert.Single(chat.RecentChats).Id);
    }

    [Fact]
    public async Task DeletingAnotherChat_LeavesTheOpenOneAlone()
    {
        (ChatViewModel chat, FakeBackend _, TestClock _) = await ConnectedAsync();
        await AskAsync(chat, "first chat");
        chat.NewChatCommand.Execute(null);
        await AskAsync(chat, "second chat");
        RecentChatViewModel first = chat.RecentChats.Single(listed => listed.Title == "first chat");

        chat.DeleteChatCommand.Execute(first);

        Assert.Equal("second chat", Assert.Single(chat.RecentChats).Title);
        Assert.Equal(2, chat.Messages.Count);
        Assert.Equal("second chat", chat.Messages[0].Text);
    }

    [Fact]
    public void DeletingARow_TakesTwoSteps_AndCanBeCalledOff()
    {
        RecentChatViewModel row = new RecentChatViewModel("c1", "a chat", "Today, 14:05");

        row.AskDeleteCommand.Execute(null);
        Assert.True(row.IsConfirmingDelete);

        row.KeepChatCommand.Execute(null);
        Assert.False(row.IsConfirmingDelete);
    }

    [Fact]
    public async Task OpeningAChat_CallsOffAHalfConfirmedDelete()
    {
        (ChatViewModel chat, FakeBackend _, TestClock _) = await ConnectedAsync();
        await AskAsync(chat, "first chat");
        chat.NewChatCommand.Execute(null);
        await AskAsync(chat, "second chat");
        RecentChatViewModel first = chat.RecentChats[1];
        first.AskDeleteCommand.Execute(null);

        chat.OpenChatCommand.Execute(first);

        Assert.False(first.IsConfirmingDelete);
        Assert.Equal(2, chat.RecentChats.Count);
    }

    // ---- When it must not be possible, and when the file fails ----

    [Fact]
    public async Task WhileAReplyIsArriving_ChatsCannotBeSwappedOrDeleted()
    {
        (ChatViewModel chat, FakeBackend _, TestClock _) = await ConnectedAsync();
        await AskAsync(chat, "hello");
        Assert.True(chat.NewChatCommand.CanExecute(null));

        chat.IsBusy = true;

        Assert.False(chat.NewChatCommand.CanExecute(null));
        Assert.False(chat.OpenChatCommand.CanExecute(chat.RecentChats[0]));
        Assert.False(chat.DeleteChatCommand.CanExecute(chat.RecentChats[0]));
    }

    [Fact]
    public async Task AFileThatCannotBeWritten_DoesNotBreakTheChat_AndSaysSoOnce()
    {
        // A file where the folder should be: every attempt to open the store fails.
        Directory.CreateDirectory(_folder);
        string blocker = Path.Combine(_folder, "not-a-folder");
        File.WriteAllText(blocker, "x");
        FakeBackend backend = new FakeBackend();
        ChatViewModel chat = new ChatViewModel();
        chat.AttachHistory(new ConversationStore(Path.Combine(blocker, "conversations.db")), new TestClock());
        await chat.ConnectAsync(backend.Connector());
        Assert.Contains("could not be loaded", Assert.Single(chat.Messages).Text);

        await AskAsync(chat, "hello");
        await AskAsync(chat, "hello again");

        Assert.Equal(4, chat.Messages.Count(message => message.IsUser || message.IsAssistant));
        Assert.Single(chat.Messages, message => message.Text.Contains("could not be saved"));
        Assert.Empty(chat.RecentChats);
    }

    // ---- "Today", "Yesterday", a date ----

    [Theory]
    [InlineData("2026-11-15T09:30:00", "Today, 09:30")]
    [InlineData("2026-11-14T23:59:00", "Yesterday")]
    [InlineData("2026-11-02T08:00:00", "2 Nov")]
    [InlineData("2025-12-31T08:00:00", "31 Dec 2025")]
    public void WhenAChatWasLastUsed_IsSaidInWords(string when, string expected)
    {
        DateTimeOffset now = new DateTimeOffset(2026, 11, 15, 14, 5, 0, TimeSpan.Zero);
        DateTimeOffset used = new DateTimeOffset(DateTime.Parse(when, CultureInfo.InvariantCulture), TimeSpan.Zero);

        Assert.Equal(expected, RecentChatViewModel.DescribeWhen(used, now, CultureInfo.InvariantCulture));
    }
}
