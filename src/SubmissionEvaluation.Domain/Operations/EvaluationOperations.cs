using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Web;
using SubmissionEvaluation.Contracts.Data;
using SubmissionEvaluation.Contracts.Exceptions;
using SubmissionEvaluation.Contracts.Providers;
using SubmissionEvaluation.Domain.DiffCreators;
using SubmissionEvaluation.Domain.Helper;

namespace SubmissionEvaluation.Domain.Operations
{
    public class EvaluationOperations
    {
        public ISmtpProvider SmtpProvider { private get; set; }
        public IMemberProvider MemberProvider { private get; set; }
        public ILog Log { set; private get; }
        internal ProviderStore ProviderStore { get; set; }
        public IProcessProvider SandboxedProcessProvider { private get; set; }
        internal CompilerOperations CompilerOperations { private get; set; }

        internal bool ProcessSubmission(ISubmission result, IChallenge challenge, bool runFailed = false, bool runEvenNonCompilable = false,
            bool resetStats = false, bool breakAfterFirstFailedTest = false, bool forceRebuild = false)
        {
            if (!runEvenNonCompilable || !runFailed)
            {
                if (!runEvenNonCompilable && result.EvaluationResult == EvaluationResult.CompilationError)
                {
                    Log.Warning("Überspringe nicht kompilierbare Submission");
                    return false;
                }

                if (!runFailed && !result.IsPassed)
                {
                    Log.Warning("Überspringe nicht erfolgreiche Submission");
                    return false;
                }
            }

            EvaluationParameters evaluationParameters;
            try
            {
                var pathToZip = ProviderStore.FileProvider.GetSourceZipPathForSubmission(result);
                var (execParams, sizeInfo, submissionBinaryPath) = CompilerOperations.CompileSubmission(pathToZip, challenge, forceRebuild);
                evaluationParameters = Evaluate(challenge, execParams, submissionBinaryPath, sizeInfo, breakAfterFirstFailedTest);
            }
            catch (CompilerException e)
            {
                Log.Warning("Compilerfehler aufgetreten", e);
                evaluationParameters = new EvaluationParameters
                {
                    ErrorDetails = new List<FailedTestRunDetails> {new FailedTestRunDetails {ErrorMessage = $"<pre>{e.Message}</pre>"}},
                    Language = e.Language,
                    State = EvaluationResult.CompilationError,
                    SizeInBytes = new FileInfo(ProviderStore.FileProvider.GetSourceZipPathForSubmission(result)).Length
                };
            }
            catch (LanguageNotAllowedException e)
            {
                Log.Warning("Nicht erlaubte Programmiersprache für Aufgabe verwendet!");
                evaluationParameters = new EvaluationParameters
                {
                    ErrorDetails =
                        new List<FailedTestRunDetails> {new FailedTestRunDetails {ErrorMessage = $"<pre>{WebUtility.HtmlEncode(e.Message)}</pre>"}},
                    Language = e.Language,
                    State = EvaluationResult.NotAllowedLanguage
                };
            }
            catch (Exception e)
            {
                Log.Error("Fehler während des Kompilierens aufgetretten:\n{message}", e.ToString());
                evaluationParameters = new EvaluationParameters
                {
                    ErrorDetails = new List<FailedTestRunDetails> {new FailedTestRunDetails {ErrorMessage = $"<pre>{e.Message}</pre>"}},
                    State = EvaluationResult.UnknownError
                };
            }

            LogFailedTestrun(result, evaluationParameters);

            var changed = ProviderStore.FileProvider.UpdateEvaluationResult(result, evaluationParameters, resetStats);

            if (changed)
            {
                var member = MemberProvider.GetMemberById(result.MemberId);
                var wasSolved = member.SolvedChallenges.Contains(result.Challenge);
                var anyPassing = evaluationParameters.IsPassed || ProviderStore.FileProvider
                    .LoadAllSubmissionsFor(challenge).Any(x => x.IsPassed && x.MemberId == member.Id);
                var solvedChallenges = anyPassing
                    ? member.SolvedChallenges.Concat(new[] {result.Challenge})
                    : member.SolvedChallenges.Where(x => x != result.Challenge);
                MemberProvider.UpdateSolvedChallenges(member, solvedChallenges.Distinct().ToArray());
                if (!anyPassing && wasSolved)
                {
                    MemberProvider.UpdateUnlockedChallenges(member, member.UnlockedChallenges.Concat(new[] {result.Challenge}).Distinct().ToArray());
                }

                if (anyPassing && !member.SolvedChallenges.Contains(result.Challenge))
                {
                    var canRate = member.CanRate.Concat(new[] {result.Challenge}).ToArray();
                    MemberProvider.UpdateCanRate(member, canRate);
                }

                var passedDiff = 0;
                var failedDiff = 0;
                if (result.HasTestsRun)
                {
                    if (result.IsPassed && !evaluationParameters.IsPassed)
                    {
                        passedDiff--;
                    }

                    if (result.IsTestsFailed && evaluationParameters.IsPassed)
                    {
                        failedDiff--;
                    }
                }

                if (evaluationParameters.HasTestRun && evaluationParameters.IsPassed)
                {
                    passedDiff++;
                }

                if (evaluationParameters.HasTestRun && !evaluationParameters.IsPassed)
                {
                    failedDiff++;
                }

                if (passedDiff != 0 || failedDiff != 0)
                {
                    using var writeLock = ProviderStore.FileProvider.GetLock();
                    var updated = ProviderStore.FileProvider.LoadChallenge(challenge.Id, writeLock);
                    updated.State.FailedCount += failedDiff;
                    updated.State.PassedCount += passedDiff;
                    ProviderStore.FileProvider.SaveChallenge(updated, writeLock);
                }

                member = MemberProvider.GetMemberById(result.MemberId);
                StatisticsOperations.UnlockChallengesForMember(member, ProviderStore.FileProvider, MemberProvider);
            }

            return changed;
        }

