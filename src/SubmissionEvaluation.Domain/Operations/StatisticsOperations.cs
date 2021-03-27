using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Runtime.Caching;
using System.Threading.Tasks;
using SubmissionEvaluation.Contracts.Data;
using SubmissionEvaluation.Contracts.Data.Ranklist;
using SubmissionEvaluation.Contracts.Data.Review;
using SubmissionEvaluation.Contracts.Providers;
using SubmissionEvaluation.Domain.Comparers;

namespace SubmissionEvaluation.Domain.Operations
{
    internal class StatisticsOperations
    {
        private static MemoryCache cache = new MemoryCache("cache");

        public ILog Log { set; private get; }
        public ProviderStore ProviderStore { private get; set; }
        public IList<ISubmissionRater> Raters { private get; set; }
        public IMemberProvider MemberProvider { private get; set; }
        public List<ICompiler> Compilers { private get; set; }
        public IChallengeEstimator ChallengeEstimator { private get; set; }
        public AchievementOperations AchievementOperations { private get; set; }

        public void UpdateFeasibilityIndexForChallenge(Challenge challenge)
        {
            var oldFeasibilityIndex = challenge.State.FeasibilityIndex;

            const int maxFeasibilityIndex = 500000;
            var statePassedCount = challenge.State.PassedCount;
            if (challenge.State.FailedCount > 0 && statePassedCount >= 3)
            {
                challenge.State.FeasibilityIndex = 1000 * statePassedCount / challenge.State.FailedCount;
            }
            else if (statePassedCount >= 3)
            {
                challenge.State.FeasibilityIndex = 1000 * statePassedCount;
            }
            else
            {
                challenge.State.FeasibilityIndex = 0;
            }

            if (challenge.State.FeasibilityIndexMod != 0)
            {
                var diff = challenge.State.FeasibilityIndex + challenge.State.FeasibilityIndexMod - oldFeasibilityIndex;
                if (challenge.State.FeasibilityIndexMod > 0)
                {
                    if (diff > 0)
                    {
                        challenge.State.FeasibilityIndexMod = Math.Max(challenge.State.FeasibilityIndexMod - diff, 0);
                    }
                }
                else if (diff < 0)
                {
                    challenge.State.FeasibilityIndexMod = Math.Min(challenge.State.FeasibilityIndexMod - diff, 0);
                }

                challenge.State.FeasibilityIndex += challenge.State.FeasibilityIndexMod;
            }

            if (challenge.State.FeasibilityIndex > maxFeasibilityIndex)
            {
                challenge.State.FeasibilityIndex = maxFeasibilityIndex;
            }

            if (challenge.State.FeasibilityIndex < 0)
            {
                challenge.State.FeasibilityIndex = 0;
            }
        }

        public void UpdateGlobalRanking()
        {
            Log.Information("Globale Rankliste wird aktualisiert");
            var ranklists = GenerateAllChallengeRanklists();
            using (var writeLock = ProviderStore.FileProvider.GetLock())
            {
                var oldRanklist = ProviderStore.FileProvider.LoadGlobalRanklist(writeLock);
                var globalRanklist = BuildGlobalRanklist(ranklists, oldRanklist);
                ProviderStore.FileProvider.SaveGlobalRankingList(globalRanklist, writeLock);
            }

            var semesterRanklists = GenerateAllChallengeRanklistsForSemester();
            var oldSemesterRanklists = ProviderStore.FileProvider.LoadAllSemesterRanklists();
            var semesterRankList = BuildSemesterRanklist(semesterRanklists, oldSemesterRanklists);
            ProviderStore.FileProvider.SaveSemesterRankingList(semesterRankList);
        }

        public List<ChallengeRanklist> GenerateAllChallengeRanklists(bool includeSpecialRanklists = true)
        {
            if (cache.Contains(nameof(GenerateAllChallengeRanklists))) {
                return cache.Get(nameof(GenerateAllChallengeRanklists)) as List<ChallengeRanklist>; 
            }

            var allChallengeRanklists = GetBasicRanklists(includeSpecialRanklists);
            if (includeSpecialRanklists)
            {
                var reviewRanklist = GenerateReviewRanklist(ProviderStore.MemberProvider);
                allChallengeRanklists.Add(reviewRanklist);
            }

            var ranklist = allChallengeRanklists.OrderBy(x => x.Challenge, new ChallengeForRankingComparer()).ToList();
            cache.Set(nameof(GenerateAllChallengeRanklists), ranklist, DateTimeOffset.Now.AddSeconds(15));
            return ranklist;
        }

