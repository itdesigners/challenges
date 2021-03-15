using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using SubmissionEvaluation.Contracts.Data;
using SubmissionEvaluation.Contracts.Data.Ranklist;
using SubmissionEvaluation.Contracts.Data.Review;
using SubmissionEvaluation.Contracts.Exceptions;
using SubmissionEvaluation.Domain.Operations;

namespace SubmissionEvaluation.Domain
{
    public class Query
    {
        private readonly Domain domain;

        public Query(Domain domain)
        {
            this.domain = domain;
        }

        public ReviewData GetReviewData(ISubmission submission)
        {
            domain.Log.Information("Query: Abfrage Review {id}", submission.SubmissionId);
            return domain.ProviderStore.FileProvider.LoadReview(submission);
        }

        public ReviewRating GetReviewRating(ReviewData data)
        {
            domain.Log.Information("Query: Abfrage Review Rating {id}", data.Id);
            return domain.ReviewOperations.GetReviewRating(data);
        }

        public List<Result> GetAllReviewableSubmissions(IMember member)
        {
            domain.Log.Information("Query: Abfrage aller verfügbaren Reviews für {member}", member.Name);
            var reviewableSubmissions = domain.ReviewOperations.ReviewableSubmissions(member);
            return reviewableSubmissions.Where(x => x.ReviewState == ReviewStateType.NotReviewed && x.MemberId != member.Id).ToList();
        }

        public List<Result> GetOutstandingReviewSubmissions(IMember member)
        {
            domain.Log.Information("Query: Abfrage aller ausstehenden Reviews von {member}", member.Name);
            var reviewableSubmissions = domain.ReviewOperations.ReviewableSubmissions();
            return reviewableSubmissions.Where(x => x.Reviewer == member.Id && x.ReviewState == ReviewStateType.InProgress).OrderBy(x => x.ReviewDate).ToList();
        }

        public ReviewTemplate GetReviewTemplate(string challenge)
        {
            domain.Log.Information("Query: Abfrage des Review Templates für {challenge}", challenge);
            return domain.ReviewOperations.LoadReviewTemplateForChallenge(challenge);
        }

        public IReadOnlyList<ISubmission> GetSubmissionsForUser(IMember member, IChallenge challenge)
        {
            domain.Log.Information("Query: Abfrage der Submissions von {member} für {challenge}", member.Name, challenge.Id);
            var submissions = domain.ProviderStore.FileProvider.LoadAllSubmissionsFor(challenge, true).Where(x => x.MemberId == member.Id)
                .OrderByDescending(x => x.SubmissionDate);
            return submissions.ToList();
        }

        public IReadOnlyList<ISubmission> GetAllSubmissionsFor(IChallenge challenge)
        {
            domain.Log.Information("Query: Abfrage der Submissions für {challenge}", challenge.Id);
            return domain.ProviderStore.FileProvider.LoadAllSubmissionsFor(challenge).ToList();
        }

        public int GetAvailableChallengeCount()
        {
            domain.Log.Information("Query: Abfrage der Anzahl der Challenges");
            return domain.ProviderStore.FileProvider.LoadChallenges().Count(p => p.IsAvailable);
        }

        public bool TryGetBundleForChallenge(IMember member, string challengeId, out IBundle bundle)
        {
            domain.Log.Information("Query: Abfrage des Bundles für Challenge {challenge}", challengeId);
            bundle = domain.Query.GetAllBundles(member).FirstOrDefault(x => x.Challenges.Contains(challengeId));
            return bundle != null;
        }

        public List<string> GetCompilerNames()
        {
            domain.Log.Information("Query: Abfrage der verfügbaren Kompiler");
            return domain.Compilers.Where(p => p.Available).Select(x => x.Name).ToList();
        }