        internal void RunSubmissions(IEnumerable<Result> submissions, bool runFailed = false, bool runEvenNonCompilable = false, bool resetStats = false,
            bool breakAfterFirstFailedTest = false)
        {
            var results = submissions as IList<Result> ?? submissions.ToList();
            var workingBefore = results.Count(x => x.IsPassed);
            for (var i = 0; i < results.Count; i++)
            {
                var result = results[i];
                try
                {
                    Log.Information("Führe Submission {id} ({current} von {total}) aus.", result.SubmissionId, i + 1, results.Count);
                    var challengeParameters = ProviderStore.FileProvider.LoadChallenge(result.Challenge);

                    ProcessSubmission(result, challengeParameters, runFailed, runEvenNonCompilable, resetStats, breakAfterFirstFailedTest);
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "Testwiederholung fehlgeschlagen");
                }
            }

            var workingAfter = results.Select(x => ProviderStore.FileProvider.LoadResult(x.SubmissionPath)).Count(x => x.IsPassed);
            Log.Information("Fertig. Vorher liefen {before}. Jetzt laufen {after}", workingBefore, workingAfter);
        }

        internal bool RerunSubmissionsForChallenge(IChallenge challenge)
        {
            if (challenge.IsAvailable)
            {
                var withResult = ProviderStore.FileProvider.SubmissionsOfChallengeWhichShouldRerun(challenge.Id);
                var updateRating = withResult.Select(x => ProcessSubmission(x, challenge, true, breakAfterFirstFailedTest: true)).ToList().Any(x => x);
            }

            return false;
        }

        internal void CheckForUnprocessedSubmissionsOf(IChallenge challenge, Action<ISubmission> onNewEvaluation)
        {
            try
            {
                foreach (var withoutResult in ProviderStore.FileProvider.GetSubmissionsWithoutResult(challenge))
                {
                    onNewEvaluation(withoutResult);
                }
            }
            catch (Exception ex)
            {
                ProviderStore.Log.Error(ex, "Challenge {challenge} Verarbeitung fehlgeschlagen", challenge);
            }
        }

        private void LogFailedTestrun(ISubmission submission, EvaluationParameters evaluationParameters)
        {
            ProviderStore.FileProvider.LogFailedTestruns(submission, evaluationParameters, null);
        }

