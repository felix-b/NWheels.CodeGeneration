﻿using System.Linq;
using System.Threading;
using MetaPrograms.CodeModel.Imperative;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace MetaPrograms.Adapters.Roslyn.Reader
{
    public class ProjectReader
    {
        private readonly CodeModelBuilder _modelBuilder;
        private readonly Workspace _workspace;
        private readonly Project _project;
        private readonly Compilation _compilation;
        private readonly SyntaxTree[] _syntaxTrees;

        public ProjectReader(CodeModelBuilder modelBuilder, Workspace workspace, Project project)
        {
            _modelBuilder = modelBuilder;
            _workspace = workspace;
            _project = project;
            _compilation = project.GetCompilationAsync(CancellationToken.None).Result;
            _syntaxTrees = _compilation.SyntaxTrees.ToArray();
        }

        public void Read()
        {
            foreach (var tree in _syntaxTrees)
            {
                var semanticModel = _compilation.GetSemanticModel(tree);

                var topLevelClasses = tree.GetCompilationUnitRoot()
                    .DescendantNodes(descendIntoChildren: MayContainTopLevelClasses)
                    .OfType<ClassDeclarationSyntax>()
                    .ToList();

                topLevelClasses.ForEach(classSyntax => ReadTopLevelClass(semanticModel, classSyntax));
            }

            bool MayContainTopLevelClasses(SyntaxNode node)
            {
                return (node is CompilationUnitSyntax || node is NamespaceDeclarationSyntax);
            }
        }

        private void ReadTopLevelClass(SemanticModel semanticModel, ClassDeclarationSyntax classSyntax)
        {
            var classReader = new ClassReader(_modelBuilder, semanticModel, classSyntax);
            classReader.Read();
        }
    }
}