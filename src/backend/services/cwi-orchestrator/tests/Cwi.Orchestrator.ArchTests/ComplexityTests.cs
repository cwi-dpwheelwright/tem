using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Xunit;

namespace Cwi.Orchestrator.ArchTests;

public sealed class ComplexityTests
{
    private const int MaxCyclomaticComplexity = 30;
    private const int MaxNestingDepth = 5;

    [Fact]
    public void Methods_ShouldStayWithin_ComplexityAndNestingLimits()
    {
        var sourceRoot = FindSourceRoot("Cwi.Orchestrator.Api");
        var failures = new List<string>();

        foreach (var file in EnumerateSourceFiles(sourceRoot))
        {
            var text = File.ReadAllText(file);
            var tree = CSharpSyntaxTree.ParseText(text);
            var root = tree.GetRoot();

            foreach (var method in root.DescendantNodes().OfType<BaseMethodDeclarationSyntax>())
            {
                var walker = new ComplexityWalker();
                walker.Visit(method);

                if (walker.CyclomaticComplexity > MaxCyclomaticComplexity || walker.MaxNestingDepth > MaxNestingDepth)
                {
                    failures.Add($"{file}: {GetMethodName(method)} (CC={walker.CyclomaticComplexity}, Nesting={walker.MaxNestingDepth})");
                }
            }

            foreach (var localFunction in root.DescendantNodes().OfType<LocalFunctionStatementSyntax>())
            {
                var walker = new ComplexityWalker();
                walker.Visit(localFunction);

                if (walker.CyclomaticComplexity > MaxCyclomaticComplexity || walker.MaxNestingDepth > MaxNestingDepth)
                {
                    failures.Add($"{file}: {localFunction.Identifier.Text} (CC={walker.CyclomaticComplexity}, Nesting={walker.MaxNestingDepth})");
                }
            }
        }

        Assert.True(failures.Count == 0, "Complexity limits exceeded:\n" + string.Join("\n", failures));
    }

    private static string FindSourceRoot(string markerProject)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            var candidate = Path.Combine(dir.FullName, "src");
            if (Directory.Exists(Path.Combine(candidate, markerProject)))
            {
                return candidate;
            }

            dir = dir.Parent;
        }

        throw new InvalidOperationException("Unable to locate source root.");
    }

    private static IEnumerable<string> EnumerateSourceFiles(string root)
    {
        return Directory
            .EnumerateFiles(root, "*.cs", SearchOption.AllDirectories)
            .Where(path => !path.Contains("\\bin\\", StringComparison.OrdinalIgnoreCase))
            .Where(path => !path.Contains("\\obj\\", StringComparison.OrdinalIgnoreCase))
            .Where(path => !path.EndsWith(".g.cs", StringComparison.OrdinalIgnoreCase))
            .Where(path => !path.EndsWith(".designer.cs", StringComparison.OrdinalIgnoreCase));
    }

    private static string GetMethodName(BaseMethodDeclarationSyntax method)
    {
        return method switch
        {
            MethodDeclarationSyntax m => m.Identifier.Text,
            ConstructorDeclarationSyntax c => c.Identifier.Text,
            DestructorDeclarationSyntax d => d.Identifier.Text,
            OperatorDeclarationSyntax o => o.OperatorToken.Text,
            ConversionOperatorDeclarationSyntax c => c.Type.ToString(),
            _ => method.Kind().ToString()
        };
    }

    private sealed class ComplexityWalker : CSharpSyntaxWalker
    {
        private int _currentNesting;

        public int CyclomaticComplexity { get; private set; } = 1;
        public int MaxNestingDepth { get; private set; }

        public override void VisitIfStatement(IfStatementSyntax node)
        {
            CyclomaticComplexity++;
            EnterNesting(() => base.VisitIfStatement(node));
        }

        public override void VisitForStatement(ForStatementSyntax node)
        {
            CyclomaticComplexity++;
            EnterNesting(() => base.VisitForStatement(node));
        }

        public override void VisitForEachStatement(ForEachStatementSyntax node)
        {
            CyclomaticComplexity++;
            EnterNesting(() => base.VisitForEachStatement(node));
        }

        public override void VisitForEachVariableStatement(ForEachVariableStatementSyntax node)
        {
            CyclomaticComplexity++;
            EnterNesting(() => base.VisitForEachVariableStatement(node));
        }

        public override void VisitWhileStatement(WhileStatementSyntax node)
        {
            CyclomaticComplexity++;
            EnterNesting(() => base.VisitWhileStatement(node));
        }

        public override void VisitDoStatement(DoStatementSyntax node)
        {
            CyclomaticComplexity++;
            EnterNesting(() => base.VisitDoStatement(node));
        }

        public override void VisitSwitchStatement(SwitchStatementSyntax node)
        {
            EnterNesting(() => base.VisitSwitchStatement(node));
        }

        public override void VisitSwitchSection(SwitchSectionSyntax node)
        {
            CyclomaticComplexity += node.Labels.Count;
            base.VisitSwitchSection(node);
        }

        public override void VisitCatchClause(CatchClauseSyntax node)
        {
            CyclomaticComplexity++;
            EnterNesting(() => base.VisitCatchClause(node));
        }

        public override void VisitConditionalExpression(ConditionalExpressionSyntax node)
        {
            CyclomaticComplexity++;
            base.VisitConditionalExpression(node);
        }

        public override void VisitBinaryExpression(BinaryExpressionSyntax node)
        {
            if (node.IsKind(SyntaxKind.LogicalAndExpression) || node.IsKind(SyntaxKind.LogicalOrExpression))
            {
                CyclomaticComplexity++;
            }

            base.VisitBinaryExpression(node);
        }

        public override void VisitUsingStatement(UsingStatementSyntax node)
        {
            EnterNesting(() => base.VisitUsingStatement(node));
        }

        public override void VisitLockStatement(LockStatementSyntax node)
        {
            EnterNesting(() => base.VisitLockStatement(node));
        }

        public override void VisitTryStatement(TryStatementSyntax node)
        {
            EnterNesting(() => base.VisitTryStatement(node));
        }

        private void EnterNesting(Action visit)
        {
            _currentNesting++;
            MaxNestingDepth = Math.Max(MaxNestingDepth, _currentNesting);
            visit();
            _currentNesting--;
        }
    }
}
