using System.Text;

namespace CFCAgent.McpServer.Docs;

public sealed class DocSection
{
    public required string Doc { get; init; }
    public required string Title { get; init; }
    public required string Content { get; init; }
}

// Loads every .md file in a product's docs/ folder and splits each on its
// "## " headings into addressable sections, for search_docs/read_section.
// A doc with no "## " headings at all becomes a single section named after
// the file itself, so it's still searchable/readable rather than silently empty.
public sealed class DocsIndex
{
    private readonly List<DocSection> _sections;

    public DocsIndex(string docsDirectory)
    {
        _sections = [];

        if (!Directory.Exists(docsDirectory))
        {
            return;
        }

        foreach (string file in Directory.GetFiles(docsDirectory, "*.md"))
        {
            string docName = Path.GetFileNameWithoutExtension(file);
            _sections.AddRange(ParseSections(docName, File.ReadAllText(file)));
        }
    }

    public IReadOnlyList<DocSection> Sections => _sections;

    private static IEnumerable<DocSection> ParseSections(string docName, string content)
    {
        string[] lines = content.Replace("\r\n", "\n").Split('\n');
        string? currentTitle = null;
        StringBuilder currentBody = new();

        foreach (string line in lines)
        {
            if (line.StartsWith("## ", StringComparison.Ordinal))
            {
                if (currentTitle is not null)
                {
                    yield return new DocSection { Doc = docName, Title = currentTitle, Content = currentBody.ToString().Trim() };
                }

                currentTitle = line[3..].Trim();
                currentBody.Clear();
            }
            else if (line.StartsWith("# ", StringComparison.Ordinal))
            {
                // Top-level doc title - not a section, skip.
            }
            else if (currentTitle is not null)
            {
                currentBody.AppendLine(line);
            }
        }

        if (currentTitle is not null)
        {
            yield return new DocSection { Doc = docName, Title = currentTitle, Content = currentBody.ToString().Trim() };
        }
        else if (content.Trim().Length > 0)
        {
            yield return new DocSection { Doc = docName, Title = docName, Content = content.Trim() };
        }
    }
}