        private EvaluationParameters Evaluate(IChallenge challenge, ExecutionParameters execParams, string submissionBinaryPath, SizeInfo sizeInfo,
            bool breakAfterFirstFailedTest)
        {
            Log.Information("Submission wird ausgeführt für die Aufgabe: {aufgabe}", challenge.Title);
            EvaluationParameters result;
            try
            {
                var allTests = GetAllTestsForEvaluation(challenge);

                var comparedResults = RunConfiguredTestsAndCompareResults(challenge, execParams, submissionBinaryPath, allTests, breakAfterFirstFailedTest);
                var forceDetails = challenge.RatingMethod == RatingMethod.Fixed || challenge.RatingMethod == RatingMethod.Score;
                result = new EvaluationParameters
                {
                    Language = execParams.Language,
                    Results = comparedResults,
                    CustomScore = comparedResults.Sum(x => x.CustomScore),
                    TestsSkipped = allTests.Count() - comparedResults.Count
                };

                for (var i = 0; i < comparedResults.Count; i++)
                {
                    var comparedResult = comparedResults[i];
                    if (!comparedResult.IsPassed)
                    {
                        result.ErrorDetails.Add(new FailedTestRunDetails
                        {
                            HintMessage = GetHintMessage(comparedResult),
                            HintCategories = HintDetailsGenerator.GetHintDetails(comparedResult, i, comparedResults.Count, forceDetails)
                        });
                    }
                }

                if (result.IsPassed)
                {
                    if (result.Results.Any(x => x.Timeout))
                    {
                        result.State = EvaluationResult.SucceededWithTimeout;
                    }
                    else
                    {
                        result.State = EvaluationResult.Succeeded;
                    }
                }
                else
                {
                    if (result.Results.Any(x => x.Timeout))
                    {
                        result.State = EvaluationResult.Timeout;
                    }
                    else
                    {
                        result.State = EvaluationResult.TestsFailed;
                    }
                }
            }
            catch (EvaluationTimeoutException ex)
            {
                result = new EvaluationParameters
                {
                    ErrorDetails = new List<FailedTestRunDetails> {new FailedTestRunDetails {ErrorMessage = $"<pre>{ex.Message}</pre>"}},
                    Language = execParams.Language,
                    State = EvaluationResult.Timeout
                };
            }
            catch (DeserializationException ex)
            {
                Log.Fatal(ex, "Fehler während des Evaluierens aufgetretten. Testdaten konnten nicht geladen werden.");
                result = new EvaluationParameters
                {
                    ErrorDetails = new List<FailedTestRunDetails>
                    {
                        new FailedTestRunDetails
                        {
                            ErrorMessage =
                                $"<pre>Fehler während des Evaluierens aufgetretten. Testdaten konnten nicht geladen werden. Bitte den Administrator oder Aufgabenersteller kontaktieren. Weitere Details:<br>{ex.Message}</pre>"
                        }
                    },
                    State = EvaluationResult.UnknownError,
                    Language = execParams.Language
                };
            }
            catch (Exception e)
            {
                Log.Error("Fehler während des Evaluierens aufgetretten:\n{message}", e.ToString());
                result = new EvaluationParameters
                {
                    ErrorDetails = new List<FailedTestRunDetails> {new FailedTestRunDetails {ErrorMessage = $"<pre>{e.Message}</pre>"}},
                    State = EvaluationResult.UnknownError,
                    Language = execParams.Language
                };
            }

            result.SizeInBytes = sizeInfo.SizeInBytes;
            result.CompilerVersion = execParams.CompilerVersion;
            return result;
        }

        private IEnumerable<TestParameters> GetAllTestsForEvaluation(IChallenge challenge)
        {
            var tests = ProviderStore.FileProvider.LoadTestProperties(challenge);
            foreach (var includeTest in challenge.IncludeTests)
            {
                var includeProps = ProviderStore.FileProvider.LoadChallenge(includeTest);
                var moreTests = ProviderStore.FileProvider.LoadTestProperties(includeProps);
                tests = moreTests.Concat(tests);
            }

            return tests;
        }

