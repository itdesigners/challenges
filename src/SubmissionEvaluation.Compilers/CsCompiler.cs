using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml;
using System.Xml.Linq;
using System.Xml.XPath;
using SubmissionEvaluation.Contracts.Data;
using SubmissionEvaluation.Contracts.Exceptions;
using SubmissionEvaluation.Contracts.Providers;

namespace SubmissionEvaluation.Compilers
{
    public class CsCompiler : CompilerBase
    {
        private readonly string dotnetPath;
        private readonly ILog log;

        public CsCompiler(ILog log, string dotnetPath)
        {
            this.log = log;
            this.dotnetPath = dotnetPath;
        }

        public override string Name => "C#";
        public override string Description => "";
        public override string LatexCodeExtension => ".cs";

        public override string ReadVersionDetails(IProcessProvider processProvider, ISyncLock versionlock)
        {
            var result = processProvider.Execute(dotnetPath, new[] {"--version"}, syncLock: versionlock);
            if (result.Result.ExitCode == 0)
            {
                return result.Result.Output;
            }

            log.Error($"Compiler nicht verfügbar. {result.Result.Output}(RC: {result.Result.ExitCode})");

            throw new CompilerException("Compiler nicht verfügbar.", Name);
        }

        public override IEnumerable<string> GetSourceFiles(IEnumerable<string> files)
        {
            return files.Where(x => (x.EndsWith(".cs", StringComparison.CurrentCultureIgnoreCase) ||
                                     x.EndsWith(".csproj", StringComparison.CurrentCultureIgnoreCase)) && !x.Contains("TemporaryGeneratedFile_"));
        }

        public override ExecutionParameters CompileContent(CompilerPaths paths, IProcessProvider processProvider, bool forceRecompile)
        {
            var compilableFiles = Directory.GetFiles(paths.Host.SourcePath, "*.cs", SearchOption.AllDirectories);
            log.Information("{count} C# Dateien zum kompelieren gefunden.", compilableFiles.Length);

            var projectFile = Directory.EnumerateFiles(paths.Host.SourcePath, "*.csproj", SearchOption.AllDirectories).FirstOrDefault();
            if (projectFile == null)
            {
                projectFile = Path.Combine(paths.Host.SourcePath, "submission.csproj");
                File.WriteAllText(projectFile,
                    "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>netcoreapp5.0</TargetFramework></PropertyGroup></Project>");
            }

            var document = XDocument.Load(projectFile);
            var namespaceManager = new XmlNamespaceManager(new NameTable());
            namespaceManager.AddNamespace("empty", "http://demo.com/2011/demo-schema");
            var execName = document.XPathSelectElement("/Project/PropertyGroup/OutputType/TargetFramework", namespaceManager)?.Value;
            if (execName == null)
            {
                execName = Path.GetFileNameWithoutExtension(projectFile);
            }

            var binaryPath = $"{execName}.dll";

            var binaryDate = File.GetLastWriteTime(Path.Combine(paths.Host.BinaryPath, binaryPath));
            if (!forceRecompile && compilableFiles.All(x => File.GetLastWriteTime(x) < binaryDate))
            {
                log.Information("Überspringe Kompilierung, da schon ein aktuelles Binary vorliegt");
            }
            else
            {
                CleanOutputBin(paths);
                ISyncLock compilelock = null;
                try
                {
                    compilelock = processProvider.GetLock(new[]
                    {
                        new FolderMapping {Source = paths.Host.SourcePath, Target = paths.Sandbox.SourcePath, ReadOnly = true},
                        new FolderMapping {Source = paths.Host.BinaryPath, Target = paths.Sandbox.BinaryPath}
                    });

                    var result = processProvider.Execute(dotnetPath, new[] {"publish", "-o", paths.Sandbox.BinaryPath, Path.GetFileName(projectFile)},
                        workingDir: paths.Sandbox.SourcePath, syncLock: compilelock).Result;
                    if (result.Timeout)
                    {
                        throw new CompilerException("Timeout während Kompilierevorgang", result, Name);
                    }

                    if (result.ExitCode != 0)
                    {
                        throw new CompilerException("Kompiliervorgang fehlgeschlagen", result, Name);
                    }
                }
                finally
                {
                    processProvider.ReleaseLock(compilelock);
                }
            }

            return new ExecutionParameters
            {
                Path = "dotnet",
                WorkingDirectory = paths.Sandbox.BinaryPath,
                Arguments = new[] {binaryPath},
                Language = Name,
                OutputEncoding = "UTF8"
            };
        }
    }
}