        public List<ChallengeRanklist> GenerateAllChallengeRanklistsForSemester(bool includeSpecialRanklists = true)
        {
            var allChallengeRanklists = GetBasicRanklists(includeSpecialRanklists);
            if (includeSpecialRanklists)
            {
                var reviewRanklist = GenerateSemesterReviewRanklist(ProviderStore.MemberProvider);
                allChallengeRanklists.Add(reviewRanklist);
            }

            return allChallengeRanklists.OrderBy(x => x.Challenge, new ChallengeForRankingComparer()).ToList();
        }

        protected List<ChallengeRanklist> GetBasicRanklists(bool includeSpecialRanklists = true)
        {
            var allChallenges = ProviderStore.FileProvider.GetChallengeIds().Select(p => ProviderStore.FileProvider.LoadChallenge(p));
            var allChallengeRanklists = allChallenges.Select(GenerateChallengeRanklist).ToList();
            if (includeSpecialRanklists)
            {
                var challengeCreatorRanklist = GenerateChallengeCreatorRanklist(ProviderStore.MemberProvider);
                var achievementRanklist = AchievementOperations.BuildAchievementsRanklist(ProviderStore.FileProvider);
                allChallengeRanklists.Add(challengeCreatorRanklist);
                allChallengeRanklists.Add(achievementRanklist);
            }

            return allChallengeRanklists;
        }

        internal SubmitterHistory BuildSubmitterHistory(IMember member, IEnumerable<Result> submissions = null)
        {
            IEnumerable<HistoryEntry> CreateEntries(Result result)
            {
                if (result.MemberId == member.Id)
                {
                    if (result.ReportFailing)
                    {
                        yield return new HistoryEntry
                        {
                            Challenge = result.Challenge,
                            Type = HistoryType.SubmissionNowFailing,
                            Language = result.Language,
                            Result = result.EvaluationResult,
                            Id = result.SubmissionId,
                            Date = result.LastTestrun
                        };
                    }

                    if (result.ReviewState == ReviewStateType.Reviewed)
                    {
                        yield return new HistoryEntry
                        {
                            Challenge = result.Challenge, Type = HistoryType.SubmissionRated, Date = result.ReviewDate.Value, Stars = result.ReviewRating
                        };
                    }

                    yield return new HistoryEntry
                    {
                        Challenge = result.Challenge,
                        Type = HistoryType.ChallengeSubmission,
                        Language = result.Language,
                        Result = result.EvaluationState == EvaluationState.NotEvaluated || result.EvaluationState == EvaluationState.RerunRequested
                            ? EvaluationResult.Undefined
                            : result.EvaluationResult,
                        Id = result.SubmissionId,
                        Date = result.SubmissionDate
                    };
                }

                if (result.ReviewState == ReviewStateType.InProgress && result.Reviewer == member.Id)
                {
                    yield return new HistoryEntry {Challenge = result.Challenge, Type = HistoryType.ReviewAvailable, Date = result.ReviewDate.Value};
                }
            }

            if (submissions == null)
            {
                submissions = ProviderStore.FileProvider.LoadAllSubmissions(true);
            }

            var entries = submissions.SelectMany(CreateEntries);
            entries = FilterUnnecessarySubmissionNowFailing(entries);
            return new SubmitterHistory {Id = member.Id, Entries = entries.OrderByDescending(x => x.Date).ToList()};
        }

        private IEnumerable<HistoryEntry> FilterUnnecessarySubmissionNowFailing(IEnumerable<HistoryEntry> entries)
        {
            var submissionNowFailing = entries.Where(p => p.Type == HistoryType.SubmissionNowFailing);
            var removedDoubleFailings = submissionNowFailing.GroupBy(p => p.Challenge).Select(p => p.OrderByDescending(q => q.Date).First());
            var buffer = entries.Where(p => p.Type != HistoryType.SubmissionNowFailing).ToList();
            var removeSolvedFailings = removedDoubleFailings.Where(p => !buffer.Any(q => q.Challenge == p.Challenge && q.IsPassed));
            buffer.AddRange(removeSolvedFailings);
            return buffer;
        }

        public List<SubmitterRankings> BuildSubmitterRanklist(List<ChallengeRanklist> ranklists)
        {
            var result = new ConcurrentDictionary<string, SubmitterRankings>();
            Parallel.ForEach(ranklists, ranklist =>
            {
                foreach (var submitter in ranklist.Submitters)
                {
                    if (!result.ContainsKey(submitter.Id))
                    {
                        result[submitter.Id] = new SubmitterRankings {Name = submitter.Id};
                    }

                    result[submitter.Id].Submissions.Add(new SubmitterRankingEntry
                    {
                        Challenge = ranklist.Challenge,
                        Rank = submitter.Rank,
                        Points = submitter.Points,
                        Language = submitter.Language,
                        DuplicateScore = submitter.DuplicateScore
                    });
                }
            });
            return result.Values.ToList();
        }