        private List<RunResult> RunConfiguredTestsAndCompareResults(IChallenge challenge, ExecutionParameters execParams, string submissionBinaryPath,
            IEnumerable<TestParameters> tests, bool breakAfterFirstFailedTest)
        {
            var folderMappings = new[] {new FolderMapping {ReadOnly = true, Source = submissionBinaryPath, Target = "/testrun/bin"}};

            ISyncLock CreateLock()
            {
                return SandboxedProcessProvider.GetLock(folderMappings.ToArray(),
                    tests.Where(x => x.ExpectedOutputFile != null).Select(x => x.ExpectedOutputFile.Name).Distinct()
                        .Select(x => new InteresstedFileChanges {Filename = x}).ToArray());
            }

            execParams.TestRunnerPath = "/testrun/runner";

            var lockObject = CreateLock();
            try
            {
                var previous = new List<TestParameters>();

                var comparedResults = new List<RunResult>();
                foreach (var test in tests)
                {
                    if (!test.IncludePreviousTests)
                    {
                        previous.Clear();
                    }

                    if (!test.IncludePreviousTests && test.ClearSandbox && lockObject.IsDirty)
                    {
                        lockObject.Dispose();
                        lockObject = CreateLock();
                    }

                    RunResult comparedRunResult;
                    if (test.CustomTestRunner != null)
                    {
                        comparedRunResult = RunExternalTests(challenge, test, execParams, lockObject);
                    }
                    else
                    {
                        var runResult = RunTestWithSubmission(
                            new ExecutionParameters
                            {
                                Path = execParams.Path,
                                Arguments = execParams.Arguments.Concat(execParams.PreludingTestParameters).Concat(test.Parameters).ToArray(),
                                WorkingDirectory = execParams.WorkingDirectory,
                                Language = execParams.Language,
                                OutputEncoding = execParams.OutputEncoding,
                                Env = execParams.Env,
                                TimeoutBonus = execParams.TimeoutBonus,
                                PutStdinToFile = execParams.PutStdinToFile,
                                OutputFilter = execParams.OutputFilter
                            }, test, lockObject);
                        comparedRunResult = CompareResults(runResult);
                    }

                    comparedRunResult.PreviousTestParemeters = previous.ToList();
                    comparedResults.Add(comparedRunResult);
                    if (!comparedRunResult.IsPassed)
                    {
                        if (comparedRunResult.Timeout)
                        {
                            Log.Warning("Testlauf abgebrochen, da ein Test einen Timeout hatte. Weitere Tests blockieren den Testrunner unnötig.");
                            break;
                        }

                        if (breakAfterFirstFailedTest)
                        {
                            Log.Warning("Testlauf abgebrochen, da ein Test fehlgeschlagen ist");
                            break;
                        }
                    }

                    previous.Add(test);
                }

                return comparedResults;
            }
            finally
            {
                SandboxedProcessProvider.ReleaseLock(lockObject);
            }
        }

        private RunResult RunExternalTests(IChallenge challenge, TestParameters parameters, ExecutionParameters execParams, ISyncLock lockObject)
        {
            var args = ReplacePlaceholdersWithProperties(parameters.Parameters, execParams);
            var path = ProviderStore.FileProvider.GetCustomTestRunnerPath(challenge, parameters.CustomTestRunner.Path);
            Log.Information("Starte externen Test mit {args} in {pfad}", path, string.Join(" ", args));
            var processResult = SandboxedProcessProvider.Execute(parameters.CustomTestRunner.Command, args,
                folderMappings: new[] {new FolderMapping {ReadOnly = true, Source = path, Target = execParams.TestRunnerPath}},
                workingDir: execParams.TestRunnerPath, syncLock: lockObject, timeout: parameters.Timeout * 1000);

            var resultData = ParseExternalTestResults(processResult.Result);
            return new RunResult
            {
                IsPassed = resultData.IsPassed,
                Diff = new ComparisonResult {Difference = resultData.Diff, Evaluated = true, Success = string.IsNullOrWhiteSpace(resultData.Diff)},
                CustomScore = resultData.CustomScore,
                TestParameters = parameters,
                Timeout = processResult.Result.Timeout,
                ExecutionDuration = processResult.Result.ExecutionDuration
            };
        }

        private ExternalTestResult ParseExternalTestResults(ProcessResult processResult)
        {
            ExternalTestResult result = null;
            if (!string.IsNullOrWhiteSpace(processResult.Output))
            {
                result = ProviderStore.FileProvider.DeserializeFromText<ExternalTestResult>(processResult.Output, HandleMode.ReturnNull);
            }

            if (result == null)
            {
                if (processResult.Timeout)
                {
                    return new ExternalTestResult {Diff = "Zeitüberschreitung", IsPassed = false};
                }

                result = new ExternalTestResult
                {
                    IsPassed = false,
                    Diff = $"Fehler beim Parsen des Ergebnis. Bitte Administrator oder Aufgabenersteller informieren. Inhalt: \'{processResult.Output}\'"
                };
            }

            return result;
        }

