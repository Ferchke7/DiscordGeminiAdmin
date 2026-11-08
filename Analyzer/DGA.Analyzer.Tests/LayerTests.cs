using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using Xunit;

namespace DGA.Analyzer.Tests;

public class LayerTests
{
    [Fact]
    public Task FolderRule_Blocks_Target_Namespace()
    {
        var source = @"
namespace Domain.Models
{
    using Infrastructure.Services;

    public class MyModel
    {
        private readonly SomeService _service;
    }
}

namespace Infrastructure.Services
{
    public class SomeService { }
}
";

        var test = new CSharpAnalyzerTest<CleanArchitectureDependenciesAnalyzer, DefaultVerifier>
        {
            TestCode = source
        };

        test.TestState.AnalyzerConfigFiles.Add(("/.editorconfig", @"
root = true
[*.cs]
dotnet_code_quality.DGA0001.Domain = Application, Infrastructure
"));

        var expectedError = new DiagnosticResult("DGA0001", DiagnosticSeverity.Error);
        test.ExpectedDiagnostics.Add(expectedError.WithLocation(4, 11));
        test.ExpectedDiagnostics.Add(expectedError.WithLocation(4, 26));
        test.ExpectedDiagnostics.Add(expectedError.WithLocation(8, 26));

        return test.RunAsync();
    }

    [Fact]
    public Task FolderRule_Supports_Multiple_Comma_Separated_Exclusions()
    {
        var source = @"
namespace Infrastructure.Layer
{
    using {|#0:Api|};
    using {|#1:Controller|};

    public class MyService { }
}

namespace Api
{
    public class Endpoint { }
}

namespace Controller
{
    public class Endpoint { }
}
";

        var test = new CSharpAnalyzerTest<CleanArchitectureDependenciesAnalyzer, DefaultVerifier>
        {
            TestCode = source
        };

        test.TestState.AnalyzerConfigFiles.Add(("/.editorconfig", @"
root = true
[*.cs]
dotnet_code_quality.DGA0001.Infrastructure = Api, Controller
"));

        var expectedError = new DiagnosticResult("DGA0001", DiagnosticSeverity.Error)
            .WithLocation(0);
        var expectedError2 = new DiagnosticResult("DGA0001", DiagnosticSeverity.Error)
            .WithLocation(1);

        test.ExpectedDiagnostics.Add(expectedError);
        test.ExpectedDiagnostics.Add(expectedError2);

        return test.RunAsync();
    }

    [Fact]
    public Task FolderRule_DoesNotMatch_Substring()
    {
        var source = @"
namespace Infrastructure.OpenApi
{
    using Microsoft.OpenApi;

    public class MyConfigurator
    {
        public Info GetInfo() => new Info();
    }
}

namespace Microsoft.OpenApi
{
    public class Info { }
}
";

        var test = new CSharpAnalyzerTest<CleanArchitectureDependenciesAnalyzer, DefaultVerifier>
        {
            TestCode = source
        };

        test.TestState.AnalyzerConfigFiles.Add(("/.editorconfig", @"
root = true
[*.cs]
dotnet_code_quality.DGA0001.Infrastructure = Api
"));

        return test.RunAsync();
    }
}
