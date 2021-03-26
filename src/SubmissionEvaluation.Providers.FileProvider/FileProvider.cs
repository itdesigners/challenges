using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using MimeTypes;
using SubmissionEvaluation.Contracts.Data;
using SubmissionEvaluation.Contracts.Data.Ranklist;
using SubmissionEvaluation.Contracts.Data.Review;
using SubmissionEvaluation.Contracts.Providers;

namespace SubmissionEvaluation.Providers.FileProvider
{
    public class FileProvider : IFileProvider
    {
        private const string statusChangesLogFile = "challenge_results.csv";
        private readonly string challengesDir;
        private readonly ILog log;
        private readonly bool logStatusChanges;
        private readonly YamlProvider yamlProvider;
        private IEnumerable<IChallenge> chached_challenges;
        private bool dirtyFlag;

        public FileProvider(ILog log, string challengesDir, string wwwrootDir, bool logStatusChanges)
        {
            this.challengesDir = challengesDir;
            this.log = log;
            yamlProvider = new CachedYamlProvider(log);
            this.logStatusChanges = logStatusChanges;
            dirtyFlag = true;
            if (logStatusChanges)
            {
                File.WriteAllText(statusChangesLogFile, $"Challenge;Lang;ID;Old;New{Environment.NewLine}");
            }
        }

        public string GetPathToChallengeProperties(string challenge)
        {
            return Path.Combine(GetPathToChallenge(challenge), "challenge.md");
        }

        public string GetSourceZipPathForSubmission(ISubmission submission)
        {
            return GetSourceZipPath(GetSubmissionPath(submission));
        }

        public void LogFailedTestruns(ISubmission submission, EvaluationParameters evaluationParameters, byte[] data)
        {
            var submissionPath = GetSubmissionPath(submission);
            var file = Path.Combine(submissionPath, "failed_report.yml");
            var fileZip = Path.Combine(submissionPath, "failed_report.zip");

            if (File.Exists(file))
            {
                File.Delete(file);
            }

            if (File.Exists(fileZip))
            {
                File.Delete(fileZip);
            }

            if (evaluationParameters.IsPassed)
            {
                return;
            }

            yamlProvider.SerializeErrorDetails(file, evaluationParameters.ErrorDetails.FirstOrDefault()?.HintCategories);
            if (data != null)
            {
                File.WriteAllBytes(fileZip, data);
            }
        }

        public void DeleteSubmission(Result result)
        {
            using var writeLock = (WriteLock) GetLock();
            writeLock.Add(result.SubmissionPath);
            DeleteDirectory(result.SubmissionPath);
        }

        public IEnumerable<string> GetChallengeIds()
        {
            return Directory.GetDirectories(GetPathToChallenges()).Select(Path.GetFileName);
        }

        public IEnumerable<string> GetGroupIds()
        {
            return Directory.GetDirectories(GetPathToGroups()).Select(Path.GetFileName);
        }

        public IEnumerable<IChallenge> LoadChallenges()
        {
            if (dirtyFlag)
            {
                chached_challenges = GetChallengeIds().Select(x => LoadChallenge(x));
                dirtyFlag = false;
            }

            return chached_challenges;
        }

        public IEnumerable<ISubmission> GetSubmissionsWithoutResult(IChallenge challenge)
        {
            return LoadAllSubmissionsFor(challenge).Where(x =>
                x.EvaluationState == EvaluationState.NotEvaluated || x.EvaluationState == EvaluationState.RerunRequested);
        }

        public string GetBuildPathForSubmissionSource(string pathToContent)
        {
            if (!Directory.Exists(pathToContent))
            {
                throw new IOException("Following directory could not be found: " + pathToContent);
            }

            var buildPath = Path.Combine(Directory.GetParent(pathToContent).FullName, "built");
            Directory.CreateDirectory(buildPath);
            return buildPath;
        }

        public void DeleteChallenge(IChallenge challenge)
        {
            using var writeLock = (WriteLock) GetLock();
            var path = GetPathToChallenge(challenge.Id);
            writeLock.Add(path);
            DeleteDirectory(path);
            dirtyFlag = true;
        }

        public bool UpdateEvaluationResult(ISubmission submission, EvaluationParameters evaluationParameters, bool resetStats = false)
        {
            try
            {
                using var writeLock = GetLock();
                var result = LoadResult(submission.Challenge, submission.SubmissionId, writeLock);
                var changed = false;
                if (result.EvaluationResult != evaluationParameters.State)
                {
                    changed = true;
                    log.Information("Zustandswechsel {oldState} -> {newState}", result.EvaluationResult, evaluationParameters.State);

                    if (logStatusChanges)
                    {
                        File.AppendAllText(statusChangesLogFile,
                            $"{submission.Challenge};{submission.Language};{submission.SubmissionId};{result.EvaluationResult};{evaluationParameters.State}{Environment.NewLine}");
                    }

                    if (result.IsPassed && !evaluationParameters.IsPassed)
                    {
                        log.Fatal("Bisher laufende Submission schlägt nun fehl ({newPassedState})!\n{submission}", evaluationParameters.State,
                            submission.SubmissionId);
                        result.ReportFailing = true;
                    }
                }

                result.EvaluationResult = evaluationParameters.State;
                if (result.EvaluationState == EvaluationState.NotEvaluated || resetStats)
                {
                    result.ExecutionDuration = evaluationParameters.ExecutionDuration;
                    result.CustomScore = evaluationParameters.CustomScore;
                    changed = true;
                }

                if (result.IsPassed)
                {
                    if (result.ExecutionDuration == 0 || result.ExecutionDuration > evaluationParameters.ExecutionDuration)
                    {
                        result.ExecutionDuration = evaluationParameters.ExecutionDuration;
                        changed = true;
                    }

                    if (result.CustomScore < evaluationParameters.CustomScore)
                    {
                        result.CustomScore = evaluationParameters.CustomScore;
                        changed = true;
                    }

                    result.ReportFailing = false;
                }

                if (evaluationParameters.State == EvaluationResult.CompilationError)
                {
                    if (evaluationParameters.ErrorDetails.Count == 1)
                    {
                        result.CompileError = evaluationParameters.ErrorDetails[0].ErrorMessage;
                    }
                }

                if (result.IsPassed != evaluationParameters.IsPassed)
                {
                    changed = true;
                }

                result.TestsPassed = evaluationParameters.TestsPassed;
                result.TestsFailed = evaluationParameters.TestsFailed;
                result.TestsSkipped = evaluationParameters.TestsSkipped;
                result.SizeInBytes = evaluationParameters.SizeInBytes;
                result.CompilerVersion = evaluationParameters.CompilerVersion;
                result.Language = evaluationParameters.Language;
                result.LastTestrun = DateTime.Now;
                result.EvaluationState = EvaluationState.Evaluated;

                if (result.ReviewState == ReviewStateType.InProgress && !result.IsPassed)
                {
                    result.ReviewState = ReviewStateType.NotReviewed;
                }

                SaveResult(result, writeLock);
                return changed;
            }
            catch (Exception ex)
            {
                log.Warning(ex, "Fehler beim aktualisieren der Submission: {file}", submission);
            }

            return false;
        }

