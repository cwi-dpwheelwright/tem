using Cwi.Orchestrator.Application.Ask;
using FluentAssertions;
using Xunit;

namespace Cwi.Orchestrator.UnitTests.Ask;

public sealed class AskQuestionScopeTests
{
    [Fact]
    public void ExtractPrimaryQuestion_uses_last_user_turn_when_prompt_is_transcript()
    {
        var prompt = """
            User: What is CWI?
            Assistant: CWI stands for Christian Wheelwright Intelligence.
            User: yo this is broken
            """;

        var result = AskQuestionScope.ExtractPrimaryQuestion(prompt);

        result.Should().Be("yo this is broken");
    }

    [Fact]
    public void ExtractPrimaryQuestion_uses_current_question_label_over_previous_context()
    {
        var prompt = """
            Recent conversation:
            User: What is CWI?
            Assistant: CWI stands for Christian Wheelwright Intelligence.

            Current question:
            Summarize the impact of this system.
            """;

        var result = AskQuestionScope.ExtractPrimaryQuestion(prompt);

        result.Should().Be("Summarize the impact of this system.");
    }
}