        public SubmitterSolvedList GetSubmitterSolvedList(IMember member)
        {
            domain.Log.Information("Query: Abfrage der gelösten Aufgaben von {id}", member.Name);
            var solved = new Dictionary<string, SolvedInfoForChallenge>();
            var submissions = domain.ProviderStore.FileProvider.LoadAllSubmissionsFor(member, true);
            var challenges = submissions.Where(x => x.Language != null).GroupBy(x => x.Challenge);

            foreach (var challengeSubmissions in challenges)
            {
                var solvedInfoForChallenge = new SolvedInfoForChallenge();
                var languages = challengeSubmissions.GroupBy(x => x.Language);
                foreach (var languageSubmissions in languages)
                {
                    var solvedInfo = new SolvedInfo();
                    if (languageSubmissions.Any(x => x.IsPassed))
                    {
                        solvedInfo.State = SolvedState.Solved;
                        solvedInfo.Stars = languageSubmissions.Where(x => x.IsPassed).Max(p => p.ReviewRating);
                    }
                    else
                    {
                        solvedInfo.State = SolvedState.Failed;
                    }

                    solvedInfoForChallenge.Add(languageSubmissions.Key, solvedInfo);
                }

                solved.Add(challengeSubmissions.Key, solvedInfoForChallenge);
            }

            return new SubmitterSolvedList {Id = member.Id, Solved = solved};
        }

        public SubmitterHistory GetSubmitterHistory(IMember member)
        {
            domain.Log.Information("Query: Abfrage der Historie von {id}", member.Name);
            return domain.StatisticsOperations.BuildSubmitterHistory(member);
        }

        public byte[] GetSourceForSubmission(ISubmission submission)
        {
            domain.Log.Information("Query: Abfrage des Quellcodes für {id}", submission.SubmissionId);
            var pathToSource = domain.ProviderStore.FileProvider.GetSourceZipPathForSubmission(submission);
            return File.ReadAllBytes(pathToSource);
        }

        public IReadOnlyList<ITest> GetTests(IChallenge challengeProps)
        {
            domain.Log.Information("Query: Abfragen der Tests für {id}", challengeProps.Id);
            return domain.ProviderStore.FileProvider.LoadTestProperties((Challenge) challengeProps).ToList();
        }

        public IChallenge GetChallenge(IMember member, string challengeId, bool includingAdditionalFiles = false)
        {
            domain.Log.Information("Query: Abfragen der Aufgabenbeschreibung von {id} für {member}", challengeId, member?.Name);
            var challenge = domain.ProviderStore.FileProvider.LoadChallenge(challengeId);
            if (!ChallengeOperations.CanAccessChallenge(member, challenge, domain.ProviderStore.FileProvider))
            {
                throw new ChallengeLockedForUserException($"Access to challenge {challengeId} for user {member.Id} not allowed!");
            }

            if (includingAdditionalFiles)
            {
                challenge.AdditionalFiles = domain.ProviderStore.FileProvider.GetAdditionalFiles(challenge).ToList();
            }

            return challenge;
        }

        public IReadOnlyList<IChallenge> GetAllChallenges(IMember member, bool containUnavailable = false)
        {
            domain.Log.Information("Query: Abfragen aller verfügbarer Aufgaben für {member}", member.Name);
            return ChallengeOperations.GetAllChallengesForMember(member, domain.ProviderStore.FileProvider, containUnavailable);
        }

        public IReadOnlyList<IChallenge> GetAllChallenges(bool containUnavailable = false)
        {
            domain.Log.Information("Query: Abfragen aller verfügbarer Aufgaben");
            return ChallengeOperations.GetAllChallengesForMember(domain.ProviderStore.FileProvider, containUnavailable);
        }
        public ISubmission GetSubmission(string challenge, string id)
        {
            domain.Log.Information("Query: Abfragen der Einreichung {id}", id);
            return domain.ProviderStore.FileProvider.LoadResult(challenge, id);
        }

        public GlobalRanklist GetGlobalRanklist()
        {
            domain.Log.Information("Query: Abfrage der Rangliste");
            return domain.ProviderStore.FileProvider.LoadGlobalRanklist();
        }

        public GlobalRanklist GetCurrentSemesterRanklist()
        {
            domain.Log.Information("Query: Abfrage der Semesterrangliste");
            return domain.ProviderStore.FileProvider.LoadAllSemesterRanklists().FirstOrDefault(p =>
                p.CurrentSemester?.FirstDay <= DateTime.Now && p.CurrentSemester?.LastDay >= DateTime.Now) ?? new GlobalRanklist();
        }