        public void AbortRunningReview(ISubmission submission)
        {
            using var writeLock = GetLock();
            var result = LoadResult(submission.Challenge, submission.SubmissionId, writeLock);
            result.Reviewer = null;
            result.ReviewState = ReviewStateType.NotReviewed;
            result.ReviewDate = null;
            SaveResult(result, writeLock);
        }

        public void SetReviewStateAsStarted(ISubmission submission, string reviewer, DateTime reviewDueDate)
        {
            using var writeLock = GetLock();
            var result = LoadResult(submission.Challenge, submission.SubmissionId, writeLock);
            result.Reviewer = reviewer;
            result.ReviewState = ReviewStateType.InProgress;
            result.ReviewDate = reviewDueDate;
            SaveResult(result, writeLock);
        }

        public void SetReviewStateAsReviewed(ISubmission submission, int rating)
        {
            using var writeLock = GetLock();
            var result = LoadResult(submission.Challenge, submission.SubmissionId, writeLock);
            result.ReviewState = ReviewStateType.Reviewed;
            result.ReviewRating = rating;
            result.ReviewDate = DateTime.Now;
            SaveResult(result, writeLock);
        }

        public void SetReviewStateAsSkipped(ISubmission submission)
        {
            using var writeLock = GetLock();
            var result = LoadResult(submission.Challenge, submission.SubmissionId, writeLock);
            result.ReviewState = ReviewStateType.Skipped;
            result.ReviewRating = 0;
            result.ReviewDate = DateTime.Now;
            result.Reviewer = null;
            SaveResult(result, writeLock);
        }

        public void SetDuplicateScore(ISubmission submission, int? score, string bestMatchId)
        {
            using var writeLock = GetLock();
            var result = LoadResult(submission.Challenge, submission.SubmissionId, writeLock);
            result.DuplicateId = score > 65 ? bestMatchId : null;
            result.DuplicateScore = score;
            SaveResult(result, writeLock);
        }

        public void UnsetReviewerAndResetIfInProgress(ISubmission submission)
        {
            using var writeLock = GetLock();
            var result = LoadResult(submission.Challenge, submission.SubmissionId, writeLock);
            result.Reviewer = Member.REMOVED_ENTRY_ID;
            if (result.ReviewState == ReviewStateType.InProgress)
            {
                result.ReviewState = ReviewStateType.NotReviewed;
            }

            SaveResult(result, writeLock);
        }

        public void AddFileToZip(MemoryStream zipStream, byte[] fileToAdd, string pathInZip)
        {
            try
            {
                using var archive = new ZipArchive(zipStream, ZipArchiveMode.Update, true);
                var entry = archive.CreateEntry(pathInZip);
                using var writer = new BinaryWriter(entry.Open());
                writer.Write(fileToAdd);
            }
            catch (Exception e)
            {
                log.Error(e, "Hinzufügen von Datei als {pathInZip} ins ZIP fehlgeschlagen", pathInZip);
                throw;
            }
        }

        public IEnumerable<string> GetFilenamesInsideZip(byte[] data)
        {
            using var stream = new MemoryStream(data);
            using var archive = new ZipArchive(stream);
            foreach (var entry in archive.Entries)
            {
                yield return entry.FullName;
            }
        }

        public List<HintCategory> LoadFailedSubmissionReport(ISubmission submission)
        {
            var file = Path.Combine(((Result) submission).SubmissionPath, "failed_report.yml");
            if (File.Exists(file))
            {
                return yamlProvider.DeserializeErrorDetails(file);
            }

            return new List<HintCategory>();
        }

        public void DeleteCurrentSubmissionBuild(string submissionPath)
        {
            var path = Path.Combine(submissionPath, "built");
            if (Directory.Exists(path))
            {
                try
                {
                    DeleteDirectory(path);
                }
                catch (Exception ex)
                {
                    log.Warning(ex, "Löschen von built-Directory nicht möglich");
                }
            }
        }

        public void MarkSubmissionForRerun(ISubmission submission)
        {
            using var writeLock = GetLock();
            var result = LoadResult(submission.Challenge, submission.SubmissionId, writeLock);
            if (result.EvaluationState != EvaluationState.RerunRequested)
            {
                result.EvaluationState = EvaluationState.RerunRequested;
                SaveResult(result, writeLock);
            }
        }

        public void MarkSubmissionAsDead(ISubmission submission)
        {
            using var writeLock = GetLock();
            var result = LoadResult(submission.Challenge, submission.SubmissionId, writeLock);
            if (result.EvaluationState != EvaluationState.Dead)
            {
                result.EvaluationState = EvaluationState.Dead;
                SaveResult(result, writeLock);
            }
        }


        public IEnumerable<string> GetAdditionalFiles(IChallenge challenge)
        {
            var di = new DirectoryInfo(GetPathToChallenge(challenge.Id));
            return di.EnumerateFiles("*", SearchOption.AllDirectories).Where(x => x.Name != "challenge.md" && x.Name != "_testParameters.yml")
                .Select(x => GetRelativePath(x.FullName, di.FullName));
        }

        public void SaveAdditionalFile(IChallenge challenge, string filename, byte[] data)
        {
            var path = Path.Combine(GetPathToChallenge(challenge.Id), Path.GetFileName(filename));
            File.WriteAllBytes(path, data);
        }

        public void DeleteAdditionalFile(IChallenge challenge, string filename)
        {
            var pathToChallenge = GetPathToChallenge(challenge.Id);
            var path = Path.Combine(pathToChallenge, Path.GetFileName(filename));
            File.Delete(path);

            PruneEmptyDirs(pathToChallenge);
        }