        public GlobalRanklist BuildGlobalRanklist(IEnumerable<ChallengeRanklist> ranklists, GlobalRanklist oldRanklist)
        {
            var submitters = ranklists.SelectMany(x => x.Submitters);
            var submittersGrouped = submitters.GroupBy(x => x.Id);
            var creatorsRanklist = ranklists.FirstOrDefault(x => x.Challenge == "ChallengeCreators") ?? new ChallengeRanklist();
            var authorsCount = creatorsRanklist.Submitters.GroupBy(x => x.Id).ToDictionary(x => x.Key, x => x.Count());
            return BuildRanklistFromSubmitters(submittersGrouped, authorsCount, oldRanklist, true);
        }

        private GlobalRanklist BuildSemesterRanklist(IEnumerable<ChallengeRanklist> ranklists, IEnumerable<GlobalRanklist> oldRanklists)
        {
            var submitters = ranklists.SelectMany(x => x.Submitters);
            var submittersGrouped = submitters.Where(x => DateInCurrentSemester(x.Date)).GroupBy(x => x.Id);
            var creatorsRanklist = ranklists.FirstOrDefault(x => x.Challenge == "ChallengeCreators") ?? new ChallengeRanklist();
            var authorsCount = creatorsRanklist.Submitters.Where(x => DateInCurrentSemester(x.Date)).GroupBy(x => x.Id)
                .ToDictionary(x => x.Key, x => x.Count());
            var oldRanklist = oldRanklists.FirstOrDefault(p => p.CurrentSemester?.FirstDay <= DateTime.Now && p.CurrentSemester?.LastDay >= DateTime.Now) ??
                              new GlobalRanklist();
            return BuildRanklistFromSubmitters(submittersGrouped, authorsCount, oldRanklist, false);
        }

        private GlobalRanklist BuildRanklistFromSubmitters(IEnumerable<IGrouping<string, SubmissionEntry>> submittersGrouped,
            Dictionary<string, int> authorsCount, GlobalRanklist oldRanklist, bool includeNotYetActive)
        {
            var list = submittersGrouped.Select(g => new GlobalSubmitter
            {
                Id = g.Key,
                Points = g.Sum(x => x.Points),
                SubmissionCount = g.Count(x => x.Rank > 0),
                SolvedCount = g.Count(x => x.Rank >= 0),
                ChallengesCreated = authorsCount.ContainsKey(g.Key) ? authorsCount[g.Key] : 0,
                Languages = string.Join(", ",
                    g.SelectMany(x => x.MoreLanguages ?? new string[0]).Concat(g.Select(x => x.Language)).Distinct()
                        .Where(x => Compilers.Any(c => c.Name == x))),
                Stars = Average(g),
                DuplicationScore = (int) g.Average(x => x.DuplicateScore),
                ReceivedReviews = g.Count(x => x.Rating > 0)
            }).ToList();

            var members = MemberProvider.GetMembers().ToList();

            if (includeNotYetActive)
            {
                var submittersLookup = list.ToLookup(x => x.Id);
                var notYetActiveMembers = members.Where(x => !submittersLookup.Contains(x.Id)).Select(x => new GlobalSubmitter {Id = x.Id});
                list.AddRange(notYetActiveMembers);
            }

            var membersDict = members.ToDictionary(x => x.Id);
            var submitters = list.Where(x => membersDict.ContainsKey(x.Id) && !membersDict[x.Id].Roles.Contains("admin")).ToList();

            var ranklist = new GlobalRanklist
            {
                Submitters = submitters, LastRankingChange = oldRanklist.LastRankingChange, CurrentSemester = GetCorrespondingSemester(DateTime.Today)
            };
            DetermineMemberRankings(ref ranklist, oldRanklist);
            return ranklist;
        }

