using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.AI;

namespace CFCAgent.App.Core;

// One entry of a saved conversation as the window showed it: who said it, the text, and for an approval
// card how the question ended.
public sealed record TranscriptEntry(ChatAuthor Author, string Text, bool IsError = false, ApprovalState? Approval = null);

// A conversation as it is kept on disk. It holds two views of the same chat, because they differ:
// Turns is what the model is sent (tool calls and their results included, notices and cards left out), and
// Transcript is what the window shows (notices, approval records and stopped replies included, tool calls
// left out). Reopening a chat needs both.
public sealed class SavedConversation
{
    public required string Id { get; init; }

    public required string Title { get; init; }

    // What the app was connected to when the chat was last used ("tenant 'x'"), if it was connected.
    public string? Source { get; init; }

    public DateTimeOffset CreatedUtc { get; init; }

    public DateTimeOffset UpdatedUtc { get; init; }

    public required IReadOnlyList<ChatMessage> Turns { get; init; }

    public required IReadOnlyList<TranscriptEntry> Transcript { get; init; }
}

// What the list of recent chats needs to know about a conversation, without loading all of it.
public sealed record ConversationSummary(string Id, string Title, DateTimeOffset UpdatedUtc);

// Saved conversations, in one SQLite file in the user's own profile. Nothing here touches the fertilizer
// database: the file belongs to the app and to this Windows (or other OS) user. It is not encrypted, so it is
// as private as the user's other files. A database connection string is never part of a conversation.
//
// Every call opens the file, does its work and closes it again, so nothing is held open between calls and a
// failure in one call cannot break the next. The file and its table are created on first use.
public sealed class ConversationStore
{
    private const int SchemaVersion = 1;

    private static readonly JsonSerializerOptions TranscriptJson = new JsonSerializerOptions
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly string _databasePath;
    private bool _schemaReady;

    public ConversationStore(string databasePath)
    {
        _databasePath = databasePath;
    }

    // Beside tenant.json, in the per-user application data folder.
    public static string DefaultPath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CFCAgent", "conversations.db");

    public string DatabasePath => _databasePath;

    // The most recently used conversations, newest first.
    public IReadOnlyList<ConversationSummary> List(int limit = 50)
    {
        using SqliteConnection connection = Open();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "SELECT id, title, updated_utc FROM conversation ORDER BY updated_utc DESC LIMIT $limit";
        command.Parameters.AddWithValue("$limit", limit);

        List<ConversationSummary> summaries = new List<ConversationSummary>();
        using SqliteDataReader reader = command.ExecuteReader();
        while (reader.Read())
        {
            summaries.Add(new ConversationSummary(reader.GetString(0), reader.GetString(1), ParseTime(reader.GetString(2))));
        }

        return summaries;
    }

    // Returns null when there is no conversation with that id (it was deleted, for example).
    public SavedConversation? Load(string id)
    {
        using SqliteConnection connection = Open();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText =
            "SELECT title, source, created_utc, updated_utc, turns_json, transcript_json FROM conversation WHERE id = $id";
        command.Parameters.AddWithValue("$id", id);

        using SqliteDataReader reader = command.ExecuteReader();
        if (!reader.Read())
        {
            return null;
        }

        List<ChatMessage> turns =
            JsonSerializer.Deserialize<List<ChatMessage>>(reader.GetString(4), AIJsonUtilities.DefaultOptions) ?? new List<ChatMessage>();
        List<TranscriptEntry> transcript =
            JsonSerializer.Deserialize<List<TranscriptEntry>>(reader.GetString(5), TranscriptJson) ?? new List<TranscriptEntry>();

        return new SavedConversation
        {
            Id = id,
            Title = reader.GetString(0),
            Source = reader.IsDBNull(1) ? null : reader.GetString(1),
            CreatedUtc = ParseTime(reader.GetString(2)),
            UpdatedUtc = ParseTime(reader.GetString(3)),
            Turns = turns,
            Transcript = transcript,
        };
    }