        public GlobalSubmitter GetGlobalSubmitter(IMember member)
        {
            domain.Log.Information("Query: Abfrage der Punkte von {id}", member.Name);
            var globalRanklist = domain.ProviderStore.FileProvider.LoadGlobalRanklist();
            var globalSubmitter = globalRanklist.Submitters.FirstOrDefault(p => p.Id == member.Id);
            return globalSubmitter ?? new GlobalSubmitter {Id = member.Id};
        }

        public List<HintCategory> GetFailedChallengeSubmissionReport(ISubmission submission)
        {
            domain.Log.Information("Query: Abfragen des Fehlerberichts von {id}", submission.SubmissionId);
            return domain.ProviderStore.FileProvider.LoadFailedSubmissionReport((Result) submission);
        }

        public IEnumerable<string> GetSubmissionRelativeFilesPath(ISubmission submission)
        {
            domain.Log.Information("Query: Abfragen des Quellcodepfade von {id}", submission.SubmissionId);
            var files = domain.ProviderStore.FileProvider.GetSubmissionFilesRelativePath(submission);
            var compiler = domain.CompilerOperations.GetCompilerForContent(files);
            var cleanedFiles = compiler.GetSourceFiles(files);
            return cleanedFiles;
        }

        public string GetSubmissionSourceCode(ISubmission submission, string relativeFilePath)
        {
            return domain.ProviderStore.FileProvider.GetSubmissionFileContent(submission, relativeFilePath);
        }

        public (string name, byte[] data, string type, DateTime lastMod) GetChallengeAdditionalFile(string challengeName, string fileName)
        {
            domain.Log.Information("Query: Abfragen des Files {id} der Challenge {challengeName}", fileName, challengeName);
            return domain.ProviderStore.FileProvider.GetChallengeAdditionalFile(challengeName, fileName);
        }

        public (string name, byte[] data, string type, DateTime lastMod) GetChallengeZip(string challengeName)
        {
            domain.Log.Information("Query: Abfragen des Zips der Challenge {challengeName}", challengeName);
            return domain.ProviderStore.FileProvider.GetChallengeZip(domain.ProviderStore.FileProvider.LoadChallenge(challengeName));
        }

        public string GetChallengeAdditionalFileContentAsText(string challengeName, string fileName)
        {
            domain.Log.Information("Query: Abfragen des Files {id} der Challenge {challengeName}", fileName, challengeName);
            return domain.ProviderStore.FileProvider.GetChallengeAdditionalFileContentAsText(challengeName, fileName);
        }

        public string GetContentTypeForFile(string fileName)
        {
            domain.Log.Information("Query: Abfragen des Files-ContentType {path}", fileName);
            return domain.ProviderStore.FileProvider.GetMimeTypeForFile(fileName);
        }

        public byte[] ExportSolutionsAsZip()
        {
            domain.Log.Information("Query: Export der Lösungen als Zip");
            using var zip = domain.ProviderStore.FileProvider.CreateZipInMemory(new (string, string)[0]);
            foreach (var challenge in domain.ProviderStore.FileProvider.GetChallengeIds().Select(x => domain.ProviderStore.FileProvider.LoadChallenge(x)))
            {
                var ctr = 1;
                var results = domain.ProviderStore.FileProvider.LoadAllSubmissionsFor(challenge);
                var passed = results.Where(x => x.IsPassed).OrderByDescending(x => x.LastSubmissionDate);
                var lastPerUser = passed.GroupBy(x => x.MemberId).Select(x => x.First()).ToList();
                if (lastPerUser.Count > 0)
                {
                    foreach (var result in lastPerUser)
                    {
                        try
                        {
                            var pathName = $"{challenge.Id}\\{ctr++:000}";
                            var pathToSourceZip = domain.ProviderStore.FileProvider.GetSourceZipPathForSubmission(result);
                            var pathToContent = domain.ProviderStore.FileProvider.ExtractContent(pathToSourceZip, false);
                            var compiler = domain.CompilerOperations.GetCompilerForContent(pathToContent);

                            var sourceFiles = compiler.GetSourceFiles(pathToContent);
                            foreach (var file in sourceFiles)
                            {
                                var data = File.ReadAllBytes(file);
                                domain.ProviderStore.FileProvider.AddFileToZip(zip, data, $"{pathName}\\{Path.GetFileName(file)}");
                            }
                        }
                        catch (Exception e)
                        {
                            domain.Log.Error(e, "Kann Quellcode nicht zu Sources.zip hinzufügen ({challenge}/{id})", result.Challenge, result.SubmissionId);
                        }
                    }
                }
            }

            zip.Flush();
            return zip.ToArray();
        }

