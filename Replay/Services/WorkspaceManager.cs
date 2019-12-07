﻿using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Formatting;
using Microsoft.CodeAnalysis.Host.Mef;
using Microsoft.CodeAnalysis.Text;
using Replay.Model;
using Replay.Services.AssemblyLoading;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Replay.Services
{
    /// <summary>
    /// Some Roslyn APIs like code completion and syntax highligher operate on the
    /// Document/Project/Workspace object model. This class keeps track of the state
    /// of the object model for a single REPL window, so we can interact with those APIs.
    /// </summary>
    class WorkspaceManager
    {
        private readonly IDictionary<int, ReplSubmission> EditorToSubmission = new ConcurrentDictionary<int, ReplSubmission>();
        private readonly AdhocWorkspace workspace;
        private readonly CSharpCompilationOptions compilationOptions;
        private readonly DefaultAssemblies defaultAssemblies;

        public WorkspaceManager(DefaultAssemblies defaultAssemblies)
        {
            var host = MefHostServices.Create(MefHostServices.DefaultAssemblies);
            this.workspace = new AdhocWorkspace(host);
            this.defaultAssemblies = defaultAssemblies;
            this.compilationOptions = new CSharpCompilationOptions(
               OutputKind.DynamicallyLinkedLibrary,
               usings: defaultAssemblies.DefaultUsings
            );
        }

        public ReplSubmission CreateOrUpdateSubmission(
            int lineId,
            string code,
            params MetadataReference[] assemblyReferences)
        {
            var replSubmission = EditorToSubmission.TryGetValue(lineId, out var previousSubmission)
                ? UpdateSubmission(previousSubmission, code, assemblyReferences)
                : CreateSubmission(lineId, code, assemblyReferences);

            EditorToSubmission[lineId] = replSubmission;
            return replSubmission;
        }

        private ReplSubmission CreateSubmission(int lineId, string code, MetadataReference[] assemblyReferences)
        {
            var name = "Script" + lineId;
            // we add the previous REPL submission as a project reference, so
            // APIs like Code Completion know about them.
            var projectReferences = GetPreviousSubmission(lineId);
            Project project = CreateProject(name, projectReferences, assemblyReferences);
            Document document = CreateDocument(project, name, code);

            return new ReplSubmission
            {
                Document = document,
                Code = code
            };
        }

        private Project CreateProject(string name, ProjectReference[] previousSubmission, MetadataReference[] assemblyReferences)
        {
            var projectInfo = ProjectInfo
                .Create(
                    id: ProjectId.CreateNewId(),
                    version: VersionStamp.Create(),
                    name: name,
                    assemblyName: name,
                    language: LanguageNames.CSharp,
                    isSubmission: true
                )
                .WithProjectReferences(previousSubmission)
                .WithMetadataReferences(defaultAssemblies.Assemblies.Value.Concat(assemblyReferences))
                .WithCompilationOptions(compilationOptions);
            var project = workspace.AddProject(projectInfo);
            return project;
        }

        private Document CreateDocument(Project project, string name, string code)
        {
            var documentInfo = DocumentInfo.Create(
                id: DocumentId.CreateNewId(project.Id),
                name: name,
                sourceCodeKind: SourceCodeKind.Script,
                loader: TextLoader.From(TextAndVersion.Create(SourceText.From(code), VersionStamp.Create())));
            var document = workspace.AddDocument(documentInfo);
            return document;
        }

        private ProjectReference[] GetPreviousSubmission(int lineId)
        {
            return lineId > 1
                ? new[] { new ProjectReference(EditorToSubmission[lineId - 1].Document.Project.Id) }
                : Array.Empty<ProjectReference>();
        }

        private ReplSubmission UpdateSubmission(
            ReplSubmission replSubmission,
            string code,
            IReadOnlyCollection<MetadataReference> references)
        {
            var edit = workspace.CurrentSolution
                .WithDocumentText(replSubmission.Document.Id, SourceText.From(code))
                .AddMetadataReferences(replSubmission.Document.Project.Id, references);

            var success = workspace.TryApplyChanges(edit);

            // document has changed, requery to get the new one
            var document = workspace.CurrentSolution.GetDocument(replSubmission.Document.Id);

            return new ReplSubmission
            {
                Code = code,
                Document = document
            };
        }
    }
}
