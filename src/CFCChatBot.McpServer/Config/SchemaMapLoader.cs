using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace CFCChatBot.McpServer.Config;

public static class SchemaMapLoader
{
    public static ProductConfig Load(string path)
    {
        if (!File.Exists(path))
        {
            throw new FileNotFoundException($"Product schema map not found: {path}", path);
        }

        IDeserializer deserializer = new DeserializerBuilder()
            .WithNamingConvention(UnderscoredNamingConvention.Instance)
            .IgnoreUnmatchedProperties()
            .Build();

        string yaml = File.ReadAllText(path);
        return deserializer.Deserialize<ProductConfig>(yaml)
            ?? throw new InvalidOperationException($"Schema map is empty or invalid: {path}");
    }
}