        private RunResult CompareResults(RunResult submissionResult)
        {
            var test = submissionResult.TestParameters;
            if (test.ExpectedOutput != null)
            {
                var expected = new List<string> {test.ExpectedOutput.Content};
                if (test.ExpectedOutput.Alternatives != null)
                {
                    expected.AddRange(test.ExpectedOutput.Alternatives);
                }

                foreach (var content in expected)
                {
                    submissionResult.Diff = CompareOutput(submissionResult.ResultOutput, content, test.ExpectedOutput.Settings);
                    if (submissionResult.Diff.Success)
                    {
                        break;
                    }
                }
            }
            else
            {
                submissionResult.Diff = new ComparisonResult {Success = true};
            }

            if (test.ExpectedOutputFile != null)
            {
                var expectedOutput = test.ExpectedOutputFile.Content;
                submissionResult.FileDiff = CompareOutput(submissionResult.ResultOutputFile, expectedOutput, test.ExpectedOutputFile.Settings);
            }
            else
            {
                submissionResult.FileDiff = new ComparisonResult {Success = true};
            }

            submissionResult.IsPassed = submissionResult.Diff.Success && submissionResult.FileDiff.Success;
            Log.Information("Testergebniss {isPassed}", submissionResult.IsPassed);
            return submissionResult;
        }

        private string GetHintMessage(RunResult result)
        {
            return string.IsNullOrWhiteSpace(result.TestParameters.Hint) ? "" : $"<pre><code>{HttpUtility.HtmlEncode(result.TestParameters.Hint)}</code></pre>";
        }

        private static string[] ReplacePlaceholdersWithProperties(string[] args, object rootObject)
        {
            var result = new string[args.Length];
            for (var i = 0; i < args.Length; i++)
            {
                var arg = args[i];
                var matches = Regex.Matches(arg, @"\{\{(?<property>\w+)\}\}");
                foreach (Match match in matches)
                {
                    var obj = rootObject;
                    var fullname = match.Groups["property"].Value;
                    var names = fullname.Split('.');
                    foreach (var name in names)
                    {
                        if (obj != null)
                        {
                            var type = obj.GetType();
                            var prop = type.GetProperty(name);
                            obj = prop?.GetValue(obj);
                            if (obj is IEnumerable<object> enu)
                            {
                                obj = string.Join(" ", enu);
                            }
                        }
                    }

                    var replaceText = obj?.ToString() ?? "";
                    arg = arg.Replace($"{{{{{fullname}}}}}", $"{replaceText}");
                }

                result[i] = arg;
            }

            return result;
        }

        private ComparisonResult CompareOutput(string output, string expectedOutput, CompareSettings settings)
        {
            if (expectedOutput == null)
            {
                return new ComparisonResult {Success = true};
            }

            var result = new ComparisonResult {Success = false};
            IDiffCreator diffCreator = null;
            switch (settings.CompareMode)
            {
                case CompareModeType.Exact:
                case CompareModeType.ExactSubstring:
                    diffCreator = new ExactDiffCreator(settings.Trim, !settings.IncludeCase, !settings.KeepUmlauts, settings.UnifyFloatingNumbers,
                        settings.Whitespaces, settings.CompareMode == CompareModeType.ExactSubstring);
                    break;
                case CompareModeType.Contains:
                case CompareModeType.ContainsWord:
                    diffCreator = new ContainsDiffCreator(settings.Trim, !settings.IncludeCase, !settings.KeepUmlauts, settings.UnifyFloatingNumbers,
                        settings.Whitespaces, settings.CompareMode == CompareModeType.ContainsWord);
                    break;
                case CompareModeType.Regex:
                    diffCreator = new RegexDiffCreator(settings.Trim, !settings.IncludeCase, !settings.KeepUmlauts, settings.UnifyFloatingNumbers,
                        settings.Whitespaces);
                    break;
                case CompareModeType.Numbers:
                    diffCreator = new NumbersDiffCreator(settings.UnifyFloatingNumbers, settings.Threshold);
                    break;
            }

            var (success, details, showExpected) = diffCreator.GetDiff(output, expectedOutput);
            result.Difference = details;
            result.Success = success;
            result.Evaluated = true;
            if (showExpected)
            {
                result.Expected = $"<pre><code>{expectedOutput}</code></pre>";
                if (settings.CompareMode == CompareModeType.ExactSubstring || settings.CompareMode == CompareModeType.Contains ||
                    settings.CompareMode == CompareModeType.ContainsWord)
                {
                    result.Expected +=
                        "<p><b>Hinweis:</b>Der erwartete Text muss nicht der kompletten Ausgabe entsprechen. Der Text muss allerdings als ein zusammenhängender Texteil in der Ausgabe vorhanden sein.</p>";
                }
            }

            if (settings.CompareMode != CompareModeType.Contains && settings.CompareMode != CompareModeType.ContainsWord || showExpected)
            {
                result.Output = output;
            }

            return result;
        }