        public void RenameAdditionalFile(IChallenge challenge, string oldname, string newname)
        {
            var pathToChallenge = GetPathToChallenge(challenge.Id);
            var oldpath = Path.Combine(pathToChallenge, oldname);
            var newpath = Path.Combine(pathToChallenge, newname);

            var rootdir = new DirectoryInfo(pathToChallenge);
            var newdir = new DirectoryInfo(newpath);
            if (!newdir.FullName.StartsWith(rootdir.FullName))
            {
                throw new Exception("New path is outside of challenge dir!");
            }

            Directory.CreateDirectory(Path.GetDirectoryName(newpath));
            File.Move(oldpath, newpath);

            PruneEmptyDirs(pathToChallenge);
        }

        public MemoryStream CreateZipInMemory((string, string)[] paths, (string, byte[])[] additionalFiles = null)
        {
            var ms = new MemoryStream();
            using var archive = new ZipArchive(ms, ZipArchiveMode.Create, true);
            foreach (var path in paths)
            {
                foreach (var file in Directory.EnumerateFiles(path.Item1, "*", SearchOption.AllDirectories))
                {
                    var relativePath = GetRelativePath(file, path.Item1);
                    if (relativePath[0] == Path.DirectorySeparatorChar)
                    {
                        relativePath = relativePath[1..];
                    }

                    var finalPath = string.IsNullOrWhiteSpace(path.Item2) ? relativePath : $"{path.Item2}{Path.DirectorySeparatorChar}{relativePath}";
                    archive.CreateEntryFromFile(file, finalPath);
                }
            }

            if (additionalFiles == null)
            {
                return ms;
            }

            foreach (var (name, data) in additionalFiles)
            {
                var entry = archive.CreateEntry(name);
                using var stream = entry.Open();
                stream.Write(data, 0, data.Length);
            }

            return ms;
        }

        public void SaveReview(ISubmission submission, ReviewData review)
        {
            var pathReviewData = GetPathToReviewData(submission);
            if (!Directory.Exists(Path.GetDirectoryName(pathReviewData)))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(pathReviewData));
            }