        public IEnumerable<Result> GetRunningReviews()
        {
            domain.Log.Information("Query: Abfrage aller laufenden Reviews");
            var reviewableSubmissions = domain.ReviewOperations.ReviewableSubmissions();
            return reviewableSubmissions.Where(x => x.ReviewState == ReviewStateType.InProgress).OrderBy(x => x.ReviewDate).ToList();
        }

        public IEnumerable<IBundle> GetAllBundles(IMember member, bool includeDraft = false)
        {
            domain.Log.Information("Query: Alle Bundle abrufen für {member}", member.Name);
            return ChallengeOperations.GetAllBundles(member, includeDraft, domain.ProviderStore.FileProvider);
        }

        public IBundle GetBundle(IMember member, string id)
        {
            domain.Log.Information("Query: Bundle {id} abrufen für {member}", id, member.Name);
            return ChallengeOperations.GetBundle(member, id, domain.ProviderStore.FileProvider);
        }

        public bool HasMemberSolvedAllPreviousChallengesInBundle(IMember member, IChallenge challenge)
        {
            domain.Log.Information("Query: Abfragen ob {member} alle vorherigen Aufgaben für {challenge} gelöst hat", member.Name, challenge.Id);
            if (TryGetBundleForChallenge(member, challenge.Id, out var bundle))
            {
                var indexOfCurrentChallenge = bundle.Challenges.IndexOf(challenge.Id);
                var previousChallenges = bundle.Challenges.Take(indexOfCurrentChallenge);
                var hasSolved = true;
                foreach (var prevChallenge in previousChallenges)
                {
                    var hasPassedChallenge = member.SolvedChallenges.Contains(prevChallenge);
                    hasSolved = hasSolved && hasPassedChallenge;
                }

                return hasSolved;
            }

            return true;
        }

        public IReadOnlyCollection<Award> GetAwardsFor(IMember member)
        {
            domain.Log.Information("Query: Abfragen der Achivements für {member}", member.Name);
            return domain.ProviderStore.FileProvider.LoadAwards()[member.Id];
        }

        public SubmitterRankings GetSubmitterRanklist(IMember member)
        {
            domain.Log.Information("Query: Abfragen der Rangliste für {member}", member.Name);
            var ranklists = domain.StatisticsOperations.GenerateAllChallengeRanklists();
            var result = domain.StatisticsOperations.BuildSubmitterRanklist(ranklists).Find(p => p.Name == member.Id);
            return result ?? new SubmitterRankings {Name = member.Id, Submissions = new List<SubmitterRankingEntry>()};
        }

        public List<Activity> GetRecentActivities()
        {
            return domain.ProviderStore.FileProvider.LoadRecentActivities();
        }

        public ChallengeRanklist GetChallengeRanklist(IChallenge challenge)
        {
            domain.Log.Information("Query: Abfragen der Rangliste für {challenge}", challenge.Id);
            return domain.StatisticsOperations.GenerateChallengeRanklist(challenge);
        }

        public Dictionary<string, List<IElement>> GetCategoryStats(IMember member)
        {
            domain.Log.Information("Query: Abfragen der Kategoriestatistiken durch {member}", member.Name);
            var challenges = ChallengeOperations.GetAllChallengesForMember(member, domain.ProviderStore.FileProvider, false)
                .Where(x => !x.State.IsPartOfBundle);
            var bundles = ChallengeOperations.GetAllBundles(member, false, domain.ProviderStore.FileProvider);

            var challengeElements = challenges.Select(x => (IElement) new ChallengeElement(x));
            var bundleElements = bundles.Select(x => new BundleElement(x));
            var elements = challengeElements.Concat(bundleElements).OrderByDescending(x => x.Activity);
            return elements.GroupBy(x => x.Category).ToDictionary(x => x.Key, x => x.Take(8).ToList());
        }

