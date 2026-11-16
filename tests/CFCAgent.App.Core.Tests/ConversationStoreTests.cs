using System.Text.Json;
using CFCAgent.App.Core;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.AI;

namespace CFCAgent.App.Core.Tests;

// The SQLite file that keeps conversations: what goes in comes out the same, the list is newest first, and
// the file is left closed after every call. Each test uses its own file in a temporary folder.
public class ConversationStoreTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "cfcagent-store-tests-" + Guid.NewGuid().ToString("N"));

    private string DatabasePath => Path.Combine(_folder, "conversations.db");

    public void Dispose()
    {
        if (Directory.Exists(_folder))
        {
            Directory.Delete(_folder, recursive: true);
        }
    }

    private static DateTimeOffset At(int day, int hour = 12)
    {
        return new DateTimeOffset(2026, 11, day, hour, 0, 0, TimeSpan.Zero);
    }

    private static SavedConversation Conversation(string id, string title, DateTimeOffset updated, string? source = "tenant 'default'")
    {
        return new SavedConversation
        {
            Id = id,
            Title = title,
            Source = source,
            CreatedUtc = updated,
            UpdatedUtc = updated,
            Turns = new List<ChatMessage>
            {
                new ChatMessage(ChatRole.User, title),
                new ChatMessage(ChatRole.Assistant, "an answer"),
            },
            Transcript = new List<TranscriptEntry>
            {
                new TranscriptEntry(ChatAuthor.User, title),
                new TranscriptEntry(ChatAuthor.Assistant, "an answer"),
            },
        };
    }

    [Fact]
    public void ANewStore_HasNothing_AndCreatesItsFolderAndFile()
    {
        ConversationStore store = new ConversationStore(DatabasePath);

        Assert.Empty(store.List());
        Assert.Null(store.Load("missing"));
        Assert.True(File.Exists(DatabasePath));
    }

    [Fact]
    public void WhatIsSaved_ComesBackTheSame()
    {
        ConversationStore store = new ConversationStore(DatabasePath);
        SavedConversation saved = new SavedConversation
        {
            Id = "c1",
            Title = "What does D066 cost?",
            Source = "tenant 'default'",
            CreatedUtc = At(10),
            UpdatedUtc = At(11, 9),
            Turns = new List<ChatMessage>
            {
                new ChatMessage(ChatRole.User, "What does D066 cost?"),
                new ChatMessage(ChatRole.Assistant, "D066 costs **0.41** per lb.\n\n| Code | Cost |\n|---|---:|\n| D066 | 0.41 |"),
            },
            Transcript = new List<TranscriptEntry>
            {
                new TranscriptEntry(ChatAuthor.User, "What does D066 cost?"),
                new TranscriptEntry(ChatAuthor.Assistant, "D066 costs **0.41** per lb."),
                new TranscriptEntry(ChatAuthor.Notice, "Changes are now allowed."),
                new TranscriptEntry(ChatAuthor.Notice, "Something went wrong: timeout", IsError: true),
                new TranscriptEntry(ChatAuthor.Approval, "Ingredient D066\n  owncost: 0.41 -> 0.43", Approval: ApprovalState.Approved),
            },
        };

        store.Save(saved);
        SavedConversation? loaded = new ConversationStore(DatabasePath).Load("c1");

        Assert.NotNull(loaded);
        Assert.Equal("What does D066 cost?", loaded.Title);
        Assert.Equal("tenant 'default'", loaded.Source);
        Assert.Equal(At(10), loaded.CreatedUtc);
        Assert.Equal(At(11, 9), loaded.UpdatedUtc);
        Assert.Equal(2, loaded.Turns.Count);
        Assert.Equal(ChatRole.User, loaded.Turns[0].Role);
        Assert.Equal(saved.Turns[1].Text, loaded.Turns[1].Text);
        Assert.Equal(saved.Transcript, loaded.Transcript);
    }

    [Fact]
    public void ToolCallsAndTheirResults_SurviveBeingSaved()
    {
        // What a real reply leaves in the history: the model asks for a tool, the tool answers, the model
        // replies. All of it has to come back, or the model would be sent a conversation with holes in it.
        ConversationStore store = new ConversationStore(DatabasePath);
        JsonElement result = JsonSerializer.SerializeToElement(new { code = "D066", owncost = 0.41 });
        List<ChatMessage> turns = new List<ChatMessage>
        {
            new ChatMessage(ChatRole.User, "What does D066 cost?"),
            new ChatMessage(ChatRole.Assistant, new List<AIContent>
            {
                new TextContent("Let me look that up."),
                new FunctionCallContent("call_1", "get_ingredient", new Dictionary<string, object?> { ["code"] = "D066", ["limit"] = 5 }),
            }),
            new ChatMessage(ChatRole.Tool, new List<AIContent> { new FunctionResultContent("call_1", result) }),
            new ChatMessage(ChatRole.Assistant, "D066 costs 0.41 per lb."),
        };
        store.Save(new SavedConversation
        {
            Id = "c1",
            Title = "What does D066 cost?",
            CreatedUtc = At(10),
            UpdatedUtc = At(10),
            Turns = turns,
            Transcript = new List<TranscriptEntry>(),
        });

        SavedConversation? loaded = store.Load("c1");

        Assert.NotNull(loaded);
        Assert.Equal(4, loaded.Turns.Count);
        Assert.Equal(new[] { "user", "assistant", "tool", "assistant" }, loaded.Turns.Select(turn => turn.Role.Value));

        FunctionCallContent call = Assert.IsType<FunctionCallContent>(loaded.Turns[1].Contents[1]);
        Assert.Equal("call_1", call.CallId);
        Assert.Equal("get_ingredient", call.Name);
        Assert.NotNull(call.Arguments);
        Assert.Equal("D066", call.Arguments["code"]?.ToString());
        Assert.Equal("5", call.Arguments["limit"]?.ToString());

        FunctionResultContent answer = Assert.IsType<FunctionResultContent>(loaded.Turns[2].Contents[0]);
        Assert.Equal("call_1", answer.CallId);
        JsonElement answered = Assert.IsType<JsonElement>(answer.Result);
        Assert.Equal("D066", answered.GetProperty("code").GetString());
        Assert.Equal(0.41, answered.GetProperty("owncost").GetDouble());
    }

    [Fact]
    public void TheList_IsNewestFirst_AndStopsAtTheLimit()
    {
        ConversationStore store = new ConversationStore(DatabasePath);
        store.Save(Conversation("old", "oldest", At(1)));
        store.Save(Conversation("new", "newest", At(9)));
        store.Save(Conversation("mid", "in between", At(5)));

        IReadOnlyList<ConversationSummary> all = store.List();
        IReadOnlyList<ConversationSummary> two = store.List(2);

        Assert.Equal(new[] { "new", "mid", "old" }, all.Select(summary => summary.Id));
        Assert.Equal("newest", all[0].Title);
        Assert.Equal(At(9), all[0].UpdatedUtc);
        Assert.Equal(new[] { "new", "mid" }, two.Select(summary => summary.Id));
    }

    [Fact]
    public void SavingAgain_ReplacesTheSavedCopy_AndKeepsWhenItWasCreated()
    {
        ConversationStore store = new ConversationStore(DatabasePath);
        store.Save(Conversation("c1", "first version", At(3)));

        SavedConversation later = Conversation("c1", "first version", At(8), source: null);
        store.Save(new SavedConversation
        {
            Id = later.Id,
            Title = later.Title,
            Source = null,
            CreatedUtc = At(8),
            UpdatedUtc = At(8),
            Turns = later.Turns.Append(new ChatMessage(ChatRole.User, "and per ton?")).ToList(),
            Transcript = later.Transcript,
        });

        SavedConversation? loaded = store.Load("c1");
        Assert.NotNull(loaded);
        Assert.Single(store.List());
        Assert.Equal(At(3), loaded.CreatedUtc);
        Assert.Equal(At(8), loaded.UpdatedUtc);
        Assert.Equal(3, loaded.Turns.Count);

        // A save that does not know what it was connected to does not wipe what an earlier save knew.
        Assert.Equal("tenant 'default'", loaded.Source);
    }

    [Fact]
    public void Deleting_RemovesOnlyThatConversation()
    {
        ConversationStore store = new ConversationStore(DatabasePath);
        store.Save(Conversation("keep", "keep me", At(1)));
        store.Save(Conversation("drop", "delete me", At(2)));

        Assert.True(store.Delete("drop"));
        Assert.False(store.Delete("drop"));

        Assert.Null(store.Load("drop"));
        Assert.Equal("keep", Assert.Single(store.List()).Id);
    }

    [Fact]
    public void TheFile_IsNotHeldOpen_BetweenCalls()
    {
        ConversationStore store = new ConversationStore(DatabasePath);
        store.Save(Conversation("c1", "a chat", At(1)));
        store.List();

        // Deleting a file that is still open fails on Windows, so this shows every call closed it.
        File.Delete(DatabasePath);

        Assert.False(File.Exists(DatabasePath));
    }

    [Fact]
    public void AFileFromANewerVersion_IsRefused_NotOverwritten()
    {
        Directory.CreateDirectory(_folder);
        SqliteConnectionStringBuilder builder = new SqliteConnectionStringBuilder { DataSource = DatabasePath, Pooling = false };
        using (SqliteConnection connection = new SqliteConnection(builder.ConnectionString))
        {
            connection.Open();
            using SqliteCommand command = connection.CreateCommand();
            command.CommandText = "PRAGMA user_version = 99";
            command.ExecuteNonQuery();
        }

        ConversationStore store = new ConversationStore(DatabasePath);

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(() => store.List());
        Assert.Contains("newer version", error.Message);
    }
}