            yamlProvider.Serialize(GetPathToReviewData(submission), review);
        }

        public ReviewData LoadReview(ISubmission submission)
        {
            return yamlProvider.Deserialize<ReviewData>(GetPathToReviewData(submission));
        }

        public HelpPage LoadHelpPage(string path, bool includeDescription = true, IWriteLock writeLock = null)
        {
            var file = Path.Combine(GetPathToHelpPages(), path + ".md");
            if (writeLock is WriteLock w)
            {
                w.Add(file);
            }

            return DeserializeHelpPage(file);
        }

        public List<HelpPage> GetHelpHierarchy()
        {
            var pageFiles = Directory.GetFiles(GetPathToHelpPages(), "*.md", SearchOption.AllDirectories);
            var pages = pageFiles.Select(DeserializeHelpPage).ToDictionary(x => x.Path, x => x);

            foreach (var page in pages)
            {
                if (page.Value.Parent != null)
                {
                    if (pages.TryGetValue(page.Value.Parent, out var parent))
                    {
                        parent.SubPages.Add(page.Value);
                    }
                    else
                    {
                        page.Value.Parent = null;
                    }
                }
            }

            return pages.Values.Where(x => x.Parent == null).ToList();
        }

        public void SaveHelpPage(HelpPage helpPage, IWriteLock writeLock)
        {
            if (string.IsNullOrWhiteSpace(helpPage.Path))
            {
                throw new Exception("Path was not set");
            }

            SerializeHelpPage(helpPage, writeLock);
        }


        public (string name, byte[] data, string type, DateTime lastMod) GetHelpAdditionalFile(string path)
        {
            return LoadFile(Path.Combine(GetPathToHelpPages(), path));
        }

        public IEnumerable<string> GetSubmissionFilesRelativePath(ISubmission submission)
        {
            var pathToZip = GetSourceZipPathForSubmission(submission);
            var pathToSource = GetExtractPathForZip(pathToZip);
            var files = GetSubmissionFilesPath(submission);
            var relativeFiles = files.Select(p => p[(pathToSource.Length + 1)..]);
            return relativeFiles;
        }

        public IEnumerable<string> GetSubmissionFilesPath(ISubmission submission)
        {
            var pathToZip = GetSourceZipPathForSubmission(submission);
            var pathToSource = GetExtractPathForZip(pathToZip);
            if (!Directory.Exists(pathToSource))
            {
                ExtractContent(pathToZip);
            }

            return Directory.EnumerateFiles(pathToSource, "*", SearchOption.AllDirectories);
        }

        public string GetSubmissionFileContent(ISubmission submission, string relativeFilePath)
        {
            var pathToZip = GetSourceZipPathForSubmission(submission);
            var pathToSource = GetExtractPathForZip(pathToZip);
            if (!Directory.Exists(pathToSource))
            {
                ExtractContent(pathToZip);
            }

            var path = Path.Combine(pathToSource, relativeFilePath);
            return File.ReadAllText(path);
        }

        public bool HasReviewFile(ISubmission submission)
        {
            return File.Exists(GetPathToReviewData(submission));
        }

        public byte[] BuildZipFor(IEnumerable<(string filename, byte[] data)> files)
        {
            using var stream = CreateZipInMemory(new (string, string)[0]);
            foreach (var (filename, data) in files)
            {
                AddFileToZip(stream, data, filename);
            }

            return stream.ToArray();
        }

        public string GetCustomTestRunnerPath(IChallenge challenge, string relativePath)
        {
            return Path.Combine(GetPathToChallenge(challenge.Id), relativePath);
        }


        public string ExtractContent(string pathToZip, bool forceClean = true)
        {
            var extractPath = GetExtractPathForZip(pathToZip);
            if (forceClean || !Directory.Exists(extractPath) || !Directory.EnumerateFiles(extractPath, "*", SearchOption.AllDirectories).Any())
            {
                ClearTargetPath(extractPath);
                ExtractArchiveFileTo(pathToZip, extractPath);
            }

            return extractPath;
        }

        public void DeleteAllSubmissions()
        {
            DeleteDirectory(GetPathToSubmissions());
            Directory.CreateDirectory(GetPathToSubmissions());
        }

        public void DeleteAllStatistics()
        {
            var dir = new DirectoryInfo(GetPathToJekyllData());
            foreach (var file in dir.GetFiles())
            {
                if (file.Name != "members.yml")
                {
                    file.Delete();
                }
            }
        }

        public void CreateMissingDirectories()
        {
            Directory.CreateDirectory(GetPathToBundles());
            Directory.CreateDirectory(GetPathToChallenges());
            Directory.CreateDirectory(GetPathToSubmissions());
            Directory.CreateDirectory(GetPathToGroups());
            Directory.CreateDirectory(GetPathToMembers());
        }

        public IEnumerable<TestParameters> LoadTestProperties(IChallenge challenge)
        {
            var testfile = GetTestFilePath(challenge.Id, "_testParameters.yml");
            var tests = yamlProvider.DeserializeTestProperties(testfile).ToList();
            foreach (var test in tests.Where(x => x.Timeout == 0))
            {
                test.Timeout = TestParameters.DefaultTimeout;
            }

            return tests;
        }


        public List<Activity> LoadRecentActivities(IWriteLock writeLock = null)
        {
            var path = GetRecentActivityFile();
            if (writeLock is WriteLock w)
            {
                w.Add(path);
            }

            return yamlProvider.Deserialize<List<Activity>>(path, HandleMode.CreateDefaultObjectAndDelete);
        }

        public Awards LoadAwards(IWriteLock writeLock = null)
        {
            static Awards RebuildAwards(Dictionary<string, List<Award>> items)
            {
                var awards = new Awards();
                foreach (var item in items)
                {
                    awards.AwardWith(item.Key, item.Value.ToArray());
                }

                return awards;
            }

            var file = GetAwardsFile();
            if (writeLock is WriteLock w)
            {
                w.Add(file);
            }

            var list = yamlProvider.Deserialize<Dictionary<string, List<Award>>>(file, HandleMode.CreateDefaultObject);
            return RebuildAwards(list);
        }

        public Challenge LoadChallenge(string challenge, IWriteLock writeLock = null)
        {
            if (writeLock is WriteLock w)
            {
                w.Add(GetPathToChallenge(challenge));
            }

            var parsedFile = yamlProvider.DeserializeWithDescription<Challenge>(GetPathToChallengeProperties(challenge));
            parsedFile.Id = challenge;
            return parsedFile;
        }

        public IEnumerable<Member> LoadMembers()
        {
            if (!Directory.Exists(GetPathToMembers()))
            {
                yield break;
            }

            foreach (var memberFile in Directory.EnumerateFiles(GetPathToMembers(), "*.yml"))
            {
                Member member = null;
                try
                {
                    member = LoadMember(Path.GetFileNameWithoutExtension(memberFile));
                }
                catch (Exception ex)
                {
                    log.Error(ex, "Fehler beim Laden des Users {member}", Path.GetFileNameWithoutExtension(memberFile));
                }

                if (member != null)
                {
                    yield return member;
                }
            }
        }

        public Member LoadMember(string id, IWriteLock writeLock = null)
        {
            var pathToMember = GetPathToMember(id);

            if (writeLock is WriteLock w)
            {
                w.Add(pathToMember);
            }

            var member = yamlProvider.Deserialize<Member>(pathToMember, force: writeLock != null);
            member.Id = id;
            return member;
        }

        public void SaveMember(Member member, IWriteLock writeLock)
        {
            var path = GetPathToMember(member.Id);
            writeLock.EnsureWriteLock(path);
            yamlProvider.Serialize(path, member, false);
        }

        public List<Result> LoadTestedResults(IChallenge challenge)
        {
            var submissions = GetSubmissionsWithResult(challenge.Id);
            return submissions.Select(DeserializeResult).ToList();
        }

        public Result LoadResult(string submissionPath, IWriteLock writeLock = null)
        {
            var file = GetSubmissionResultFile(submissionPath);
            if (writeLock is WriteLock w)
            {
                w.Add(submissionPath);
            }

            return DeserializeResult(file);
        }

        public Result LoadResult(string challenge, string id, IWriteLock writeLock = null)
        {
            var file = GetSubmissionResultFile(challenge, id);
            if (writeLock is WriteLock w)
            {
                w.Add(Path.GetDirectoryName(file));
            }

            return DeserializeResult(file);
        }

        public IEnumerable<Result> LoadAllSubmissions(bool includeDead = false)
        {
            var results = Directory.EnumerateDirectories(GetPathToSubmissions()).SelectMany(Directory.EnumerateDirectories)
                .Select(x => Path.Combine(x, "result.yml")).Where(File.Exists).Select(DeserializeResult);
            return !includeDead ? results.Where(x => x.EvaluationState != EvaluationState.Dead) : results;
        }

        public IEnumerable<Result> LoadAllSubmissionsFor(IChallenge challenge, bool includeDead = false)
        {
            var path = GetPathToSubmissionsFor(challenge.Id);
            if (!Directory.Exists(path))
            {
                return new List<Result>();
            }

            var results = Directory.EnumerateDirectories(path).Select(GetSubmissionResultFile).Where(File.Exists).Select(DeserializeResult);
            return !includeDead ? results.Where(x => x.EvaluationState != EvaluationState.Dead) : results;
        }

        public IEnumerable<Result> LoadAllSubmissionsFor(IMember member, bool includeDead = false)
        {
            return LoadAllSubmissions(includeDead).Where(x => x.MemberId == member.Id);
        }


        public GlobalRanklist LoadGlobalRanklist(IWriteLock writeLock = null)
        {
            var path = GetGlobalRankingPath();
            if (writeLock is WriteLock w)
            {
                w.Add(path);
            }

            return yamlProvider.Deserialize<GlobalRanklist>(path, HandleMode.CreateDefaultObjectAndDelete);
        }

        public IEnumerable<GlobalRanklist> LoadAllSemesterRanklists()
        {
            if (!Directory.Exists(GetSemesterRankingPath()))
            {
                return new List<GlobalRanklist>();
            }

            return Directory.GetFiles(GetSemesterRankingPath())
                .Select(p => yamlProvider.Deserialize<GlobalRanklist>(p, HandleMode.CreateDefaultObjectAndDelete));
        }

        public ReviewTemplate LoadReviewTemplate(IChallenge challenge)
        {
            var path = GetReviewTemplatePath(((Challenge) challenge).ReviewTemplate);
            return yamlProvider.Deserialize<ReviewTemplate>(path);
        }

        public void SaveAwards(Awards awards, IWriteLock writeLock)
        {
            var path = GetAwardsFile();
            writeLock.EnsureWriteLock(path);
            var lst = new Dictionary<string, List<Award>>();
            foreach (var award in awards)
            {
                lst[award.Key] = award.Value.ToList();
            }

            yamlProvider.Serialize(path, lst, referenceDuplicates: false);
        }

        public void SaveGlobalRankingList(GlobalRanklist ranklist, IWriteLock writeLock)
        {
            var path = GetGlobalRankingPath();
            if (writeLock is WriteLock w)
            {
                w.Add(path);
            }

            writeLock.EnsureWriteLock(path);
            yamlProvider.Serialize(path, ranklist, false);
        }

        public void SaveSemesterRankingList(GlobalRanklist ranklist)
        {
            var semesterPeriod = ranklist.CurrentSemester.Period == SemesterPeriod.SS ? "SS" : "WS";
            var semester = ranklist.CurrentSemester.Years.Replace("/", "_"); //Paths dont allow /
            var filename = $"semesterRanking_{semesterPeriod}_{semester}.yml";
            var path = Path.Combine(GetSemesterRankingPath(), filename);

            Directory.CreateDirectory(GetSemesterRankingPath());
            yamlProvider.Serialize(path, ranklist, false);
        }

        public void SaveRecentActivities(List<Activity> activities, IWriteLock writeLock)
        {
            var path = GetRecentActivityFile();
            writeLock.EnsureWriteLock(path);
            yamlProvider.Serialize(path, activities, false);
        }

        public void SaveTestProperties(IChallenge challenge, IEnumerable<TestParameters> testParameters, IWriteLock writeLock)
        {
            writeLock.EnsureWriteLock(GetPathToChallenge(challenge.Id));
            var testFile = GetTestFilePath(challenge.Id, "_testParameters.yml");
            var tests = testParameters.ToList();
            foreach (var test in tests.Where(x => x.Timeout == TestParameters.DefaultTimeout))
            {
                test.Timeout = 0;
            }

            yamlProvider.SerializeTestProperties(testFile, tests);
        }

        public T DeserializeFromText<T>(string text, HandleMode mode = HandleMode.ThrowException) where T : class, new()
        {
            return yamlProvider.DeserializeFromText<T>(text, mode);
        }

        public void CreateChallenge(IChallenge challenge)
        {
            using var writeLock = (WriteLock) GetLock();
            var path = GetPathToChallenge(challenge.Id);
            writeLock.Add(path);
            if (!Directory.Exists(path))
            {
                Directory.CreateDirectory(path);
            }
            else
            {
                throw new IOException($"Eine Aufgabe mit dem Namen {challenge.Id} existiert bereits!");
            }

            ((Challenge) challenge).IsDraft = true;
            ((Challenge) challenge).Date = DateTime.Now;
            SaveChallenge(challenge, writeLock);
            dirtyFlag = true;
        }

        public void SaveChallenge(IChallenge challenge, IWriteLock writeLock)
        {
            var props = (Challenge) challenge;
            var path = GetPathToChallenge(challenge.Id);
            writeLock.EnsureWriteLock(path);
            if (!Directory.Exists(path))
            {
                throw new IOException("Aufgabe existiert nicht!");
            }

            var filePath = Path.Combine(path, "challenge.md");
            yamlProvider.SerializeWithDescription(filePath, props);
            dirtyFlag = true;
        }

        public ISubmission StoreNewSubmission(IMember member, DateTime date, string challengeName, byte[] zipData, IEnumerable<string> compilableFiles)
        {
            var hash = GetHash(zipData);
            var challenge = LoadChallenge(challengeName);
            var submission = LoadAllSubmissionsFor(challenge).FirstOrDefault(x => x.MemberId == member.Id && x.Hash == hash);
            string submissionPath;
            if (submission != null)
            {
                submissionPath = submission.SubmissionPath;
            }
            else
            {
                submissionPath = GetUniqueSubmissionPath(challengeName);
                SaveAttachementAsSourceFile(submissionPath, zipData);
            }

            var resultFile = GetSubmissionResultFile(submissionPath);
            if (!File.Exists(resultFile))
            {
                using var writeLock = (WriteLock) GetLock();
                writeLock.Add(submissionPath);
                var result = new Result
                {
                    SubmissionId = new DirectoryInfo(submissionPath).Name,
                    SubmissionPath = submissionPath,
                    MemberId = member.Id,
                    MemberName = member.Name,
                    Challenge = challengeName,
                    EvaluationState = EvaluationState.NotEvaluated,
                    SubmissionDate = date,
                    LastSubmissionDate = date,
                    Hash = hash
                };
                SaveResult(result, writeLock);
                return result;
            }

            using (var writeLock = GetLock())
            {
                var result = LoadResult(submissionPath, writeLock);
                result.EvaluationState = EvaluationState.RerunRequested;
                result.LastSubmissionDate = date;
                SaveResult(result, writeLock);
                return result;
            }
        }

        public IEnumerable<ISubmission> SubmissionsOfChallengeWhichShouldRerun(string challenge)
        {
            return GetSubmissionsWithResult(challenge).Select(Path.GetDirectoryName).Select(x => LoadResult(x));
        }

        public (string name, byte[] data, string type, DateTime lastMod) GetChallengeAdditionalFile(string challengeName, string fileName)
        {
            return LoadFile(Path.Combine(GetPathToChallenge(challengeName), fileName));
        }

        public (string name, byte[] data, string type, DateTime lastMod) GetChallengeZip(IChallenge challenge)
        {
            using var zip = CreateZipInMemory(new[] {(GetPathToChallenge(challenge.Id), "")}, new[] {("id.txt", Encoding.Default.GetBytes(challenge.Id))});
            zip.Flush();
            return (challenge.Id + ".zip", zip.ToArray(), GetMimeTypeForFile("a.zip"), challenge.LastEdit);
        }

        public byte[] LoadChallengeFileContent(string challengeId, string fileName)
        {
            var pathToFile = $"{GetPathToChallenge(challengeId)}\\{fileName}";
            var res = LoadFile(pathToFile);
            return res.Item2;
        }

        public string GetChallengeAdditionalFileContentAsText(string challengeName, string fileName)
        {
            var pathToFile = Path.Combine(GetPathToChallenge(challengeName), fileName);
            return File.ReadAllText(pathToFile);
        }

        public void WriteChallengeAdditionalFileContent(string challengeName, string fileName, string content)
        {
            var pathToFile = Path.Combine(GetPathToChallenge(challengeName), fileName);
            File.WriteAllText(pathToFile, content);
        }

        public string GetMimeTypeForFile(string fileName)
        {
            return MimeTypeMap.GetMimeType(Path.GetExtension(fileName));
        }

        public string GetLastVersionHash()
        {
            if (File.Exists(GetPathForLastVersion()))
            {
                return File.ReadAllText(GetPathForLastVersion());
            }

            return "";
        }

        public void SaveLastVersionHash(string version)
        {
            File.WriteAllText(GetPathForLastVersion(), version);
        }

        public IEnumerable<Group> LoadAllGroups()
        {
            var dir = GetPathToGroups();
            foreach (var file in Directory.EnumerateFiles(dir, "*.yml"))
            {
                var group = LoadGroup(Path.GetFileNameWithoutExtension(file));
                yield return group;
            }
        }

        public Group LoadGroup(string id, IWriteLock writeLock = null)
        {
            var path = Path.Combine(GetPathToGroups(), id + ".yml");
            if (writeLock is WriteLock w)
            {
                w.Add(path);
            }

            if (File.Exists(path))
            {
                var group = yamlProvider.Deserialize<Group>(path);

                group.Id = id;
                return group;
            }
            else
            {
                return null;
            }
        }

        public void SaveGroup(Group group, IWriteLock writeLock)
        {
            var path = Path.Combine(GetPathToGroups(), group.Id + ".yml");
            writeLock.EnsureWriteLock(path);
            yamlProvider.Serialize(path, group);
        }

        public void CreateMember(Member member)
        {
            var file = GetPathToMember(member.Id);
            using var writeLock = (WriteLock) GetLock();
            writeLock.Add(file);
            if (File.Exists(file))
            {
                throw new InvalidOperationException("User mit ID already angelegt");
            }

            var groups = LoadAllGroups();
            if (groups.Count() == 1)
            {
                member.Groups = groups.Select(x => x.Id).ToArray();
            }

            SaveMember(member, writeLock);
        }

        public void DeleteMember(IMember member, IWriteLock writeLock)
        {
            var path = GetPathToMember(member.Id);
            ((WriteLock) writeLock).Add(path);
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }

        public IWriteLock GetLock()
        {
            return new WriteLock();
        }

        public IEnumerable<Bundle> LoadAllBundles()
        {
            foreach (var file in Directory.EnumerateFiles(GetPathToBundles(), "*.md"))
            {
                yield return LoadBundle(Path.GetFileNameWithoutExtension(file));
            }
        }

        public void CreateBundle(string id, string title, string description, string authorId, string category, IEnumerable<string> challenges)
        {
            var bundle = new Bundle
            {
                Id = id,
                Title = title,
                Description = description,
                Author = authorId,
                Category = category,
                IsDraft = true,
                Challenges = challenges.ToList()
            };

            using var writeLock = (WriteLock) GetLock();
            var file = Path.Combine(GetPathToBundles(), bundle.Id + ".md");
            writeLock.Add(file);
            SaveBundle(bundle, writeLock);
        }

        public Bundle LoadBundle(string id, IWriteLock writeLock = null)
        {
            var file = Path.Combine(GetPathToBundles(), id + ".md");
            if (writeLock is WriteLock w)
            {
                w.Add(file);
            }

            var bundle = yamlProvider.DeserializeWithDescription<Bundle>(file);

            bundle.Id = id;
            if (bundle.Challenges == null)
            {
                bundle.Challenges = new List<string>();
            }

            return bundle;
        }

        public void SaveBundle(Bundle bundle, IWriteLock writeLock)
        {
            var file = Path.Combine(GetPathToBundles(), bundle.Id + ".md");
            writeLock.EnsureWriteLock(file);
            yamlProvider.SerializeWithDescription(file, bundle);
        }

        public void ChangeChallengeId(IChallenge challenge, string newId)
        {
            using var writeLock = (WriteLock) GetLock();
            var path = GetPathToChallenge(challenge.Id);
            writeLock.Add(path);
            var newpath = GetPathToChallenge(newId);
            //Due the API is case-insensitive in some cases you need to rename the folder first to something else,
            //before you can actually rename it to the same name with other case, preventing an exception.
            //TODO: Find out, if this is actually a challenge-Id no one will choose.
            if (challenge.Id.ToLower().Equals(newId.ToLower()) && !challenge.Id.Equals(newId))
            {
                var tempPath = GetPathToChallenge("$renameTemp");
                Directory.Move(path, tempPath);
                Directory.Move(tempPath, newpath);
            }
            else
            {
                Directory.Move(path, newpath);
            }
        }

        public void ChangeGroupId(IGroup group, string newId)
        {
            using var writeLock = (WriteLock) GetLock();
            var path = GetPathToGroup(@group.Id);
            writeLock.Add(path);
            var newpath = GetPathToGroup(newId);
            //Due the API is case-insensitive in some cases you need to rename the file first to something else,
            //before you can actually rename it to the same name with other case, preventing an exception.
            //TODO: Find out, if this is actually a challenge-Id no one will choose.
            if (@group.Id.ToLower().Equals(newId.ToLower()) && !@group.Id.Equals(newId))
            {
                var tempPath = GetPathToGroup("$renameTemp");
                File.Move(path, tempPath);
                File.Move(tempPath, newpath);
            }
            else
            {
                File.Move(path, newpath);
            }
        }

        public void MoveChallengeSubmissionTo(IChallenge challenge, string newId)
        {
            if (Directory.Exists(challenge.Id))
            {
                Directory.Move(GetPathToSubmissionsFor(challenge.Id), GetPathToSubmissionsFor(newId));
            }
        }

        public void CreateGroup(string id, string title, List<string> groupAdminIds, bool isSuperGroup, string[] subGroups, string[] forcedChallenges,
            string[] availableChallenges, int maxUnlockedChallenges, int? requiredPoints, DateTime? startDate, DateTime? endDate)
        {
            using var writeLock = (WriteLock) GetLock();
            var group = new Group
            {
                Id = id,
                Title = title,
                GroupAdminIds = groupAdminIds,
                ForcedChallenges = forcedChallenges,
                AvailableChallenges = availableChallenges,
                MaxUnlockedChallenges = maxUnlockedChallenges,
                RequiredPoints = requiredPoints,
                StartDate = startDate,
                EndDate = endDate,
                IsSuperGroup = isSuperGroup,
                SubGroups = subGroups
            };
            writeLock.Add(Path.Combine(GetPathToGroups(), group.Id + ".yml"));
            SaveGroup(group, writeLock);
        }

        public void DeleteGroup(string id)
        {
            using var writeLock = (WriteLock) GetLock();
            var path = Path.Combine(GetPathToGroups(), id + ".yml");
            writeLock.Add(path);
            File.Delete(path);
        }

        public bool IsMaintenanceMode { get => WriteLock.IsMaintenanceMode; set => WriteLock.IsMaintenanceMode = value; }

        public IEnumerable<(string name, byte[] data)> GetZipFiles(byte[] data)
        {
            using var archive = new ZipArchive(new MemoryStream(data));
            foreach (var entry in archive.Entries)
            {
                using var dataStream = entry.Open();
                var fileData = new byte[entry.Length];
                dataStream.Read(fileData, 0, fileData.Length);
                yield return (entry.FullName, fileData);
            }
        }

        public (Challenge, IEnumerable<TestParameters>, IEnumerable<(string name, byte[] data)>) LoadChallengeFromZip(byte[] data)
        {
            var files = GetZipFiles(data).ToDictionary(x => Path.GetFileName(x.name), x => (x.name, x.data));

            var id = ReadString(files["id.txt"].data);

            var challenge = yamlProvider.DeserializeFromTextWithDescription<Challenge>(ReadString(files["challenge.md"].data));
            challenge.Id = id;

            var tests = yamlProvider.DeserializeTestsFromText("", ReadString(files["_testParameters.yml"].data));

            var additionalFiles = files.Where(x => x.Key != "id.txt" && x.Key != "challenge.md" && x.Key != "_testParameters.yml").Select(x => x.Value)
                .ToList();
            return (challenge, tests, additionalFiles);
        }

        public bool ChallengeExists(string challengeId)
        {
            return File.Exists(GetPathToChallengeProperties(challengeId));
        }

        private string GetPathToChallenges()
        {
            return Path.Combine(challengesDir, "_challenges");
        }

        private string GetPathToHelpPages()
        {
            return Path.Combine(challengesDir, "_help");
        }

        private string GetPathToChallenge(string challenge)
        {
            return Path.Combine(GetPathToChallenges(), challenge);
        }

        public string GetPathToMember(string id)
        {
            return Path.Combine(GetPathToMembers(), $"{id}.yml");
        }

        private string GetPathToReviewData(ISubmission submission)
        {
            var submissionPath = GetSubmissionPath(submission);
            return Path.Combine(submissionPath, "review", "review_result.yml");
        }

        private string GetPathToJekyllData()
        {
            return Path.Combine(challengesDir, "_data");
        }

        private string GetPathToSubmissions()
        {
            return Path.Combine(challengesDir, "_submissions");
        }

        private string GetPathToSubmissionsFor(string challenge)
        {
            return Path.Combine(GetPathToSubmissions(), challenge);
        }

        private string GetPathToMembers()
        {
            return Path.Combine(challengesDir, "_members");
        }

        private string GetPathToBundles()
        {
            return Path.Combine(challengesDir, "_bundles");
        }

        private string GetPathToGroups()
        {
            return Path.Combine(challengesDir, "_groups");
        }

        private string GetRelativePath(string path, string folder)
        {
            if (!folder.EndsWith(Path.DirectorySeparatorChar.ToString()))
            {
                folder += Path.DirectorySeparatorChar;
            }

            var folderUri = new Uri(folder);
            var pathUri = new Uri(path);
            return Uri.UnescapeDataString(folderUri.MakeRelativeUri(pathUri).ToString()).Replace('/', Path.DirectorySeparatorChar);
        }

        private string GetHash(byte[] data)
        {
            using var md5 = new MD5CryptoServiceProvider();
            var md5Submission = md5.ComputeHash(data);
            return ByteArrayToHex(md5Submission);
        }

        private string GetUniqueSubmissionPath(string challengeName)
        {
            var challengeDir = Path.Combine(GetPathToSubmissions(), challengeName);

            var ctr = 100000;
            while (true)
            {
                var path = Path.Combine(challengeDir, ctr.ToString());
                if (!Directory.Exists(path))
                {
                    Directory.CreateDirectory(path);
                    return path;
                }

                ctr++;
            }
        }

        private static string ByteArrayToHex(byte[] ba)
        {
            return BitConverter.ToString(ba).Replace("-", "");
        }

        private string GetTestFilePath(string challenge, string testfile)
        {
            return Path.Combine(GetPathToChallenge(challenge), testfile);
        }

        private void SaveAttachementAsSourceFile(string submissionPath, byte[] zipData)
        {
            foreach (var file in Directory.EnumerateFiles(submissionPath, "Source.*", SearchOption.TopDirectoryOnly))
            {
                File.Delete(file);
            }

            var sourcePathForSubmission = GetSourceZipPath(submissionPath);
            File.WriteAllBytes(sourcePathForSubmission, zipData);
        }

        private string GetSourceZipPath(string basePath)
        {
            return Path.Combine(basePath, "Source.zip");
        }

        private string GetRecentActivityFile()
        {
            return Path.Combine(GetPathToJekyllData(), "activity.yml");
        }


        private IEnumerable<string> GetSubmissionsWithResult(string challengeName)
        {
            var path = Path.Combine(GetPathToSubmissions(), challengeName);
            if (Directory.Exists(path))
            {
                foreach (var dir in Directory.GetDirectories(path))
                {
                    var result = Path.Combine(dir, "result.yml");
                    if (File.Exists(result))
                    {
                        yield return result;
                    }
                }
            }
        }

        public string GetSubmissionResultFile(string submissionPath)
        {
            return Path.Combine(submissionPath, "result.yml");
        }

        private string GetSubmissionResultFile(string challenge, string id)
        {
            return GetSubmissionResultFile(Path.Combine(GetPathToSubmissions(), challenge, id));
        }

        private string GetGlobalRankingPath()
        {
            return Path.Combine(GetPathToJekyllData(), "globalRanking.yml");
        }

        private string GetSemesterRankingPath()
        {
            return Path.Combine(GetPathToJekyllData(), "semester_rankings");
        }

        private string GetExtractPathForZip(string pathToZip)
        {
            if (!File.Exists(pathToZip))
            {
                throw new IOException($"Following .zip file could not be found: {pathToZip}");
            }

            return Path.Combine(Path.GetDirectoryName(pathToZip), "extracted");
        }

        private void ClearTargetPath(string extractPath)
        {
            DeleteDirectory(extractPath);
        }

        private void ExtractArchiveFileTo(string pathToArchive, string extractPath)
        {
            using (var stream = File.OpenRead(pathToArchive))
            {
                using var archive = new ZipArchive(stream);
                if (Directory.Exists(extractPath))
                {
                    Directory.Delete(extractPath, true);
                }

                archive.ExtractToDirectory(extractPath, true);
                if (!Directory.Exists(extractPath))
                {
                    throw new Exception("Entpacken der Daten fehlgeschlagen. Ungültiges Zip?");
                }
            }

            log.Information("Erfolgreich entpackt: {pfad}", extractPath);
        }

        private string GetAwardsFile()
        {
            return Path.Combine(GetPathToJekyllData(), "awards.yml");
        }

        private IEnumerable<byte[]> ExtractFilesFromZip(byte[] data, IEnumerable<string> files)
        {
            using var stream = new MemoryStream(data);
            using var archive = new ZipArchive(stream);
            foreach (var file in files)
            {
                var entry = archive.GetEntry(file);
                var raw = new byte[entry.Length];
                using (var reader = entry.Open())
                {
                    reader.Read(raw, 0, raw.Length);
                }

                yield return raw;
            }
        }

        private string GetSubmissionPath(ISubmission submission)
        {
            return ((Result) submission).SubmissionPath;
        }

        private void PruneEmptyDirs(string path)
        {
            foreach (var directory in Directory.GetDirectories(path))
            {
                PruneEmptyDirs(directory);
                if (!Directory.EnumerateFileSystemEntries(directory).Any())
                {
                    Directory.Delete(directory, false);
                }
            }
        }

        public string GetReviewTemplatePath(string template)
        {
            return Path.Combine(GetPathToJekyllData(), "reviews", template + ".yml");
        }

        private HelpPage DeserializeHelpPage(string path)
        {
            var helpPage = yamlProvider.DeserializeWithDescription<HelpPage>(path);
            var withoutMd = Path.Combine(Path.GetDirectoryName(path), Path.GetFileNameWithoutExtension(path));
            helpPage.Path = GetRelativePath(withoutMd, GetPathToHelpPages());
            return helpPage;
        }

        private void SerializeHelpPage(HelpPage helpPage, IWriteLock writeLock)
        {
            var path = Path.Combine(GetPathToHelpPages(), helpPage.Path + ".md");
            writeLock.EnsureWriteLock(path);
            yamlProvider.SerializeWithDescription(path, helpPage);
        }

        private void DeleteDirectory(string path)
        {
            IOException lastException = null;
            for (var turn = 1; turn <= 10; turn++)
            {
                if (!Directory.Exists(path))
                {
                    return;
                }

                try
                {
                    Directory.Delete(path, true);
                }
                catch (IOException ex)
                {
                    lastException = ex;
                    Thread.Sleep(50);
                    continue;
                }

                return;
            }

            if (lastException != null)
            {
                throw lastException;
            }
        }


        private Result DeserializeResult(string path)
        {
            var result = yamlProvider.Deserialize<Result>(path);
            result.SubmissionPath = Path.GetDirectoryName(path);
            var directoryInfo = new DirectoryInfo(result.SubmissionPath ?? string.Empty);
            result.SubmissionId = directoryInfo.Name;
            result.Challenge = directoryInfo.Parent?.Name;
            result.HasReviewData = File.Exists(GetPathToReviewData(result));
            return result;
        }

        private void SaveResult(Result result, IWriteLock writeLock)
        {
            writeLock.EnsureWriteLock(result.SubmissionPath);
            var resultFile = GetSubmissionResultFile(result.SubmissionPath);
            var oldFile = resultFile[0..^4] + ".old";
            var newFile = resultFile[0..^4] + ".new";
            yamlProvider.Serialize(newFile, result, false);
            if (File.Exists(oldFile))
            {
                File.Delete(oldFile);
            }

            if (File.Exists(resultFile))
            {
                File.Move(resultFile, oldFile);
            }

            File.Move(newFile, resultFile);
        }

        private (string, byte[], string, DateTime) LoadFile(string pathToFile)
        {
            return (Path.GetFileName(pathToFile), File.ReadAllBytes(pathToFile), GetMimeTypeForFile(pathToFile), File.GetLastWriteTime(pathToFile));
        }


        private string GetPathForLastVersion()
        {
            return Path.Combine(GetPathToJekyllData(), "version.txt");
        }

        private string GetPathToGroup(string id)
        {
            var path = GetPathToGroups();
            return Path.Combine(path, id + ".yml");
        }

        private string ReadString(byte[] data)
        {
            using var ms = new StreamReader(new MemoryStream(data));
            return ms.ReadToEnd();
        }

        private class WriteLock : IWriteLock
        {
            private static readonly Dictionary<string, EventWaitHandle> handles = new Dictionary<string, EventWaitHandle>();

            public static volatile bool IsMaintenanceMode;
            private readonly HashSet<string> locked = new HashSet<string>();
            private volatile bool disposed;

            public void Dispose()
            {
                Dispose(true);
            }

            public void EnsureWriteLock(string path)
            {
                if (disposed)
                {
                    throw new SynchronizationLockException("Already disposed");
                }

                if (IsMaintenanceMode)
                {
                    throw new SynchronizationLockException("System ist im Wartungsmodus");
                }

                lock (locked)
                {
                    if (!locked.Contains(path))
                    {
                        throw new SynchronizationLockException("Write lock for " + path + " not acquired");
                    }
                }
            }

            ~WriteLock()
            {
                Dispose(false);
            }

            private void Dispose(bool disposing)
            {
                disposed = true;
                lock (locked)
                {
                    foreach (var writeLock in locked)
                    {
                        GetHandle(writeLock).Set();
                    }

                    locked.Clear();
                }

                if (disposing)
                {
                    GC.SuppressFinalize(this);
                }
            }

            public void Add(string path)
            {
                if (disposed)
                {
                    throw new SynchronizationLockException("Already disposed");
                }

                if (IsMaintenanceMode)
                {
                    throw new SynchronizationLockException("System ist im Wartungsmodus");
                }

                if (locked.Contains(path))
                {
                    return;
                }

                if (!GetHandle(path).WaitOne(TimeSpan.FromSeconds(60)))
                {
                    throw new SynchronizationLockException("Failed to acquire lock for: " + path);
                }

                lock (locked)
                {
                    locked.Add(path);
                }
            }

            private EventWaitHandle GetHandle(string path)
            {
                lock (handles)
                {
                    if (handles.TryGetValue(path, out var handle))
                    {
                        return handle;
                    }

                    handle = new EventWaitHandle(true, EventResetMode.AutoReset);
                    handles[path] = handle;
                    return handle;
                }
            }
        }
    }
}