        private void DetermineMemberRankings(ref GlobalRanklist ranklist, GlobalRanklist oldRanklist)
        {
            // Number of days without ranking changes before LastPeriodRank gets reset
            const int daysWithoutRankingChangeThreshold = 3;

            var sortedList = ranklist.Submitters.OrderByDescending(x => x.Points).ToList();
            var rank = 0;
            var currentRank = 0;
            var lastPoints = -1;
            foreach (var entry in sortedList)
            {
                rank++;
                if (entry.Points != lastPoints)
                {
                    currentRank = rank;
                    lastPoints = entry.Points;
                }

                entry.Rank = currentRank;
            }

            var isNewRankingPeriod = oldRanklist.LastRankingChange.Month != DateTime.Now.Month ||
                                     oldRanklist.LastRankingChange < DateTime.Now.AddDays(-daysWithoutRankingChangeThreshold);
            var lastRankingChange = isNewRankingPeriod ? DateTime.Now : oldRanklist.LastRankingChange;
            foreach (var entry in sortedList)
            {
                var oldEntry = oldRanklist.Submitters?.FirstOrDefault(x => x.Id == entry.Id);
                if (oldEntry != null)
                {
                    entry.LastPeriodRank = isNewRankingPeriod ? oldEntry.Rank : oldEntry.LastPeriodRank;
                    if (!isNewRankingPeriod && entry.Rank != oldEntry.Rank)
                    {
                        lastRankingChange = DateTime.Now;
                    }
                }
                else
                {
                    entry.LastPeriodRank = sortedList.Count + 1;
                }

                entry.RankChange = -(entry.Rank - entry.LastPeriodRank);
            }

            ranklist.Submitters = sortedList.OrderBy(x => MemberProvider.GetMemberById(x.Id)?.Name.Split(' ').Last()).ToList();
            ranklist.LastRankingChange = lastRankingChange;
        }

        private static int Average(IGrouping<string, SubmissionEntry> g)
        {
            var count = g.Count(x => x.Rating > 0);
            if (count == 0)
            {
                return 0;
            }

            var sum = (double) g.Sum(x => x.Rating);
            return (int) Math.Round(sum / count, 0);
        }

        private static bool DateInCurrentSemester(DateTime inputDate)
        {
            if (DateTime.Today.Month >= 1 && DateTime.Today.Month < 3)
            {
                return inputDate >= new DateTime(DateTime.Today.Year - 1, 8, 1);
            }

            if (DateTime.Today.Month >= 3 && DateTime.Today.Month < 9)
            {
                return inputDate >= new DateTime(DateTime.Today.Year, 2, 1);
            }

            if (DateTime.Today.Month >= 9 && DateTime.Today.Month <= 12)
            {
                return inputDate >= new DateTime(DateTime.Today.Year, 8, 1);
            }

            return false;
        }

        private static Semester GetCorrespondingSemester(DateTime date)
        {
            static DateTime ParseDate(string s)
            {
                return DateTime.ParseExact(s, "dd.MM.yyyy", CultureInfo.InvariantCulture);
            }

            SemesterPeriod period;
            DateTime firstDay;
            DateTime lastDay;
            string year;
            if (date.Month >= 1 && date.Month < 3)
            {
                period = SemesterPeriod.WS;
                year = $"{date.AddYears(-1):yy}/{date: yy}";
                firstDay = ParseDate($"01.08.{date.AddYears(-1):yyyy}");
                var lastDayString = DateTime.IsLeapYear(date.Year) ? $"29.02.{date:yyyy}" : $"28.02.{date:yyyy}";
                lastDay = ParseDate(lastDayString);
            }
            else if (date.Month >= 3 && date.Month < 9)
            {
                period = SemesterPeriod.SS;
                year = $"{date:yy}";
                firstDay = ParseDate($"01.02.{date:yyyy}");
                lastDay = ParseDate($"31.08.{date:yyyy}");
            }
            else if (date.Month >= 9 && date.Month <= 12)
            {
                period = SemesterPeriod.WS;
                year = $"{date:yy}/{date.AddYears(1):yy}";
                firstDay = ParseDate($"01.08.{date:yyyy}");
                string lastDayString;
                if (DateTime.IsLeapYear(date.AddYears(1).Year))
                {
                    lastDayString = $"29.02.{date.AddYears(1):yyyy}";
                }
                else
                {
                    lastDayString = $"28.02.{date.AddYears(1):yyyy}";
                }

                lastDay = ParseDate(lastDayString);
            }
            else
            {
                throw new NotImplementedException();
            }

            return new Semester {Period = period, Years = year, FirstDay = firstDay, LastDay = lastDay};
        }

