using KanbanBoard.Api.Middleware;
using KanbanBoard.Api.Services;

namespace KanbanBoard.Tests.Middleware;

public class AgentAuthUnitTests
{
    [Theory]
    [InlineData("abc", "abc", true)]
    [InlineData("abc", "abd", false)]
    [InlineData("abc", "abcd", false)]
    [InlineData("", "abc", false)]
    [InlineData("ABC", "abc", false)]
    public void KeysMatch_IsExactAndCaseSensitive(string supplied, string expected, bool match)
    {
        Assert.Equal(match, ApiKeyAuthenticationMiddleware.KeysMatch(supplied, expected));
    }

    [Theory]
    [InlineData("Claude-Code-Agent-v1")]
    [InlineData("Codex-Fixer")]
    [InlineData("codex/fixer@2.1 (ci)")]
    [InlineData("  padded  ")]
    public void Validate_AcceptsRealisticIdentities(string identity)
    {
        Assert.Null(AgentIdentityMiddleware.Validate(identity, 100, out var cleaned));
        Assert.Equal(identity.Trim(), cleaned);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("bad\u0000name")]
    [InlineData("<img src=x>")]
    [InlineData("name\"quote")]
    public void Validate_RejectsUnsafeIdentities(string? identity)
    {
        Assert.NotNull(AgentIdentityMiddleware.Validate(identity, 100, out _));
    }

    [Fact]
    public void Options_RequireMinimumKeyLength()
    {
        Assert.False(new AiAgentApiOptions { ApiKey = "" }.IsConfigured);
        Assert.False(new AiAgentApiOptions { ApiKey = "123456789012345" }.IsConfigured);
        Assert.True(new AiAgentApiOptions { ApiKey = "1234567890123456" }.IsConfigured);
    }

    [Fact]
    public void ActorContext_SetAiAgent_UsesIdentityAsDisplayName()
    {
        var actor = new ActorContext();
        actor.SetAiAgent("Bot-9");
        Assert.True(actor.IsAi);
        Assert.Equal("Bot-9", actor.DisplayName);

        actor.SetHuman(null);
        Assert.False(actor.IsAi);
        Assert.Null(actor.AgentIdentity);
        Assert.Equal(ActorContext.DefaultHumanName, actor.DisplayName);
    }
}
