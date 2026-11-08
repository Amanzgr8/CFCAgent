using CFCAgent.Backend;
using Microsoft.Extensions.AI;
using ModelContextProtocol.Protocol;

namespace CFCAgent.Backend.Tests;

public class ToolFilterTests
{
    [Fact]
    public void IsDeclaredReadOnly_TrueOnlyWhenReadOnlyHintIsTrue()
    {
        Tool readOnly = new Tool { Name = "a", Annotations = new ToolAnnotations { ReadOnlyHint = true } };
        Tool notReadOnly = new Tool { Name = "b", Annotations = new ToolAnnotations { ReadOnlyHint = false } };
        Tool hintMissing = new Tool { Name = "c", Annotations = new ToolAnnotations() };
        Tool noAnnotations = new Tool { Name = "d" };

        Assert.True(ToolFilter.IsDeclaredReadOnly(readOnly));
        Assert.False(ToolFilter.IsDeclaredReadOnly(notReadOnly));
        Assert.False(ToolFilter.IsDeclaredReadOnly(hintMissing));
        Assert.False(ToolFilter.IsDeclaredReadOnly(noAnnotations));
    }

    [Fact]
    public void ReadOnlyTools_DropsToolsThatAreNotMcpTools()
    {
        AITool plain = AIFunctionFactory.Create(() => 1, "plain_tool");

        IReadOnlyList<AITool> kept = ToolFilter.ReadOnlyTools(new[] { plain });

        Assert.Empty(kept);
    }
}