        public ChallengeRanklist GenerateChallengeRanklist(IChallenge challengeProperties)
        {
            var rater = Raters.First(x => x.Name == challengeProperties.RatingMethod);
            var ratingPoints = GetRatingPointsForChallenge(challengeProperties);
            var results = ProviderStore.FileProvider.LoadTestedResults(challengeProperties);
            var authorId = MemberProvider.GetMemberById(challengeProperties.AuthorID)?.Id;
            var excludeAuthor = results.Where(x => x.MemberId != authorId).ToList();
            var onlyAuthor = results.Where(x => x.MemberId == authorId).ToList();
            var ranklist = BuildRanklistFromSubmissions(challengeProperties, excludeAuthor, rater, ratingPoints);
            var authorRanklist = BuildRanklistFromSubmissions(challengeProperties, onlyAuthor, rater, ratingPoints);
            AddAuthorsToRanklist(authorRanklist, ranklist);
            return ranklist;
        }

        public static RatingPoints GetRatingPointsForChallenge(IChallenge challengeProperties)
        {
            if (challengeProperties.State.DifficultyRating == null) // Yet unrated
            {
                return new RatingPoints(1, 1, 1);
            }

            if (challengeProperties.State.DifficultyRating < 40) // Beginners
            {
                return new RatingPoints(1, 1, 1);
            }

            if (challengeProperties.State.DifficultyRating < 70) // Advanced
            {
                return new RatingPoints(1, 2, 3);
            }

            return new RatingPoints(3, 4, 5);
        }

        private static void AddAuthorsToRanklist(ChallengeRanklist authorRanklist, ChallengeRanklist ranklist)
        {
            foreach (var author in authorRanklist.Submitters)
            {
                author.Points = author.Rank = 0;
                ranklist.Submitters.Add(author);
            }
        }

        public ChallengeRanklist BuildRanklistFromSubmissions(IChallenge challenge, IList<Result> results, ISubmissionRater rater, RatingPoints ratingPoints)
        {
            var ranklist = new ChallengeRanklist {Challenge = challenge.Id};
            var submitters = results.Where(x => x.IsPassed).Select(x => new SubmissionEntry
            {
                Id = x.MemberId,
                Date = x.SubmissionDate,
                Exectime = x.ExecutionDuration,
                Language = x.Language,
                Rating = challenge.IsReviewable ? x.ReviewRating : 0,
                CustomScore = x.CustomScore ?? 0,
                SubmissionCount = results.Count(y =>
                    y.MemberId == x.MemberId && y.EvaluationResult != EvaluationResult.CompilationError &&
                    y.EvaluationResult != EvaluationResult.NotAllowedLanguage && y.EvaluationResult != EvaluationResult.UnknownError),
                DuplicateScore = x.DuplicateScore ?? 0
            }).OrderByDescending(x => x, rater).ThenByDescending(x => x.Rating).ToList();
            var solvedCounter = new List<string>();

            foreach (var submitter in submitters.GroupBy(x => x.Id))
            {
                var bestEntry = submitter.First();
                var otherLanguages = results.Where(x => x.IsPassed && x.MemberId == bestEntry.Id && x.Language != bestEntry.Language).Select(x => x.Language)
                    .Distinct().ToList();
                solvedCounter.Add(bestEntry.Language);
                solvedCounter.AddRange(otherLanguages);
                if (otherLanguages.Count > 0)
                {
                    bestEntry.MoreLanguages = otherLanguages.OrderBy(x => x);
                }

                bestEntry.DuplicateScore = submitter.Min(x => x.DuplicateScore);
                ranklist.Submitters.Add(bestEntry);
            }

            rater.UpdatePoints(ranklist.Submitters, ratingPoints);
            rater.UpdateRanking(ranklist.Submitters);
            ranklist.Submitters.Sort((x, y) => x.Rank.CompareTo(y.Rank));
            ranklist.SolvedCount = solvedCounter.GroupBy(x => x).ToDictionary(x => x.Key, x => x.Count());
            return ranklist;
        }

