using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Xunit;

namespace Cwi.Router.ArchTests;

public sealed class ConditionalComplexityTests
{
    [Fact]
    public void Handlers_ShouldAvoid_DeeplyNestedConditionals()
    {
        var handlersDir = FindHandlersDirectory();
        var files = Directory.GetFiles(handlersDir, "*.cs", SearchOption.TopDirectoryOnly);

        foreach (var file in files)
        {
            var text = File.ReadAllText(file);
            var tree = CSharpSyntaxTree.ParseText(text);
            var root = tree.GetRoot();
            var maxDepth = IfDepthCalculator.GetMaxDepth(root);

            Assert.True(
                maxDepth <= 3,
                $"{Path.GetFileName(file)} has nested if-depth {maxDepth}, expected <= 3.");
        }
    }

    private static string FindHandlersDirectory()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(
                dir.FullName,
                "src",
                "backend",
                "services",
                "cwi-router",
                "src",
                "Cwi.Router.Application",
                "Handlers");
            if (Directory.Exists(candidate))
            {
                return candidate;
            }

            dir = dir.Parent;
        }

        throw new DirectoryNotFoundException("Unable to locate router handlers directory for architecture tests.");
    }

    private sealed class IfDepthCalculator : CSharpSyntaxWalker
    {
        private int _currentDepth;
        private int _maxDepth;

        public static int GetMaxDepth(SyntaxNode node)
        {
            var walker = new IfDepthCalculator();
            walker.Visit(node);
            return walker._maxDepth;
        }

        public override void VisitIfStatement(IfStatementSyntax node)
        {
            _currentDepth++;
            _maxDepth = Math.Max(_maxDepth, _currentDepth);
            base.VisitIfStatement(node);
            _currentDepth--;
        }
    }
}
