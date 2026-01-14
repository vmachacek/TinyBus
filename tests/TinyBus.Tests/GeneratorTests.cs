using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Xunit.Abstractions;

namespace TinyBus.Tests;

using System.Reflection;
using System.Text;
using System.Text.Json.Serialization;
using Basic.Reference.Assemblies;
using TinyBus.Generator;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Newtonsoft.Json.Linq;
using Shouldly;

public class GeneratorTests(ITestOutputHelper helper)
{
    [Theory]
    [InlineData("Generate_source_for_pub_sub.cs", typeof(PubSubGenerator))]
    public async Task RunGenerator(string sampleSourceCodeFile, Type s)
    {
        var generator = Activator.CreateInstance(s) as IIncrementalGenerator ?? throw new Exception("Wrong type");
        var (output, errors) = GenerateAndLogCompilationResults(sampleSourceCodeFile, generator);
        errors.ShouldBeEmpty();
        helper.WriteLine(output);
    }

    private (string Output, Diagnostic[] Errors) GenerateAndLogCompilationResults(
        string inputCodeFileName,
        IIncrementalGenerator generator,
        Action<CSharpGeneratorDriver>? update = null)
    {
        var sb = new StringBuilder();

        var headSyntaxTree = CSharpSyntaxTree.ParseText(LoadFile(inputCodeFileName), path: "head.cs");

        var comp = CreateCompilation(headSyntaxTree);

        var cSharpGeneratorDriver = CSharpGeneratorDriver.Create(generator);

        update?.Invoke(cSharpGeneratorDriver);

        var driver = cSharpGeneratorDriver
            .RunGeneratorsAndUpdateCompilation(comp, out var outputCompilation, out var diagnostics1);

        var problems = diagnostics1.Where(f => f.WarningLevel > 1);

        problems.ShouldBeEmpty();

        var runResult = driver.GetRunResult();
        var generatedSources = runResult.Results.SelectMany(result => result.GeneratedSources);

        var syntaxTrees = new List<SyntaxTree> { headSyntaxTree };

        syntaxTrees.AddRange(generatedSources.Select(source => source.SyntaxTree));

        var withGenerator = runResult.Results.SelectMany(result => result.GeneratedSources.Select(generatedSourceResult => generatedSourceResult.SyntaxTree));

        var fullCompilation = CreateCompilation([headSyntaxTree, ..withGenerator]);

        var diagnostics = fullCompilation.GetDiagnostics();

        var errors = diagnostics.Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);

        if (errors.Any())
        {
            foreach (var error in errors.DistinctBy(f => f.Id))
            {
                sb.AppendLine($"{error.Id}: {error.GetMessage()}");
                sb.AppendLine($"Location: {error.Location.GetLineSpan().Path} ({error.Location.GetLineSpan().StartLinePosition.Line + 1},{error.Location.GetLineSpan().StartLinePosition.Character + 1})");
            }
        }
        else
        {
            sb.AppendLine("No compilation errors found.");
        }

        fullCompilation.Emit(Stream.Null);

        foreach (var result in runResult.Results)
        {
            foreach (var resultGeneratedSource in result.GeneratedSources.Reverse())
            {
                sb.AppendLine(resultGeneratedSource.SyntaxTree.FilePath);
                var message = resultGeneratedSource.SourceText.ToString();

                // var workingDirectory = @"C:\temp\out-cs\";
                // Directory.CreateDirectory(workingDirectory);
                // File.WriteAllText(Path.Combine(workingDirectory, Path.GetFileName(resultGeneratedSource.SyntaxTree.FilePath)), message);

                sb.AppendLine(message);
            }
        }

        foreach (var resultDiagnostic in runResult.Diagnostics)
        {
            sb.AppendLine(resultDiagnostic.ToString());
        }

        var generatorResult = runResult.Results[0];
        if (generatorResult.Exception is not null)
        {
            sb.AppendLine(generatorResult.Exception.Message);
            sb.AppendLine(generatorResult.Exception.ToString());
        }

        return (sb.ToString(), errors.ToArray());
    }

    private static Compilation CreateCompilation(params SyntaxTree[] source)
    {
        var references = new List<MetadataReference>
        {
            MetadataReference.CreateFromFile(typeof(JsonIgnoreAttribute).GetTypeInfo().Assembly.Location),
            MetadataReference.CreateFromFile(typeof(JObject).GetTypeInfo().Assembly.Location),
            MetadataReference.CreateFromFile(typeof(IServiceCollection).GetTypeInfo().Assembly.Location),
        };

        references.AddRange(ReferenceAssemblies.NetStandard20);
        return CSharpCompilation.Create("compilation", source, references, new CSharpCompilationOptions(OutputKind.ConsoleApplication));
    }

    private string LoadFile(string fileName)
    {
        return File.ReadAllText(Path.Combine("SampleCode", fileName));
    }
}