        public void UpdateChallengeActivities(IReadOnlyList<IChallenge> challenges, IEnumerable<Bundle> bundles)
        {
            var recentDate = DateTime.Now.AddDays(-14);
            foreach (var challenge in challenges)
            {
                using var writeLock = ProviderStore.FileProvider.GetLock();
                var updated = ProviderStore.FileProvider.LoadChallenge(challenge.Id, writeLock);
                if (updated.IsAvailable)
                {
                    var counter = 0;
                    var date = updated.Date;
                    date = new DateTime(date.Year, date.Month, date.Day, 23, 59, 59);
                    foreach (var result in ProviderStore.FileProvider.LoadAllSubmissionsFor(updated))
                    {
                        if (result.SubmissionDate > date)
                        {
                            date = result.SubmissionDate;
                        }

                        if (result.SubmissionDate > recentDate)
                        {
                            counter++;
                        }
                    }

                    updated.State.Activity = (int) (date.AddHours(counter * 4) - DateTime.Today).TotalHours;
                }
                else
                {
                    updated.State.Activity = 0;
                }

                ProviderStore.FileProvider.SaveChallenge(updated, writeLock);
            }

            var lookup = challenges.ToDictionary(x => x.Id, x => x);
            foreach (var bundle in bundles)
            {
                using var writeLock = ProviderStore.FileProvider.GetLock();
                var updated = ProviderStore.FileProvider.LoadBundle(bundle.Id, writeLock);
                updated.State.Activity = -7000;
                foreach (var challengeId in updated.Challenges)
                {
                    if (lookup.TryGetValue(challengeId, out var challenge) && challenge.State.Activity > updated.State.Activity)
                    {
                        updated.State.Activity = challenge.State.Activity;
                    }
                }

                ProviderStore.FileProvider.SaveBundle(updated, writeLock);
            }
        }

        public void UpdateBundleDifficultyRatings(List<Bundle> bundles)
        {
            foreach (var bundle in bundles)
            {
                using var writeLock = ProviderStore.FileProvider.GetLock();
                var updated = ProviderStore.FileProvider.LoadBundle(bundle.Id, writeLock);
                var challenges = updated.Challenges.Select(x => ProviderStore.FileProvider.LoadChallenge(x)).Where(x => x.State.DifficultyRating.HasValue)
                    .ToList();
                if (challenges.Count == 0)
                {
                    updated.State.DifficultyRating = null;
                }
                else
                {
                    var min = challenges.Min(x => x.State.DifficultyRating.Value);
                    var max = challenges.Max(x => x.State.DifficultyRating.Value);
                    updated.State.DifficultyRating = (min + max) / 2;
                }

                ProviderStore.FileProvider.SaveBundle(updated, writeLock);
            }
        }

        public void GenerateChallengeEstimation()
        {
            var challenges = ProviderStore.FileProvider.GetChallengeIds();
            var bundles = ProviderStore.FileProvider.LoadAllBundles().ToList();
            var lastEstimation = new Dictionary<string, double?>();
            foreach (var challengeId in challenges)
            {
                using var writeLock = ProviderStore.FileProvider.GetLock();
                var updated = ProviderStore.FileProvider.LoadChallenge(challengeId, writeLock);
                var results = ProviderStore.FileProvider.LoadAllSubmissionsFor(updated).ToList();
                double? lastEst = null;
                var bundle = bundles.Find(x => x.Challenges.Contains(updated.Id));
                if (bundle != null)
                {
                    lastEstimation.TryGetValue(bundle.Id, out lastEst);
                }

                var estimation = ChallengeEstimator.GuessEffortFor(updated, results, lastEst);
                var features = ChallengeEstimator.FindFeaturesFor(results);
                updated.State.MinEffort = estimation.Min;
                updated.State.MaxEffort = estimation.Max;

                if (features?.Count > 0)
                {
                    updated.State.Features = features;
                }

                if (bundle != null)
                {
                    lastEstimation[bundle.Id] = estimation.RawRating;
                }

                ProviderStore.FileProvider.SaveChallenge(updated, writeLock);
            }
        }