    // Adds the conversation, or replaces the saved copy with the same id. The time it was first created is
    // kept from the first save, and so is what it was connected to when a later save does not know.
    public void Save(SavedConversation conversation)
    {
        string turnsJson = JsonSerializer.Serialize(conversation.Turns.ToList(), AIJsonUtilities.DefaultOptions);
        string transcriptJson = JsonSerializer.Serialize(conversation.Transcript.ToList(), TranscriptJson);

        using SqliteConnection connection = Open();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO conversation (id, title, source, created_utc, updated_utc, turns_json, transcript_json)
            VALUES ($id, $title, $source, $created, $updated, $turns, $transcript)
            ON CONFLICT(id) DO UPDATE SET
                title = excluded.title,
                source = COALESCE(excluded.source, conversation.source),
                updated_utc = excluded.updated_utc,
                turns_json = excluded.turns_json,
                transcript_json = excluded.transcript_json
            """;
        command.Parameters.AddWithValue("$id", conversation.Id);
        command.Parameters.AddWithValue("$title", conversation.Title);
        command.Parameters.AddWithValue("$source", (object?)conversation.Source ?? DBNull.Value);
        command.Parameters.AddWithValue("$created", FormatTime(conversation.CreatedUtc));
        command.Parameters.AddWithValue("$updated", FormatTime(conversation.UpdatedUtc));
        command.Parameters.AddWithValue("$turns", turnsJson);
        command.Parameters.AddWithValue("$transcript", transcriptJson);
        command.ExecuteNonQuery();
    }

    // Removes the conversation for good. Returns false when there was none with that id.
    public bool Delete(string id)
    {
        using SqliteConnection connection = Open();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "DELETE FROM conversation WHERE id = $id";
        command.Parameters.AddWithValue("$id", id);
        return command.ExecuteNonQuery() > 0;
    }

    private SqliteConnection Open()
    {
        string? folder = Path.GetDirectoryName(_databasePath);
        if (!string.IsNullOrEmpty(folder))
        {
            Directory.CreateDirectory(folder);
        }

        // No pooling: a pooled connection would keep the file open after the call that used it.
        SqliteConnectionStringBuilder builder = new SqliteConnectionStringBuilder
        {
            DataSource = _databasePath,
            Pooling = false,
        };
        SqliteConnection connection = new SqliteConnection(builder.ConnectionString);
        try
        {
            connection.Open();
            if (!_schemaReady)
            {
                CreateSchema(connection);
                _schemaReady = true;
            }

            return connection;
        }
        catch
        {
            connection.Dispose();
            throw;
        }
    }

    private static void CreateSchema(SqliteConnection connection)
    {
        using SqliteCommand version = connection.CreateCommand();
        version.CommandText = "PRAGMA user_version";
        long found = (long)(version.ExecuteScalar() ?? 0L);
        if (found > SchemaVersion)
        {
            throw new InvalidOperationException(
                "The saved conversations were written by a newer version of CFC Agent, so this version cannot read them.");
        }

        using SqliteCommand create = connection.CreateCommand();
        create.CommandText =
            $"""
            CREATE TABLE IF NOT EXISTS conversation (
                id              TEXT PRIMARY KEY,
                title           TEXT NOT NULL,
                source          TEXT,
                created_utc     TEXT NOT NULL,
                updated_utc     TEXT NOT NULL,
                turns_json      TEXT NOT NULL,
                transcript_json TEXT NOT NULL
            );
            CREATE INDEX IF NOT EXISTS conversation_updated ON conversation (updated_utc DESC);
            PRAGMA user_version = {SchemaVersion};
            """;
        create.ExecuteNonQuery();
    }

    // Times are stored as UTC text in one fixed format, so sorting the text sorts by time.
    private static string FormatTime(DateTimeOffset time)
    {
        return time.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ss.fffffffZ", CultureInfo.InvariantCulture);
    }

    private static DateTimeOffset ParseTime(string text)
    {
        return DateTimeOffset.Parse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal);
    }
}
