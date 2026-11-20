using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Xunit;

namespace Cwi.Coordination.ArchTests;

public sealed class ComplexityTests
{
    private const int MaxIfDepth = 3;
    private const int MaxCyclomaticComplexity = 12;

    [Fact]
    public void Application_ShouldAvoid_DeeplyNested_Conditionals()
    {
        foreach (var file in EnumerateTargetFiles())
        {
            var root = Parse(file);
            var maxDepth = IfDepthCalculator.GetMaxDepth(root);

            Assert.True(
                maxDepth <= MaxIfDepth,
                $"{Path.GetFileName(file)} has nested if-depth {maxDepth}, expected <= {MaxIfDepth}.");
        }
    }

    [Fact]
    public void Application_ShouldLimit_CyclomaticComplexity()
    {
        var violations = new List<string>();

        foreach (var file in EnumerateTargetFiles())
        {
            var root = Parse(file);
            foreach (var violation in CyclomaticComplexityCalculator.FindViolations(root, MaxCyclomaticComplexity))
            {
                violations.Add($"{Path.GetFileName(file)}::{violation.Member} complexity {violation.Complexity} > {MaxCyclomaticComplexity}");
            }
        }

        Assert.True(violations.Count == 0, string.Join(Environment.NewLine, violations));
    }

    private static SyntaxNode Parse(string file)
    {
        var text = File.ReadAllText(file);
        var tree = CSharpSyntaxTree.ParseText(text);
        return tree.GetRoot();
    }

    private static IEnumerable<string> EnumerateTargetFiles()
    {
        var targets = new[]
        {
            new[] { "src", "backend", "services", "cwi-coordination-layer", "src", "Cwi.Coordination.Application", "Services" },
            new[] { "src", "backend", "services", "cwi-coordination-layer", "src", "Cwi.Coordination.Application", "Pipeline" }
        };

        foreach (var parts in targets)
        {
            var directory = FindDirectory(parts);
            foreach (var file in Directory.GetFiles(directory, "*.cs", SearchOption.AllDirectories))
            {
                yield return file;
            }
        }
    }

    private static string FindDirectory(string[] parts)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(new[] { dir.FullName }.Concat(parts).ToArray());
            if (Directory.Exists(candidate))
            {
                return candidate;
            }

            dir = dir.Parent;
        }

        throw new DirectoryNotFoundException($"Unable to locate coordination-layer directory for {string.Join(Path.DirectorySeparatorChar, parts)}.");
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

    private sealed class CyclomaticComplexityCalculator : CSharpSyntaxWalker
    {
        private int _decisionPoints;

        public static IReadOnlyList<(string Member, int Complexity)> FindViolations(SyntaxNode root, int maxComplexity)
        {
            var violations = new List<(string Member, int Complexity)>();

            foreach (var method in root.DescendantNodes().OfType<BaseMethodDeclarationSyntax>())
            {
                var body = GetBody(method);
                if (body is null)
                {
                    continue;
                }

                var complexity = Compute(body);
                if (complexity > maxComplexity)
                {
                    violations.Add((DescribeMember(method), complexity));
                }
            }

            foreach (var accessor in root.DescendantNodes().OfType<AccessorDeclarationSyntax>())
            {
                var body = GetBody(accessor);
                if (body is null)
                {
                    continue;
                }

                var complexity = Compute(body);
                if (complexity > maxComplexity)
                {
                    violations.Add((DescribeMember(accessor), complexity));
                }
            }

            foreach (var localFunction in root.DescendantNodes().OfType<LocalFunctionStatementSyntax>())
            {
                var body = (SyntaxNode?)localFunction.Body ?? localFunction.ExpressionBody;
                if (body is null)
                {
                    continue;
                }

                var complexity = Compute(body);
                if (complexity > maxComplexity)
                {
                    violations.Add((DescribeMember(localFunction), complexity));
                }
            }

            return violations;
        }

        private static SyntaxNode? GetBody(BaseMethodDeclarationSyntax method)
        {
            return method switch
            {
                MethodDeclarationSyntax m => (SyntaxNode?)m.Body ?? m.ExpressionBody,
                ConstructorDeclarationSyntax c => (SyntaxNode?)c.Body ?? c.ExpressionBody,
                _ => method.Body
            };
        }

        private static SyntaxNode? GetBody(AccessorDeclarationSyntax accessor) =>
            (SyntaxNode?)accessor.Body ?? accessor.ExpressionBody;

        private static int Compute(SyntaxNode node)
        {
            var walker = new CyclomaticComplexityCalculator();
            walker.Visit(node);
            return 1 + walker._decisionPoints;
        }

        public override void VisitIfStatement(IfStatementSyntax node)
        {
            _decisionPoints++;
            base.VisitIfStatement(node);
        }

        public override void VisitForStatement(ForStatementSyntax node)
        {
            _decisionPoints++;
            base.VisitForStatement(node);
        }

        public override void VisitForEachStatement(ForEachStatementSyntax node)
        {
            _decisionPoints++;
            base.VisitForEachStatement(node);
        }

        public override void VisitWhileStatement(WhileStatementSyntax node)
        {
            _decisionPoints++;
            base.VisitWhileStatement(node);
        }

        public override void VisitDoStatement(DoStatementSyntax node)
        {
            _decisionPoints++;
            base.VisitDoStatement(node);
        }

        public override void VisitCatchClause(CatchClauseSyntax node)
        {
            _decisionPoints++;
            base.VisitCatchClause(node);
        }

        public override void VisitConditionalExpression(ConditionalExpressionSyntax node)
        {
            _decisionPoints++;
            base.VisitConditionalExpression(node);
        }

        public override void VisitSwitchExpressionArm(SwitchExpressionArmSyntax node)
        {
            _decisionPoints++;
            base.VisitSwitchExpressionArm(node);
        }

        public override void VisitCaseSwitchLabel(CaseSwitchLabelSyntax node)
        {
            _decisionPoints++;
            base.VisitCaseSwitchLabel(node);
        }

        public override void VisitBinaryExpression(BinaryExpressionSyntax node)
        {
            if (node.IsKind(SyntaxKind.LogicalAndExpression) || node.IsKind(SyntaxKind.LogicalOrExpression))
            {
                _decisionPoints++;
            }
            base.VisitBinaryExpression(node);
        }

        public override void VisitLocalFunctionStatement(LocalFunctionStatementSyntax node)
        {
            // Local functions are evaluated separately.
        }

        private static string DescribeMember(SyntaxNode node)
        {
            return node switch
            {
                MethodDeclarationSyntax m => m.Identifier.Text,
                ConstructorDeclarationSyntax c => c.Identifier.Text,
                LocalFunctionStatementSyntax l => l.Identifier.Text,
                AccessorDeclarationSyntax a => $"{GetAccessorOwner(a)}.{a.Keyword.Text}",
                _ => node.Kind().ToString()
            };
        }

        private static string GetAccessorOwner(AccessorDeclarationSyntax accessor)
        {
            var owner = accessor.Parent?.Parent;
            return owner switch
            {
                PropertyDeclarationSyntax p => p.Identifier.Text,
                IndexerDeclarationSyntax => "this[]",
                EventDeclarationSyntax e => e.Identifier.Text,
                _ => "accessor"
            };
        }
    }
}
