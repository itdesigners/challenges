using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using SubmissionEvaluation.Contracts.Data;
using SubmissionEvaluation.Contracts.Exceptions;
using SubmissionEvaluation.Contracts.Providers;

namespace SubmissionEvaluation.Domain.Operations
{
    internal class CompilerOperations
    {
        public IList<ICompiler> Compilers { private get; set; }
        public ProviderStore ProviderStore { set; internal get; }


        public (ExecutionParameters, SizeInfo, string binaryPath) CompileSubmission(string pathToZip, ICompilerProperties challenge, bool forceRecompile)
        {
            var submissionSourcePath = ProviderStore.FileProvider.ExtractContent(pathToZip);
            var compiler = GetCompilerForContent(submissionSourcePath);
            forceRecompile |= HasCompilerChanged(pathToZip, compiler);

            var compileResult = CompileContent(compiler, submissionSourcePath, forceRecompile);
            EnsureLanguageSupported(challenge.Languages, compileResult.Item1.Language);
            return (compileResult.Item1, compileResult.Item2, ProviderStore.FileProvider.GetBuildPathForSubmissionSource(submissionSourcePath));
        }

        private (ExecutionParameters, SizeInfo) CompileContent(ICompiler compiler, string submissionSourcePath, bool forceRecompile)
        {
            var targetPath = ProviderStore.FileProvider.GetBuildPathForSubmissionSource(submissionSourcePath);
            var paths = new CompilerPaths();
            paths.Host.BinaryPath = targetPath;
            paths.Host.SourcePath = submissionSourcePath;
            paths.Sandbox.BinaryPath = "/testrun/bin";
            paths.Sandbox.SourcePath = "/testrun/src";
            var execParams = compiler.CompileContent(paths, ProviderStore.SandboxedProcessProvider, forceRecompile);
            execParams.CompilerVersion = compiler.Version;
            var sizeInfo = compiler.DetermineContentSize(submissionSourcePath);
            return (execParams, sizeInfo);
        }

        private static void EnsureLanguageSupported(IList<string> supportedLanguages, string language)
        {
            if (supportedLanguages != null && supportedLanguages.Count > 0 && !supportedLanguages.Contains(language))
            {
                throw new LanguageNotAllowedException($"Programmiersprache {language} ist bei dieser Aufgabe nicht erlaubt.", language);
            }
        }

        public ICompiler GetCompilerForFiles(IEnumerable<string> files)
        {
            var foundCompilers = Compilers.Where(x => x.Available && x.CanCompileContent(files)).ToList();
            if (foundCompilers.Count == 0)
            {
                throw new CompilerException("Kein passender Compiler gefunden", "-");
            }

            if (foundCompilers.Count > 1)
            {
                throw new CompilerException("Mehr als ein Compiler gefunden", "-");
            }

            return foundCompilers.First();
        }

        internal ICompiler GetCompilerForContent(string pathToContent)
        {
            var compilers = Compilers.Where(x => x.Available && x.CanCompileContent(pathToContent));
            var compiler = compilers.SingleOrDefault();
            if (compiler == null)
            {
                throw new CompilerException("Kein passender Compiler gefunden", "-");
            }

            return compiler;
        }

        internal ICompiler GetCompilerForContent(IEnumerable<string> files)
        {
            var compilers = Compilers.Where(x => x.Available && x.CanCompileContent(files));
            var compiler = compilers.SingleOrDefault();
            if (compiler == null)
            {
                throw new CompilerException("Kein passender Compiler gefunden", "-");
            }

            return compiler;
        }

        private bool HasCompilerChanged(string pathToZip, ICompiler compiler)
        {
            var submission = Path.GetDirectoryName(pathToZip);
            try
            {
                var result = ProviderStore.FileProvider.LoadResult(submission);
                return result?.CompilerVersion != compiler.Version;
            }
            catch
            {
                return true;
            }
        }

        internal static string GetCompilerVersion(ICompiler compiler)
        {
            using var md5 = new MD5CryptoServiceProvider();
            var md5Sum = md5.ComputeHash(Encoding.UTF32.GetBytes(compiler.VersionDetails));
            return BitConverter.ToString(md5Sum).Replace("-", "");
        }
    }
}