        public void LogChallengeActivity(IChallenge challenge)
        {
            try
            {
                var lastEditor = MemberProvider.GetMemberById(challenge.LastEditorID);
                var lastEditorId = lastEditor?.Id ?? "";
                if (challenge.IsAvailable)
                {
                    Log.Activity(lastEditorId, ActivityType.ChangedChallenge, challenge.Id);
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Aktivitätslogging fehlgeschlagen");
            }
        }

        public void DeleteMemberFromGlobalRanking(IMember member)
        {
            using var writeLock = ProviderStore.FileProvider.GetLock();
            var oldRanklist = ProviderStore.FileProvider.LoadGlobalRanklist(writeLock);
            var submitter = oldRanklist.Submitters.Find(x => x.Id == member.Id);
            if (submitter != null)
            {
                oldRanklist.Submitters.Remove(submitter);
                ProviderStore.FileProvider.SaveGlobalRankingList(oldRanklist, writeLock);
            }
        }

        public void DeleteMemberFromSemesterRanking(IMember member)
        {
            using var writeLock = ProviderStore.FileProvider.GetLock();
            foreach (var oldRanklist in ProviderStore.FileProvider.LoadAllSemesterRanklists())
            {
                var submitter = oldRanklist.Submitters.Find(x => x.Id == member.Id);
                if (submitter != null)
                {
                    oldRanklist.Submitters.Remove(submitter);
                    ProviderStore.FileProvider.SaveSemesterRankingList(oldRanklist);
                }
            }
        }

        private ChallengeRanklist GenerateChallengeCreatorRanklist(IMemberProvider memberProvider)
        {
            var ranklist = new ChallengeRanklist {Challenge = "ChallengeCreators"};
            var members = memberProvider.GetMembers().ToDictionary(x => x.Id);
            foreach (var challenge in ProviderStore.FileProvider.LoadChallenges())
            {
                var succeedSubmitters = members.Values.Count(x => x.SolvedChallenges.Contains(challenge.Id));
                try
                {
                    var author = members[challenge.AuthorID];
                    if (author != null && challenge.IsAvailable)
                    {
                        var points = succeedSubmitters > 0 ? 1 : 0;
                        points = succeedSubmitters >= 5 ? 2 : points;
                        ranklist.Submitters.Add(new SubmissionEntry
                        {
                            Id = author.Id,
                            Date = challenge.Date,
                            Points = points,
                            Language = challenge.Id,
                            Rank = -1
                        });
                    }
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "Punktevergabe für {challenge} fehlgeschlagen", challenge);
                }
            }

            return ranklist;
        }

        private ChallengeRanklist GenerateReviewRanklist(IMemberProvider memberProvider)
        {
            var ranklist = new ChallengeRanklist {Challenge = "Reviews"};

            foreach (var member in memberProvider.GetMembers())
            {
                var points = member.ReviewCounter * 10 / 25;
                if (points > 0)
                {
                    ranklist.Submitters.Add(new SubmissionEntry
                    {
                        Id = member.Id,
                        Date = DateTime.Now,
                        Points = points,
                        Language = member.ReviewCounter + " Reviews",
                        Rank = -1
                    });
                }
            }

            return ranklist;
        }

        private ChallengeRanklist GenerateSemesterReviewRanklist(IMemberProvider memberProvider)
        {
            var ranklist = new ChallengeRanklist {Challenge = "Reviews"};

            foreach (var member in memberProvider.GetMembers())
            {
                var allSubmissionsAsReviewer = GetAllSubmissionsForUserAsReviewer(member);
                var points = allSubmissionsAsReviewer.Count(p => DateInCurrentSemester(p.ReviewDate.Value)) * 10 / 25;
                if (points > 0)
                {
                    ranklist.Submitters.Add(new SubmissionEntry
                    {
                        Id = member.Id,
                        Date = DateTime.Now,
                        Points = points,
                        Language = member.ReviewCounter + " Reviews",
                        Rank = -1
                    });
                }
            }

            return ranklist;
        }

        private IReadOnlyList<ISubmission> GetAllSubmissionsForUserAsReviewer(IMember member)
        {
            return ProviderStore.FileProvider.LoadAllSubmissions().Where(p => p.ReviewState == ReviewStateType.Reviewed && p.Reviewer == member.Id).ToList();
        }

        public void LoadCompilerInfo(List<ICompiler> compilers)
        {
            ISyncLock versionlock = null;
            try
            {
                versionlock = ProviderStore.SandboxedProcessProvider.GetLock();
                Parallel.ForEach(compilers, compiler =>
                {
                    try
                    {
                        compiler.VersionDetails = compiler.ReadVersionDetails(ProviderStore.SandboxedProcessProvider, versionlock);
                        compiler.Version = CompilerOperations.GetCompilerVersion(compiler);
                        compiler.Available = true;
                    }
                    catch (Exception)
                    {
                        compiler.Available = false;
                        Log.Warning("Compiler " + compiler.Name + " nicht vefügbar.");
                    }
                });
            }
            finally
            {
                ProviderStore.SandboxedProcessProvider.ReleaseLock(versionlock);
            }
        }

        public List<SubmitterHistory> BuildSubmitterHistories()
        {
            var result = new List<SubmitterHistory>();
            var members = MemberProvider.GetMembers();
            var submissions = ProviderStore.FileProvider.LoadAllSubmissions().ToList();
            foreach (var member in members)
            {
                result.Add(BuildSubmitterHistory(member, submissions));
            }

            return result;
        }

