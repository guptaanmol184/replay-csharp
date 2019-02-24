﻿using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Completion;
using Microsoft.CodeAnalysis.Text;
using System.Collections.Immutable;
using System.Threading.Tasks;

namespace Replay.Services
{
    // TODO: rework code completion based on
    // https://www.strathweb.com/2018/12/using-roslyn-c-completion-service-programmatically/
    // https://github.com/dotnet/roslyn/issues/24423
    // http://source.roslyn.io/#Microsoft.CodeAnalysis.InteractiveEditorFeatures/Extensibility/Interactive/InteractiveEvaluator.cs,387
    /// <summary>
    /// Provides code completion (i.e. Intellisense) using Roslyn
    /// </summary>
    class CodeCompleter
    {
        private Document document;

        public CodeCompleter(Document document)
        {
            this.document = document;
        }

        public async Task<ImmutableArray<CompletionItem>> Complete(string code)
        {
            string program = $"using System; namespace TestProject {{ public class CodeComplete {{ public void CodeCompleteMethod() {{ {code} }} }} }}";
            document = document.WithText(SourceText.From(program));
            int cursor = program.IndexOf(code) + code.Length;
            var service = CompletionService.GetService(document);
            var completions = await service.GetCompletionsAsync(document, cursor);
            if(completions == null)
            {
                return ImmutableArray.Create<CompletionItem>();
            }
            var filtered = service.FilterItems(document, completions.Items, code);
            return filtered;
        }
    }
}