        private RunResult RunTestWithSubmission(ExecutionParameters execParams, TestParameters test, ISyncLock lockObject)
        {
            Log.Information("Starte sanboxed Test {path} {args} in {dir}", execParams.Path, execParams.Arguments, execParams.WorkingDirectory);

            // Fix Newlines befor passing
            var input = test.Input?.Content;
            var inputFixed = input != null ? Regex.Replace(input, @"\r\n?|\n", "\n") : null;
            var testTimeout = Math.Max(test.Timeout, 5);

            string stdinInputFile = null;
            try
            {
                var testInputFiles = test.InputFiles;
                if (!string.IsNullOrWhiteSpace(execParams.PutStdinToFile))
                {
                    stdinInputFile = Path.GetRandomFileName();
                    File.WriteAllText(stdinInputFile, test.Input?.Content ?? "");
                    testInputFiles = (test.InputFiles?.ToArray() ?? new FileDefinition[0]).Concat(new[]
                    {
                        new FileDefinition {ContentFilePath = stdinInputFile, Name = execParams.PutStdinToFile}
                    }).ToList();
                }

                var processResult = SandboxedProcessProvider.Execute(execParams.Path, execParams.Arguments, inputFixed, execParams.WorkingDirectory,
                    (testTimeout + execParams.TimeoutBonus) * 1000, execParams.OutputEncoding, testInputFiles, execParams.Env, syncLock: lockObject).Result;
                if (processResult.Exception)
                {
                    Log.Error("Exception bei Programmausführung");
                    throw new EvaluationException("Fehler bei der Ausführung. Bitte den Administrator oder Aufgabenersteller benachrichtigen.");
                }

                var resultOutput = processResult.Output;
                // HOTFIX: Fixing errors with some submissions
                resultOutput = resultOutput.Replace(Convert.ToChar(0x0).ToString(), "");

                if (execParams.OutputFilter != null)
                {
                    foreach (var filter in execParams.OutputFilter.Where(filter => resultOutput.Contains(filter)))
                    {
                        resultOutput = resultOutput.Replace(filter, "");
                    }
                }

                var result = new RunResult
                {
                    ExecutionDuration = processResult.ExecutionDuration,
                    PeakPagedMem = 0,
                    PeakVirtualMem = 0,
                    PeakWorkingSet = 0,
                    ResultOutput = resultOutput,
                    TestParameters = test,
                    Timeout = processResult.Timeout,
                    Commandline = execParams.Path + " " + execParams.Arguments,
                    WorkingDirectory = execParams.WorkingDirectory
                };

                if (test.ExpectedOutputFile != null)
                {
                    var files = processResult.ModifiedFiles.Where(x =>
                        x.Filename.IndexOf(test.ExpectedOutputFile.Name, StringComparison.CurrentCultureIgnoreCase) >= 0).ToList();
                    if (files.Count == 0)
                    {
                        Log.Warning("Fehler bei der Ausführung. Die erwartete Ausgabedatei {name} wurde nicht gefunden.", test.ExpectedOutputFile.Name);
                        result.ResultOutputFile = $"Fehler bei der Ausführung. Die erwartete Ausgabedatei {test.ExpectedOutputFile.Name} wurde nicht gefunden.";
                    }
                    else if (files.Count > 1)
                    {
                        var names = string.Join(", ", files.Select(x => x.Filename));
                        Log.Warning("Fehler bei der Ausführung. Die erwartete Ausgabedatei {name} wurde mehrfach gefunden {files}.",
                            test.ExpectedOutputFile.Name, names);
                        result.ResultOutputFile =
                            $"Fehler bei der Ausführung. Die erwartete Ausgabedatei {test.ExpectedOutputFile.Name} wurde mehrfach ({names}) gefunden.";
                    }
                    else
                    {
                        result.ResultOutputFile = Encoding.Default.GetString(files[0].Data);
                    }
                }

                return result;
            }
            finally
            {
                if (stdinInputFile != null)
                {
                    try
                    {
                        File.Delete(stdinInputFile);
                    }
                    catch
                    {
                    }
                }
            }
        }
    }
}