        public static void UnlockChallengesForMember(IMember member, IFileProvider fileProvider, IMemberProvider memberProvider)
        {
            // keep calculation of the average difficulty
            // however, it seems really strange that it is done in the "unlock" method
            var avgDifficultyLevel = CalculateAverageDifficultyLevel(member, fileProvider);
            memberProvider.UpdateAverageDifficultyLevel(member, avgDifficultyLevel);

            // assign all unlocked challenges which the user is allowed to view and he*she hasn't already solved
            var challenges = ChallengeOperations.GetAllChallengesForMember(member, fileProvider, false).Select(x => x.Id)
                .Where(x => !member.SolvedChallenges.Contains(x));
            memberProvider.UpdateUnlockedChallenges(member, challenges.ToArray());
        }

        private static int CalculateAverageDifficultyLevel(IMember member, IFileProvider fileProvider)
        {
            var ratingSum = 0.0;
            var ratingDivisor = 0;
            foreach (var solved in member.SolvedChallenges)
            {
                var challenge = fileProvider.LoadChallenge(solved);
                if (challenge.State.DifficultyRating.HasValue)
                {
                    var submissions = fileProvider.LoadAllSubmissionsFor(challenge).Where(x => x.MemberId == member.Id);
                    var solvedCount = 0;
                    var failedCount = 0;
                    foreach (var submission in submissions)
                    {
                        if (submission.IsPassed)
                        {
                            solvedCount++;
                        }

                        if (submission.IsTestsFailed)
                        {
                            failedCount++;
                        }
                    }

                    var solvedGrade = solvedCount > 0 ? failedCount / (double) solvedCount : 0;
                    var challengeGrade = challenge.State.PassedCount > 0 ? challenge.State.FailedCount / (double) challenge.State.PassedCount : 0;

                    if (solvedGrade >= challengeGrade)
                    {
                        ratingSum += (int) challenge.State.DifficultyRating;
                    }
                    else
                    {
                        ratingSum += (int) (challenge.State.DifficultyRating * 0.8);
                    }

                    ratingDivisor++;
                }
            }

            return ratingDivisor > 0 ? (int) (ratingSum / ratingDivisor) : 0;
        }

        public void UpdateDifficultyRatingsForChallenges()
        {
            foreach (var props in ProviderStore.FileProvider.LoadChallenges().Where(x => x.IsAvailable))
            {
                using var writeLock = ProviderStore.FileProvider.GetLock();
                var updating = ProviderStore.FileProvider.LoadChallenge(props.Id, writeLock);
                if (updating.FreezeDifficultyRating) { continue; }

                if (!updating.FreezeDifficultyRating && updating.State.FeasibilityIndex == 0)
                {
                    updating.State.DifficultyRating = null;
                }

                ProviderStore.FileProvider.SaveChallenge(updating, writeLock);
            }

            using (var writeLock = ProviderStore.FileProvider.GetLock())
            {
                var challenges = ProviderStore.FileProvider.GetChallengeIds().Select(x => ProviderStore.FileProvider.LoadChallenge(x, writeLock))
                    .Where(x => x.IsAvailable).ToList();
                var sortedByFeasibility = challenges.Where(x => x.State.FeasibilityIndex > 0).OrderByDescending(x => x.State.FeasibilityIndex).ToList();
                var groups = Math.Max(Math.Min(20, sortedByFeasibility.Count / 2), 1);
                var groupCount = (int) Math.Round(sortedByFeasibility.Count / (double) groups);
                if (groupCount == 0)
                {
                    groupCount = 1;
                }

                var ctr = 0;
                foreach (var challenge in sortedByFeasibility)
                {
                    if (!challenge.FreezeDifficultyRating)
                    {
                        challenge.State.DifficultyRating = Math.Min(100, (ctr / groupCount + 1) * 100 / groups);
                        if (challenge.RatingMethod != RatingMethod.Fixed)
                        {
                            challenge.State.DifficultyRating = Math.Min((int) (1.25 * challenge.State.DifficultyRating), 100);
                        }
                    }

                    ProviderStore.FileProvider.SaveChallenge(challenge, writeLock);
                    ctr++;
                }
            }
        }

        public static void FixNotSolvedChallengesForMember(IMember member, IFileProvider fileProvider, IMemberProvider memberProvider, ILog log)
        {
            var solved = fileProvider.LoadAllSubmissionsFor(member).Where(x => x.IsPassed).Select(x => x.Challenge).Distinct().ToList();
            var allSolved = solved.Concat(member.SolvedChallenges).Distinct().ToArray();
            if (member.SolvedChallenges.Length < allSolved.Length)
            {
                memberProvider.UpdateSolvedChallenges(member, allSolved);
                log.Error($"Gelöste Aufgaben wurden für {member.Name} behoben.");
            }
        }
    }
}
