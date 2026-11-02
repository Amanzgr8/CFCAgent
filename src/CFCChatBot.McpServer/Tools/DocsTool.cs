using System.ComponentModel;
using System.Text.Json;
using CFCChatBot.McpServer.Docs;
using ModelContextProtocol;
using ModelContextProtocol.Server;

namespace CFCChatBot.McpServer.Tools;

// Hand-written (not schema-map-driven) since this searches flat doc files,
// not the database - a different kind of tool entirely from SchemaMapSqlFunction.
[McpServerToolType]
public static class DocsTool
{
    [McpServerTool(Name = "search_docs")]
    [Description("Search the product documentation (user guide) for sections matching a keyword or phrase. Returns matching doc name, section title, and a short snippet - call read_section with the exact doc/title to get the full text.")]
    public static string SearchDocs(
        IServiceProvider services,
        [Description("Keyword or phrase to search for, e.g. 'binding agent' or 'liquid formula'.")] string query)
    {
        DocsIndex index = GetDocsIndex(services);
        List<object> matches = [.. index.Sections
            .Where(s => s.Title.Contains(query, StringComparison.OrdinalIgnoreCase)
                     || s.Content.Contains(query, StringComparison.OrdinalIgnoreCase))
            .Select(s => new
            {
                doc = s.Doc,
                section = s.Title,
                snippet = Snippet(s.Content, query),
            })];

        return JsonSerializer.Serialize(matches);
    }

    [McpServerTool(Name = "read_section")]
    [Description("Get the full text of one documentation section, by doc name and exact section title (as returned by search_docs).")]
    public static string ReadSection(
        IServiceProvider services,
        [Description("Doc name, e.g. 'guide' (as returned by search_docs).")] string doc,
        [Description("Exact section title, as returned by search_docs.")] string section)
    {
        DocsIndex index = GetDocsIndex(services);
        DocSection? match = index.Sections.FirstOrDefault(s =>
            string.Equals(s.Doc, doc, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(s.Title, section, StringComparison.OrdinalIgnoreCase));

        if (match is null)
        {
            throw new McpException($"No section '{section}' found in doc '{doc}'. Call search_docs first to find the exact doc/title.");
        }

        return match.Content;
    }

    // IServiceProvider is always excluded from the generated tool schema by the MCP SDK
    // (unconditionally, unlike resolving a concrete service type directly as the method's
    // own parameter - that exclusion didn't reliably happen for NpgsqlDataSource in
    // BinderConfigTool, so resolve DocsIndex by hand here too rather than depend on it).
    private static DocsIndex GetDocsIndex(IServiceProvider services) =>
        (DocsIndex)services.GetService(typeof(DocsIndex))!;

    private static string Snippet(string content, string query)
    {
        int index = content.IndexOf(query, StringComparison.OrdinalIgnoreCase);
        if (index < 0)
        {
            return content.Length <= 200 ? content : content[..200] + "...";
        }

        int start = Math.Max(0, index - 80);
        int length = Math.Min(200, content.Length - start);
        string snippet = content.Substring(start, length);
        return (start > 0 ? "..." : "") + snippet + (start + length < content.Length ? "..." : "");
    }
}