        public void StartReview(ISubmission submission, IMember member)
        {
            domain.Log.Information("Query: Review starten für {submission} durch {member}", submission.SubmissionId, member.Id);
            domain.ReviewOperations.SetReviewStateAsStarted(submission, member.Id);
        }

        public HelpPage GetHelpPage(string path)
        {
            if (path.StartsWith("Compiler/"))
            {
                var compilerName = path.Split('/').Last();
                var compiler = domain.Compilers.FirstOrDefault(x => x.Name == compilerName);
                if (compiler == null)
                {
                    path = "Compilers";
                }
                else
                {
                    var description = compiler.VersionDetails + Environment.NewLine + Environment.NewLine + compiler.Description + Environment.NewLine;
                    if (compiler.CompilerSwitches?.Length > 0)
                    {
                        description += "Compiler Switches: `" + string.Join(" ", compiler.CompilerSwitches) + "`";
                    }

                    return new HelpPage {Description = description, Title = $"Compilers: {compiler.Name}"};
                }
            }

            if (path == "Compilers")
            {
                return new HelpPage
                {
                    Description = "Folgende Compiler stehen zur Verfügung: " + Environment.NewLine + string.Join(Environment.NewLine,
                        domain.Compilers.Select(x => $"* {x.Name} [{x.VersionDetails}]")),
                    Title = "Compilers"
                };
            }

            return domain.ProviderStore.FileProvider.LoadHelpPage(path);
        }

        public List<HelpPage> GetHelpHierarchy()
        {
            var help = domain.ProviderStore.FileProvider.GetHelpHierarchy();
            var compilerPage = new HelpPage {Title = "Compilers", Path = "Compilers"};
            help.Add(compilerPage);
            foreach (var compiler in domain.Compilers)
            {
                if (compiler.Available && (!string.IsNullOrWhiteSpace(compiler.Description) || compiler.CompilerSwitches?.Length > 0))
                {
                    compilerPage.SubPages.Add(new HelpPage
                    {
                        Title = compiler.Name, Parent = "Compilers", Path = "Compiler/" + WebUtility.UrlEncode(compiler.Name)
                    });
                }
            }

            return help;
        }

        public (string name, byte[] data, string type, DateTime lastMod) GetHelpAdditionalFile(string path)
        {
            domain.Log.Information("Query: Abfragen des Hilfe-Files {id}", path);
            return domain.ProviderStore.FileProvider.GetHelpAdditionalFile(path);
        }

        public string GetLastVersionHash()
        {
            domain.Log.Information("Query: Abfragen des letzten Version");
            return domain.ProviderStore.FileProvider.GetLastVersionHash();
        }

        public (ISubmission entry, ISubmission copySource) GetDuplicationSourceInfo(IMember member, string challengeName)
        {
            var challenge = domain.ProviderStore.FileProvider.LoadChallenge(challengeName);
            var fit = domain.ProviderStore.FileProvider.LoadAllSubmissionsFor(challenge).Where(x => x.IsPassed && x.MemberId == member.Id)
                .OrderByDescending(x => x.DuplicateScore).FirstOrDefault();
            try
            {
                var copy = fit?.DuplicateId != null ? domain.ProviderStore.FileProvider.LoadResult(challengeName, fit.DuplicateId) : null;
                return (fit, copy);
            }
            catch (Exception)
            {
                return (fit, null);
            }
        }

        public IEnumerable<Group> GetAllGroups()
        {
            return domain.ProviderStore.FileProvider.LoadAllGroups();
        }

        public RatingPoints GetRatingPoints(IChallenge challenge)
        {
            return StatisticsOperations.GetRatingPointsForChallenge(challenge);
        }

        public IGroup GetGroup(string id)
        {
            return domain.ProviderStore.FileProvider.LoadGroup(id);
        }
    }
}
