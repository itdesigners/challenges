using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using SubmissionEvaluation.Contracts.Data;
using SubmissionEvaluation.Contracts.Data.Ranklist;
using SubmissionEvaluation.Contracts.Data.Review;
using SubmissionEvaluation.Contracts.Providers;
using SubmissionEvaluation.Domain.Operations;
using SubmissionEvaluation.Shared.Classes.Config;

namespace SubmissionEvaluation.Domain
{
    public class Interactions
    {
        private readonly Domain domain;
        private readonly bool enableAchivements;

        public Interactions(Domain domain, bool enableAchievements)
        {
            this.domain = domain;
            enableAchivements = enableAchievements;
        }

        public event Action<ISubmission> NewSubmission;
        public event Action<ISubmission> NewReview;

        public void AddChallengeSubmission(IMember member, string challengeName, byte[] fileData)
        {
            if (domain.IsMaintenanceMode)
            {
                domain.Log.Warning("Interaktion: System ist im Wartungsmodus");
                return;
            }

            domain.Log.Information("Interaktion: Füge Einreichung hinzu {member} {challenge}", member.Name, challengeName);
            var filenames = domain.ProviderStore.FileProvider.GetFilenamesInsideZip(fileData);
            var compilerBase = domain.CompilerOperations.GetCompilerForFiles(filenames);
            var submission = SubmissionOperations.SaveNewSubmission(domain.ProviderStore, member, DateTime.Now, fileData, challengeName, compilerBase);
            NewSubmission?.Invoke(submission);
        }

        public void AddAdditionalFileToChallenge(IChallenge challenge, string fileName, byte[] data)
        {
            if (domain.IsMaintenanceMode)
            {
                domain.Log.Warning("Interaktion: System ist im Wartungsmodus");
                return;
            }

            domain.Log.Information("Interaktion: Füge Datei zu Challenge hinzu {challenge} {file}", challenge.Id, fileName);
            domain.ProviderStore.FileProvider.SaveAdditionalFile(challenge, fileName, data);
        }

        public void CopyFileToOtherFile(string oldfileName, string newFileName, IChallenge challenge)
        {
            var content = domain.ProviderStore.FileProvider.LoadChallengeFileContent(challenge.Id, oldfileName);
            AddAdditionalFileToChallenge(challenge, newFileName, content);
        }

        public void EditAdditionalTextFile(IChallenge challenge, string fileName, string content)
        {
            if (domain.IsMaintenanceMode)
            {
                domain.Log.Warning("Interaktion: System ist im Wartungsmodus");
                return;
            }

            domain.Log.Information("Interaktion: Verändere die Datei {file} in Challenge {challenge}", challenge.Id, fileName);
            domain.ProviderStore.FileProvider.WriteChallengeAdditionalFileContent(challenge.Id, fileName, content);
        }

        public void UpdateTests(IChallenge challenge, IEnumerable<ITest> tests)
        {
            if (domain.IsMaintenanceMode)
            {
                domain.Log.Warning("Interaktion: System ist im Wartungsmodus");
                return;
            }

            domain.Log.Information("Interaktion: Aktuallisiere Tests für {challenge}", challenge.Id);
            ChallengeOperations.UpdateTestsForChallenge(domain, challenge, tests);
        }

        public void ChangeUidForMember(string id, string uid)
        {
            if (domain.IsMaintenanceMode)
            {
                domain.Log.Warning("Interaktion: System ist im Wartungsmodus");
                return;
            }

            domain.Log.Information("Interaktion: Ändere UID für User {id}", id);
            var member = domain.MemberProvider.GetMemberById(id);
            if (uid != member.Uid)
            {
                domain.MemberProvider.UpdateUid(member, uid);
            }
        }

        public void UpdateMember(string id, string name, string mail)
        {
            if (domain.IsMaintenanceMode)
            {
                domain.Log.Warning("Interaktion: System ist im Wartungsmodus");
                return;
            }

            domain.Log.Information("Interaktion: Aktualisiere User-Daten für {id}", id);
            var member = domain.MemberProvider.GetMemberById(id);
            if (name != member.Name)
            {
                domain.MemberProvider.UpdateName(member, name);
            }

            if (mail != member.Mail)
            {
                domain.MemberProvider.UpdateMail(member, mail);
            }
        }

        public void CreateNewChallenge(IChallenge challenge)
        {
            if (domain.IsMaintenanceMode)
            {
                domain.Log.Warning("Interaktion: System ist im Wartungsmodus");
                return;
            }

            domain.Log.Information("Interaktion: Erzeuge neue Challenge {id}", challenge.Id);
            ChallengeOperations.ValidateChallengeName(challenge);
            domain.ProviderStore.FileProvider.CreateChallenge(challenge);
            ChallengeOperations.VerifyChallenge(domain, challenge.Id);
        }

        public bool CreateGlobalRankList(GlobalRanklist list)
        {
            if (domain.ProviderStore.FileProvider.LoadGlobalRanklist().CurrentSemester == null)
            {
                using var writeLock = domain.ProviderStore.FileProvider.GetLock();
                domain.ProviderStore.FileProvider.SaveGlobalRankingList(list, writeLock);

                return true;
            }

            domain.Log.Warning("Illegal try to overwrite global ranking.");
            return false;
        }

        public void CopyChallenge(IChallenge challenge, string toChallengeName, string userId)
        {
            if (domain.IsMaintenanceMode)
            {
                domain.Log.Warning("Interaktion: System ist im Wartungsmodus");
                return;
            }

            domain.Log.Information("Interaktion: Kopiere Challenge {id} zu {toChallengeName}", challenge.Id, toChallengeName);
            var challengeToCopy = domain.ProviderStore.FileProvider.LoadChallenge(challenge.Id);
            challengeToCopy.Id = toChallengeName;
            challengeToCopy.IsDraft = true;
            challengeToCopy.AuthorID = userId;
            challengeToCopy.Date = DateTime.Now;
            challengeToCopy.LastEditorID = null;
            challengeToCopy.LearningFocus = challenge.LearningFocus;
            domain.ProviderStore.FileProvider.CreateChallenge(challengeToCopy);

            using (var writeLock = domain.ProviderStore.FileProvider.GetLock())
            {
                var update = domain.ProviderStore.FileProvider.LoadChallenge(toChallengeName, writeLock);
                var testParameters = domain.ProviderStore.FileProvider.LoadTestProperties(challenge);
                domain.ProviderStore.FileProvider.SaveTestProperties(update, testParameters, writeLock);
                var additionalFiles = domain.ProviderStore.FileProvider.GetAdditionalFiles(challenge);
                foreach (var fileName in additionalFiles)
                {
                    var (_, data, _, _) = domain.ProviderStore.FileProvider.GetChallengeAdditionalFile(challenge.Id, fileName);
                    domain.ProviderStore.FileProvider.SaveAdditionalFile(update, fileName, data);
                }
            }

            ChallengeOperations.VerifyChallenge(domain, toChallengeName);
        }

        public void CopyGroup(IGroup group, string toGroupName)
        {
            if (domain.IsMaintenanceMode)
            {
                domain.Log.Warning("Interaktion: System ist im Wartungsmodus");
                return;
            }

            domain.Log.Information($"Interaktion: Kopiere Gruppe {group.Id} zu {toGroupName}");
            var groupToCopy = domain.ProviderStore.FileProvider.LoadGroup(group.Id);
            groupToCopy.Id = toGroupName;
            domain.ProviderStore.FileProvider.CreateGroup(groupToCopy.Id, groupToCopy.Title, groupToCopy.GroupAdminIds, groupToCopy.IsSuperGroup, groupToCopy.SubGroups, groupToCopy.ForcedChallenges, groupToCopy.AvailableChallenges, groupToCopy.MaxUnlockedChallenges, groupToCopy.RequiredPoints, groupToCopy.StartDate, groupToCopy.EndDate);

            using (var writeLock = domain.ProviderStore.FileProvider.GetLock())
            {
                var update = domain.ProviderStore.FileProvider.LoadGroup(toGroupName, writeLock);
            }

            GroupOperations.VerifyGroup(domain, toGroupName);
        }

        public void EditChallenge(IChallenge changes)
        {
            if (domain.IsMaintenanceMode)
            {
                domain.Log.Warning("Interaktion: System ist im Wartungsmodus");
                return;
            }

            domain.Log.Information("Interaktion: Ändere Challenge {id}", changes.Id);

            using (var writeLock = domain.ProviderStore.FileProvider.GetLock())
            {
                UpdateChallenge(changes, writeLock);
            }

            ChallengeOperations.VerifyChallenge(domain, changes.Id);
        }

        private void UpdateChallenge(IChallenge changes, IWriteLock writeLock)
        {
            var props = domain.ProviderStore.FileProvider.LoadChallenge(changes.Id, writeLock);
            props.Title = changes.Title;
            props.AuthorID = changes.AuthorID;
            props.Category = changes.Category;
            props.RatingMethod = changes.RatingMethod;
            props.IsDraft = changes.IsDraft;
            props.Description = changes.Description;
            props.Source = changes.Source;
            props.Languages = changes.Languages;
            props.IncludeTests = changes.IncludeTests;
            props.DependsOn = changes.DependsOn;
            props.LastEdit = DateTime.Now;
            props.LearningFocus = changes.LearningFocus;

            domain.ProviderStore.FileProvider.SaveChallenge(props, writeLock);
            domain.StatisticsOperations.LogChallengeActivity(props);
        }

        public bool ChangeChallengeId(IChallenge challenge, string newId)
        {
            if (domain.IsMaintenanceMode)
            {
                domain.Log.Warning("Interaktion: System ist im Wartungsmodus");
                return false;
            }

            if (IsChallengeIdTaken(newId))
            {
                return false;
            }

            domain.Log.Information("Interaktion: Ändere Challenge Id {id} to {newId}", challenge.Id, newId);
            var fileProvider = domain.ProviderStore.FileProvider;
            ChallengeOperations.RenameChallengeInRecentActivities(challenge, newId, fileProvider);
            ChallengeOperations.RenameChallengeInBundles(challenge, newId, fileProvider);
            ChallengeOperations.RenameChallengeInGroups(challenge, newId, fileProvider);
            ChallengeOperations.RenameChallengeInMembers(challenge, newId, fileProvider);
            fileProvider.ChangeChallengeId(challenge, newId);
            fileProvider.MoveChallengeSubmissionTo(challenge, newId);
            return true;
        }

        public bool ChangeGroupId(IGroup group, string newId)
        {
            if (domain.IsMaintenanceMode)
            {
                domain.Log.Warning("Interaktion: System ist im Wartungsmodus");
                return false;
            }

            if (IsGroupIdTaken(newId))
            {
                return false;
            }

            domain.Log.Information("Interaktion: Ändere Group Id {id} to {newId}", group.Id, newId);
            var fileProvider = domain.ProviderStore.FileProvider;
            GroupOperations.RenameGroupInAllMembers(fileProvider, group, newId);
            fileProvider.ChangeGroupId(group, newId);
            return true;
        }

        private bool IsChallengeIdTaken(string id)
        {
            return domain.ProviderStore.FileProvider.GetChallengeIds().Contains(id);
        }

        private bool IsGroupIdTaken(string id)
        {
            return domain.ProviderStore.FileProvider.GetGroupIds().Contains(id);
        }

        public void AddReview(ReviewData reviewData)
        {
            if (domain.IsMaintenanceMode)
            {
                domain.Log.Warning("Interaktion: System ist im Wartungsmodus");
                return;
            }

            var submission = domain.ReviewOperations.StoreReviewSubmission(reviewData);
            NewReview?.Invoke(submission);
        }

        public void RegisterMemberViaMail(string mail, string passwordHash)
        {
            if (domain.IsMaintenanceMode)
            {
                domain.Log.Warning("Interaktion: System ist im Wartungsmodus");
                return;
            }

            domain.Log.Information("Interaktion: Registriere neuen user {mail}", mail);
            var member = domain.MemberProvider.GetMemberByMail(mail);
            if (member != null)
            {
                domain.MemberProvider.UpdatePassword(member, passwordHash);
            }
            else
            {
                var name = mail.Split('@').First().Replace('.', ' ');
                var uid = mail;
                member = domain.Interactions.AddMember(name, mail);
                domain.MemberProvider.UpdateUid(member, uid);
                domain.MemberProvider.UpdatePassword(member, passwordHash);
            }
        }

        public IMember RegisterMemberViaUsername(string username, string passwordHash, string fullname)
        {
            if (domain.IsMaintenanceMode)
            {
                domain.Log.Warning("Interaktion: System ist im Wartungsmodus");
                return null;
            }

            domain.Log.Information("Interaktion: Registriere neuen user {username}", username);
            var member = domain.MemberProvider.GetMemberByName(username);
            if (member != null)
            {
                throw new Exception("User already exists");
            }

            member = domain.Interactions.AddMember(username, "");
            domain.MemberProvider.UpdateUid(member, username);
            domain.MemberProvider.UpdatePassword(member, passwordHash);
            domain.MemberProvider.UpdateName(member, fullname);
            return member;
        }

        public void ChangeAdditionalFilenameOfChallenge(string challenge, string originalName, string newname)
        {
            if (domain.IsMaintenanceMode)
            {
                domain.Log.Warning("Interaktion: System ist im Wartungsmodus");
                return;
            }

            domain.Log.Information("Interaktion: Ändere zusätzliche Datei für Challenge {challenge}", challenge);
            ChallengeOperations.ChangeAdditionalFilenameOfChallenge(domain.ProviderStore, challenge, originalName, newname);
        }

        public void RemoveAdditionalFileFromChallenge(string challenge, string name)
        {
            if (domain.IsMaintenanceMode)
            {
                domain.Log.Warning("Interaktion: System ist im Wartungsmodus");
                return;
            }

            domain.Log.Information("Interaktion: Entferne zusätzliche Datei für Challenge {challenge}", challenge);
            ChallengeOperations.RemoveAdditionalFileFromChallenge(domain.ProviderStore, challenge, name);
        }

        public void CheckForUnprocessedSubmissions(Action<ISubmission> onChallengeHasNewSubmission)
        {
            if (domain.IsMaintenanceMode)
            {
                domain.Log.Warning("Interaktion: System ist im Wartungsmodus");
                return;
            }

            domain.Log.Information("Interaktion: Schaue nach unbewerteter Submissions");
            var challenges = domain.ProviderStore.FileProvider.LoadChallenges().ToList();
            challenges.ForEach(challenge => { domain.EvaluationOperations.CheckForUnprocessedSubmissionsOf(challenge, onChallengeHasNewSubmission); });
        }

        public void ProcessSubmission(string id, string challenge)
        {
            if (domain.IsMaintenanceMode)
            {
                domain.Log.Warning("Interaktion: System ist im Wartungsmodus");
                return;
            }

            domain.Log.Information("Interaktion: Starte Verarbeitung für Einreichung {id} {challenge}", id, challenge);
            var submission = domain.ProviderStore.FileProvider.LoadResult(challenge, id);
            if (submission.EvaluationState != EvaluationState.Evaluated)
            {
                var props = domain.ProviderStore.FileProvider.LoadChallenge(challenge);
                domain.EvaluationOperations.ProcessSubmission(submission, props, true, true,
                    forceRebuild: submission.EvaluationState == EvaluationState.RerunRequested);
            }
        }

        public void RerunSubmissions()
        {
            if (domain.IsMaintenanceMode)
            {
                domain.Log.Warning("Interaktion: System ist im Wartungsmodus");
                return;
            }

            domain.Log.Information("Interaktion: Starte Verarbeitung bereits bewerteter Submissions");
            var challenges = domain.ProviderStore.FileProvider.LoadChallenges();
            foreach (var challenge in challenges)
            {
                try
                {
                    domain.EvaluationOperations.RerunSubmissionsForChallenge(challenge);
                }
                catch (Exception ex)
                {
                    domain.Log.Error(ex, "Submission rerun failed for {challenge}", challenge);
                }
            }

            domain.Log.Information("Alle Submissions wurden verarbeitet");
        }

        public void UpdateCategoryPages()
        {
            if (domain.IsMaintenanceMode)
            {
                domain.Log.Warning("Interaktion: System ist im Wartungsmodus");
                return;
            }

            domain.Log.Information("Interaktion: Aktualisiere Aktivitätenbewertung");
            var challenges = domain.ProviderStore.FileProvider.LoadChallenges().ToList();
            var bundles = domain.ProviderStore.FileProvider.LoadAllBundles();
            domain.StatisticsOperations.UpdateChallengeActivities(challenges, bundles);
        }

        public void StartNewReviews()
        {
            if (domain.IsMaintenanceMode)
            {
                domain.Log.Warning("Interaktion: System ist im Wartungsmodus");
                return;
            }

            try
            {
                domain.Log.Information("Interaktion: Starte neue Reviews");
                if (!domain.ReviewOperations.IsReviewEnabled)
                {
                    return;
                }

                var reviewableSubmissions = domain.ReviewOperations.ReviewableSubmissions();

                var pendingReviews = reviewableSubmissions.Where(x => x.ReviewState == ReviewStateType.InProgress).ToList();
                var notYetReviewed = reviewableSubmissions.Where(x => x.ReviewState == ReviewStateType.NotReviewed).ToList();
                var pendingCount = pendingReviews.GroupBy(x => x.Reviewer).ToDictionary(x => x.Key, x => x.Count());
                var reviewDates = pendingReviews.GroupBy(x => x.Reviewer).ToDictionary(x => x.Key, x => x.Select(y => y.ReviewDate.Value.AddDays(-7)).Max());
                var availableReviewers = domain.MemberProvider.GetMembers()
                    .Where(x => x.State == MemberState.Active && ReviewOperations.CanReviewMoreReviewsAtCurrentTime(x, pendingCount, reviewDates))
                    .OrderBy(x => x.ReviewFrequency);
                foreach (var availableReviewer in availableReviewers)
                {
                    var reviewItem = domain.ReviewOperations.FindReviewForReviewer(notYetReviewed, availableReviewer, 0, 100);
                    if (reviewItem != null)
                    {
                        notYetReviewed.Remove(reviewItem);
                        domain.ReviewOperations.StartReview(reviewItem, availableReviewer, notYetReviewed.Count);
                    }
                }

                domain.ReviewOperations.LogUnreviewedSubmissions(notYetReviewed);
            }
            catch (Exception ex)
            {
                domain.Log.Error(ex, "Fehler beim Verwalten der Reviews");
            }
        }

        public void CancelReview(ISubmission submission)
        {
            if (domain.IsMaintenanceMode)
            {
                domain.Log.Warning("Interaktion: System ist im Wartungsmodus");
                return;
            }

            domain.Log.Information($"Interaktion: Cancel running review in {submission.Challenge}/{submission.SubmissionId}");
            domain.ReviewOperations.ResetReview(submission);
        }

        public void ProcessPendingReviews()
        {
            if (domain.IsMaintenanceMode)
            {
                domain.Log.Warning("Interaktion: System ist im Wartungsmodus");
                return;
            }

            domain.Log.Information("Interaktion: Verarbeite ausstehende Reviews");
            if (!domain.ReviewOperations.IsReviewEnabled)
            {
                return;
            }

            var reviewableSubmissions = domain.ReviewOperations.ReviewableSubmissions();
            var pendingReviews = reviewableSubmissions.Where(x => x.ReviewState == ReviewStateType.InProgress).ToList();
            var pendingCount = pendingReviews.GroupBy(x => x.Reviewer).ToDictionary(x => x.Key, x => x.Count());
            pendingReviews.ForEach(x =>
            {
                var reviewer = domain.MemberProvider.GetMemberById(x.Reviewer);
                domain.ReviewOperations.CheckPendingReview(x, () =>
                {
                    pendingCount[reviewer.Id]--;
                    if (pendingCount[reviewer.Id] == 0)
                    {
                        domain.MemberProvider.DecreaseReviewFrequency(reviewer);
                    }
                }, () => ProcessReview(x.SubmissionId, x.Challenge));
            });
        }

        public void UpdateAchievements()
        {
            if (domain.IsMaintenanceMode)
            {
                domain.Log.Warning("Interaktion: System ist im Wartungsmodus");
                return;
            }

            domain.Log.Information("Interaktion: Aktualisiere Achievements");
            using var writeLock = domain.ProviderStore.FileProvider.GetLock();
            var awards = domain.ProviderStore.FileProvider.LoadAwards(writeLock);
            if (enableAchivements)
            {
                var updatedAwards = domain.AchievementOperations.AddAchievementsForSubmitters(domain.ProviderStore.FileProvider, domain.MemberProvider,
                    domain.Query.GetCompilerNames(), awards);
                domain.ProviderStore.FileProvider.SaveAwards(updatedAwards, writeLock);
            }
            else
            {
                domain.ProviderStore.FileProvider.SaveAwards(new Awards(), writeLock);
            }
        }

        public void UpdateChallengeDifficultyRatings()
        {
            if (domain.IsMaintenanceMode)
            {
                domain.Log.Warning("Interaktion: System ist im Wartungsmodus");
                return;
            }

            domain.Log.Information("Interaktion: Aktualisiere Challenge Schwierigkeitsbewertung");
            domain.StatisticsOperations.UpdateDifficultyRatingsForChallenges();
            var bundles = domain.ProviderStore.FileProvider.LoadAllBundles().ToList();
            domain.StatisticsOperations.UpdateBundleDifficultyRatings(bundles);
        }

        public void DeleteMember(string memberId)
        {
            if (domain.IsMaintenanceMode)
            {
                domain.Log.Warning("Interaktion: System ist im Wartungsmodus");
                return;
            }

            domain.Log.Information($"Interaktion: Lösche Nutzer mit der Benutzerid {memberId}");
            var member = domain.MemberProvider.GetMemberById(memberId);
            domain.Maintenance.EnsureMemberIsNotChallengeAuthor(member);
            var dataStore = domain.ProviderStore;
            SubmissionOperations.DeleteSubmissionForMember(dataStore, member);
            ReviewOperations.DeleteAsReviewer(dataStore, member);
            domain.AchievementOperations.DeleteAchievementForMember(domain.ProviderStore, member);
            domain.MemberProvider.DeleteMember(member);
            domain.StatisticsOperations.DeleteMemberFromGlobalRanking(member);
            domain.StatisticsOperations.DeleteMemberFromSemesterRanking(member);
            // Rebuild statistics
            UpdateAchievements();
            domain.StatisticsOperations.UpdateGlobalRanking();
        }

        public void UpdateChallengeEstimation()
        {
            if (domain.IsMaintenanceMode)
            {
                domain.Log.Warning("Interaktion: System ist im Wartungsmodus");
                return;
            }

            domain.Log.Information("Interaktion: Aktualisiere Challenge Abschätzung");
            domain.StatisticsOperations.GenerateChallengeEstimation();
        }

        public void RerunFailedSubmissions()
        {
            if (domain.IsMaintenanceMode)
            {
                domain.Log.Warning("Interaktion: System ist im Wartungsmodus");
                return;
            }

            var withResult = domain.ProviderStore.FileProvider.LoadAllSubmissions().Where(x =>
                x.EvaluationResult == EvaluationResult.TestsFailed || x.EvaluationResult == EvaluationResult.Timeout);
            domain.EvaluationOperations.RunSubmissions(withResult, true, breakAfterFirstFailedTest: true);
        }

        public void RerunWorkingSubmissions()
        {
            if (domain.IsMaintenanceMode)
            {
                domain.Log.Warning("Interaktion: System ist im Wartungsmodus");
                return;
            }

            var withResult = domain.ProviderStore.FileProvider.LoadAllSubmissions().Where(x =>
                x.EvaluationResult == EvaluationResult.Succeeded || x.EvaluationResult == EvaluationResult.SucceededWithTimeout);
            domain.EvaluationOperations.RunSubmissions(withResult, breakAfterFirstFailedTest: true);
        }

        public void UnlockChallengesForMembers()
        {
            if (domain.IsMaintenanceMode)
            {
                domain.Log.Warning("Interaktion: System ist im Wartungsmodus");
                return;
            }

            domain.Log.Information("Interaktion: Prüfe auf nicht freigeschaltete Challenges für Benutzer");
            var members = domain.MemberProvider.GetMembers();
            foreach (var member in members)
            {
                StatisticsOperations.FixNotSolvedChallengesForMember(member, domain.ProviderStore.FileProvider, domain.MemberProvider, domain.Log);
                StatisticsOperations.UnlockChallengesForMember(member, domain.ProviderStore.FileProvider, domain.MemberProvider);
            }
        }

        public void RerunBrokenSubmissions()
        {
            if (domain.IsMaintenanceMode)
            {
                domain.Log.Warning("Interaktion: System ist im Wartungsmodus");
                return;
            }

            var withResult = domain.ProviderStore.FileProvider.LoadAllSubmissions().Where(x =>
                x.EvaluationResult == EvaluationResult.UnknownError || x.EvaluationResult == EvaluationResult.CompilationError);
            domain.EvaluationOperations.RunSubmissions(withResult, true, true, breakAfterFirstFailedTest: true);
        }

        public void RerunSubmission(string pattern)
        {
            if (domain.IsMaintenanceMode)
            {
                domain.Log.Warning("Interaktion: System ist im Wartungsmodus");
                return;
            }

            var submissions = domain.ProviderStore.FileProvider.LoadAllSubmissions().Where(x =>
                x.SubmissionPath.IndexOf(pattern, StringComparison.CurrentCultureIgnoreCase) >= 0);
            domain.EvaluationOperations.RunSubmissions(submissions, true, true, breakAfterFirstFailedTest: true);
        }

        public void RerunChallengeSubmissions(string challengeName)
        {
            if (domain.IsMaintenanceMode)
            {
                domain.Log.Warning("Interaktion: System ist im Wartungsmodus");
                return;
            }

            var challenge = domain.ProviderStore.FileProvider.LoadChallenge(challengeName);
            var submissions = domain.ProviderStore.FileProvider.LoadAllSubmissionsFor(challenge);
            foreach (var submission in submissions)
            {
                domain.ProviderStore.FileProvider.MarkSubmissionForRerun(submission);
                NewSubmission?.Invoke(submission);
            }
        }

        public void RerunSubmission(string challengeName, string id)
        {
            if (domain.IsMaintenanceMode)
            {
                domain.Log.Warning("Interaktion: System ist im Wartungsmodus");
                return;
            }

            var submission = domain.ProviderStore.FileProvider.LoadResult(challengeName, id);
            if (submission != null)
            {
                domain.ProviderStore.FileProvider.MarkSubmissionForRerun(submission);
                NewSubmission?.Invoke(submission);
            }
        }

        public void MarkOldReviews()
        {
            if (domain.IsMaintenanceMode)
            {
                domain.Log.Warning("Interaktion: System ist im Wartungsmodus");
                return;
            }

            domain.Log.Information("Interaktion: Suche nach alten Einreichungen ohne Review");
            domain.ReviewOperations.FindAndSkipOldReviews();
        }

        public void PromoteNewReviewers()
        {
            if (domain.IsMaintenanceMode)
            {
                domain.Log.Warning("Interaktion: System ist im Wartungsmodus");
                return;
            }

            domain.Log.Information("Interaktion: Prüfe nach potentiellen neuen Reviewern");
            var ranklist = domain.ProviderStore.FileProvider.LoadGlobalRanklist();
            domain.ReviewOperations.ActivateNewReviewers();
            domain.ReviewOperations.UpgradeReviewerLevels();
            domain.ReviewOperations.UpdateReviewerLanguages(ranklist);
        }

        public void UpdateChallengeRankings()
        {
            if (domain.IsMaintenanceMode)
            {
                domain.Log.Warning("Interaktion: System ist im Wartungsmodus");
                return;
            }

            domain.Log.Information("Interaktion: Aktualisiere Challenge Ranking");
            domain.StatisticsOperations.UpdateGlobalRanking();
        }

        public void IdentifyInactiveMembers()
        {
            if (domain.IsMaintenanceMode)
            {
                domain.Log.Warning("Interaktion: System ist im Wartungsmodus");
                return;
            }

            domain.Log.Information("Interaktion: Identifiziere inaktive Mitglieder");
            domain.Maintenance.MarkInactiveUsers();
        }

        public void LoadCompilerData()
        {
            if (domain.IsMaintenanceMode)
            {
                domain.Log.Warning("Interaktion: System ist im Wartungsmodus");
                return;
            }

            domain.Log.Information("Interaktion: Lade Compiler Informationen");
            domain.StatisticsOperations.LoadCompilerInfo(domain.Compilers);
        }

        public void AddActivityEntry(Activity activity)
        {
            if (domain.IsMaintenanceMode)
            {
                domain.Log.Warning("Interaktion: System ist im Wartungsmodus");
                return;
            }

            using var writeLock = domain.ProviderStore.FileProvider.GetLock();
            var activities = domain.ProviderStore.FileProvider.LoadRecentActivities(writeLock);
            var today = activities.Where(x => x.Date.Year == activity.Date.Year && x.Date.Month == activity.Date.Month && x.Date.Day == activity.Date.Day);
            if (!today.Any(x => x.Type == activity.Type && x.UserId == activity.UserId && x.Reference == activity.Reference))
            {
                activities.Insert(0, activity);
                while (activities.Count > 50)
                {
                    activities.RemoveAt(activities.Count - 1);
                }

                domain.ProviderStore.FileProvider.SaveRecentActivities(activities, writeLock);
            }
        }

        public void ResetSystem(List<string> membersToKeep)
        {
            if (domain.IsMaintenanceMode)
            {
                domain.Log.Warning("Interaktion: System ist im Wartungsmodus");
                return;
            }

            if (membersToKeep.Count == 0)
            {
                Console.WriteLine("Keine Member-ID angegeben");
                return;
            }

            var lockedMembers = membersToKeep.Select(x => domain.MemberProvider.GetMemberById(x)).ToList();

            Console.WriteLine("Folgende Members beibehalten:");
            foreach (var member in lockedMembers)
            {
                Console.WriteLine("- " + member.Name);
            }

            UserInteractionOperations.AskUserToContinue("Soll das System wirklich zurückgesetzt werden?", () =>
            {
                var members = domain.MemberProvider.GetMembers().ToList();
                foreach (var member in members)
                {
                    if (lockedMembers.All(x => x.Id != member.Id))
                    {
                        domain.MemberProvider.DeleteMember(member);
                    }
                }

                domain.ProviderStore.FileProvider.DeleteAllSubmissions();
                domain.ProviderStore.FileProvider.DeleteAllStatistics();
                domain.Maintenance.ResetChallengesAndFixId(lockedMembers.First().Id);
            }, () => Console.WriteLine("Abgebrochen"));
        }

        public void InitialSetup()
        {
            if (domain.IsMaintenanceMode)
            {
                domain.Log.Warning("Interaktion: System ist im Wartungsmodus");
                return;
            }

            domain.Log.Information("Interaktion: Initialisiere System");
            domain.ProviderStore.FileProvider.CreateMissingDirectories();
        }

        public void ProcessReview(string id, string challenge)
        {
            if (domain.IsMaintenanceMode)
            {
                domain.Log.Warning("Interaktion: System ist im Wartungsmodus");
                return;
            }

            var submission = domain.ProviderStore.FileProvider.LoadResult(challenge, id);
            var reviewData = domain.ProviderStore.FileProvider.LoadReview(submission);
            domain.ReviewOperations.ProcessReview(reviewData);
        }

        public void CreateBundle(string id, string title, string description, string authorId, string category, List<string> challenges)
        {
            if (domain.IsMaintenanceMode)
            {
                domain.Log.Warning("Interaktion: System ist im Wartungsmodus");
                return;
            }

            domain.Log.Information("Interaktion: Erzeuge neues Bundle {bundle}", id);
            domain.ProviderStore.FileProvider.CreateBundle(id, title, description, authorId, category, challenges);
            ChallengeOperations.UpdateIsBundleFlag(domain.ProviderStore.FileProvider);
        }

        public void EditBundle(IMember member, Bundle bundle)
        {
            if (domain.IsMaintenanceMode)
            {
                domain.Log.Warning("Interaktion: System ist im Wartungsmodus");
                return;
            }

            domain.Log.Information("Interaktion: Editiere Bundle {bundle}", bundle.Id);

            using (var writeLock = domain.ProviderStore.FileProvider.GetLock())
            {
                var updating = domain.ProviderStore.FileProvider.LoadBundle(bundle.Id, writeLock);
                if (!member.IsAdmin && member.Id != updating.Author)
                {
                    throw new UnauthorizedAccessException("User " + member.Name + " has no access to edit bundle " + updating.Title);
                }

                updating.Title = bundle.Title;
                updating.Description = bundle.Description;
                updating.Category = bundle.Category;
                updating.Challenges = bundle.Challenges;
                updating.HasPreviousChallengesCheck = bundle.HasPreviousChallengesCheck;
                domain.ProviderStore.FileProvider.SaveBundle(updating, writeLock);
            }

            ChallengeOperations.UpdateIsBundleFlag(domain.ProviderStore.FileProvider);
        }

        public void PublishBundle(IMember member, string id)
        {
            if (domain.IsMaintenanceMode)
            {
                domain.Log.Warning("Interaktion: System ist im Wartungsmodus");
                return;
            }

            domain.Log.Information("Interaktion: Veröffentliche Bundle {bundle}", id);

            using var writeLock = domain.ProviderStore.FileProvider.GetLock();
            var updating = domain.ProviderStore.FileProvider.LoadBundle(id, writeLock);
            if (!member.IsAdmin && member.Id != updating.Author)
            {
                throw new UnauthorizedAccessException("User " + member.Name + " has no access to edit bundle " + updating.Title);
            }

            if (!updating.IsDraft)
            {
                throw new Exception("Bundle is already published");
            }

            updating.IsDraft = false;
            domain.ProviderStore.FileProvider.SaveBundle(updating, writeLock);
        }

        public void ReportErrornuousSubmission(string challenge, string id)
        {
            if (domain.IsMaintenanceMode)
            {
                domain.Log.Warning("Interaktion: System ist im Wartungsmodus");
            }
        }

        public void DisableMaintenanceMode()
        {
            domain.Log.Warning("Interaktion: Wartungsmodus deaktiviert");
            domain.IsMaintenanceMode = false;
        }

        public void EnableMaintenanceMode()
        {
            domain.Log.Warning("Interaktion: Wartungsmodus aktiviert");
            domain.IsMaintenanceMode = true;
        }

        public void Migrate()
        {
        }

        public void RemoveUserRole(IMember member, string role)
        {
            if (domain.IsMaintenanceMode)
            {
                domain.Log.Warning("Interaktion: System ist im Wartungsmodus");
                return;
            }

            domain.MemberProvider.UpdateRoles(member, member.Roles.Where(x => x != role).ToArray());
        }

        public void AddUserRole(IMember member, string role)
        {
            if (domain.IsMaintenanceMode)
            {
                domain.Log.Warning("Interaktion: System ist im Wartungsmodus");
                return;
            }

            if (member.Roles.Any(x => x == role))
            {
                return;
            }

            var roles = new List<string>(member.Roles) {role};
            domain.MemberProvider.UpdateRoles(member, roles.ToArray());
        }

        public void IncreaseReviewLevel(IMember member)
        {
            if (domain.IsMaintenanceMode)
            {
                domain.Log.Warning("Interaktion: System ist im Wartungsmodus");
                return;
            }

            if (member.ReviewLevel == ReviewLevel.Master)
            {
                return;
            }

            domain.MemberProvider.UpdateReviewLevel(member, member.ReviewLevel + 1);
        }

        public void ActivatePendingMember(IMember member)
        {
            if (domain.IsMaintenanceMode)
            {
                domain.Log.Warning("Interaktion: System ist im Wartungsmodus");
                return;
            }

            domain.MemberProvider.ActivatePendingMember(member);
        }

        public void EditHelpPage(HelpPage helpPage)
        {
            if (domain.IsMaintenanceMode)
            {
                domain.Log.Warning("Interaktion: System ist im Wartungsmodus");
                return;
            }

            using var writeLock = domain.ProviderStore.FileProvider.GetLock();
            var updated = domain.ProviderStore.FileProvider.LoadHelpPage(helpPage.Path, true, writeLock);
            updated.Description = helpPage.Description;
            updated.Parent = helpPage.Parent;
            updated.Title = helpPage.Title;
            domain.ProviderStore.FileProvider.SaveHelpPage(updated, writeLock);
        }

        public void SaveLastVersionHash(string lastVersionHash)
        {
            if (domain.IsMaintenanceMode)
            {
                domain.Log.Warning("Interaktion: System ist im Wartungsmodus");
                return;
            }

            domain.ProviderStore.FileProvider.SaveLastVersionHash(lastVersionHash);
        }

        public void AnalyseCodeDuplicates()
        {
            if (domain.IsMaintenanceMode)
            {
                domain.Log.Warning("Interaktion: System ist im Wartungsmodus");
                return;
            }

            domain.Log.Information("Interaktion: Suche nach Code Duplikaten:");
            var fp = domain.ProviderStore.FileProvider;
            var challenges = fp.GetChallengeIds();
            foreach (var challenge in challenges)
            {
                var allSubmission = fp.LoadAllSubmissionsFor(fp.LoadChallenge(challenge));
                var submissions = allSubmission.Where(x => x.IsPassed).OrderBy(x => x.SubmissionDate).Select(x => new DupeCheck {Result = x}).ToList();
                var notChecked = submissions.Where(x => x.Result.DuplicateScore == null).ToList();
                if (notChecked.Count > 0)
                {
                    domain.Log.Information("- " + challenge);
                    foreach (var sub in submissions)
                    {
                        var files = fp.GetSubmissionFilesPath(sub.Result);
                        var compiler = domain.CompilerOperations.GetCompilerForContent(files);
                        var srcFiles = compiler.GetSourceFiles(files).Select(x => Regex.Replace(File.ReadAllText(x).ToLower(), @"[\s\n\r]+", string.Empty));
                        sub.Source = srcFiles.ToArray();
                    }
                    
                    foreach (var toCheck in notChecked)
                    {
                        var bestScore = 0;
                        Result bestMatch = null;
                        foreach (var sub in submissions.Where(x => x.Result.SubmissionDate < toCheck.Result.SubmissionDate && x.Result.SubmissionDate >= toCheck.Result.SubmissionDate.AddDays(-Settings.DuplicateCheckWindow)))
                        {
                            var score = EvaluateDuplicationScore(toCheck, sub);
                            if (score > bestScore)
                            {
                                bestScore = score;
                                bestMatch = sub.Result;
                            }
                        }

                        fp.SetDuplicateScore(toCheck.Result, bestScore, bestMatch?.SubmissionId);
                    }
                }
            }
        }

        private int EvaluateDuplicationScore(DupeCheck toCheck, DupeCheck otherToCheck)
        {
            if (otherToCheck.Result.MemberId == toCheck.Result.MemberId)
            {
                return 0;
            }

            double scoreSum = 0;
            var otherSrc = otherToCheck.Source.ToList();
            foreach (var src in toCheck.Source)
            {
                double bestScore = 0;
                var bestMatch = -1;
                for (var i = 0; i < otherSrc.Count; i++)
                {
                    var other = otherSrc[i];
                    var score = src.DetermineProbabilityOfDuplicate(other);
                    if (score > bestScore)
                    {
                        bestScore = score;
                        bestMatch = i;
                    }
                }

                if (bestMatch >= 0)
                {
                    otherSrc.RemoveAt(bestMatch);
                }

                scoreSum += bestScore;
            }

            return (int) (100 * scoreSum / Math.Max(toCheck.Source.Length, otherToCheck.Source.Length));
        }

        public string RunTestGenerator(string challengeId, string submissionId, string input, string[] arguments)
        {
            if (domain.IsMaintenanceMode)
            {
                domain.Log.Warning("Interaktion: System ist im Wartungsmodus");
                return "";
            }

            domain.Log.Information($"Interaktion: Generiere Test für {challengeId} aus {submissionId}");
            var challenge = domain.ProviderStore.FileProvider.LoadChallenge(challengeId);
            var submission = domain.ProviderStore.FileProvider.LoadResult(challengeId, submissionId);

            return new TestGeneratorOperations
            {
                Log = domain.Log,
                SandboxedProcessProvider = domain.ProviderStore.SandboxedProcessProvider,
                FileProvider = domain.ProviderStore.FileProvider,
                CompilerOperations = domain.CompilerOperations
            }.GenerateTest(challenge, submission, input, arguments);
        }

        public void RemoveDeadSubmissions(IMember member, IChallenge challenge)
        {
            if (domain.IsMaintenanceMode)
            {
                domain.Log.Warning("Interaktion: System ist im Wartungsmodus");
                return;
            }

            if (!member.IsAdmin && challenge.AuthorID != member.Id)
            {
                throw new UnauthorizedAccessException($"Nicht mehr lauffähige Submissions für {challenge.Id} durch {member.Name} nicht löschbar.");
            }

            var failed = 0;
            var submissions = domain.ProviderStore.FileProvider.LoadAllSubmissionsFor(challenge).OrderByDescending(x => x.LastSubmissionDate);
            var toBeDeleted = submissions.SkipWhile(x => x.IsPassed || !x.ReportFailing);
            foreach (var submission in toBeDeleted)
            {
                if (submission.IsPassed)
                {
                    continue;
                }

                if (submission.EvaluationResult == EvaluationResult.TestsFailed || submission.EvaluationResult == EvaluationResult.Timeout)
                {
                    failed++;
                }

                domain.ProviderStore.FileProvider.MarkSubmissionAsDead(submission);
            }

            using var writeLock = domain.ProviderStore.FileProvider.GetLock();
            var updating = domain.ProviderStore.FileProvider.LoadChallenge(challenge.Id, writeLock);
            updating.State.FailedCount -= failed;
            domain.ProviderStore.FileProvider.SaveChallenge(updating, writeLock);
        }

        public void DeleteChallenge(IMember member, IChallenge challenge)
        {
            if (domain.IsMaintenanceMode)
            {
                domain.Log.Warning("Interaktion: System ist im Wartungsmodus");
                return;
            }

            domain.Log.Information("Interaktion: Lösche Challenge {challenge} durch {member}", challenge.Id, member.Name);
            if (!member.IsAdmin && member.Id != challenge.AuthorID)
            {
                throw new UnauthorizedAccessException($"{member.Name} darf Challenge {challenge.Id} nicht löschen!");
            }

            var fileProvider = domain.ProviderStore.FileProvider;
            domain.ProviderStore.FileProvider.DeleteChallenge(challenge);
            SubmissionOperations.DeleteChallengeSubmissions(challenge, fileProvider);
            ChallengeOperations.DeleteChallengeInRecentActivities(challenge, fileProvider);
            ChallengeOperations.DeleteChallengeInBundles(challenge, fileProvider);
            ChallengeOperations.DeleteChallengeInGroups(challenge, fileProvider);
            ChallengeOperations.DeleteChallengeInMembers(challenge, fileProvider);
        }

        public void DuplicateTest(IMember member, string challengeId, int testid)
        {
            if (domain.IsMaintenanceMode)
            {
                domain.Log.Warning("Interaktion: System ist im Wartungsmodus");
                return;
            }

            domain.Log.Information("Interaktion: Dupliziere Test {id} für {challenge} durch {member}", testid, challengeId, member.Name);
            using var writeLock = domain.ProviderStore.FileProvider.GetLock();
            var challenge = domain.ProviderStore.FileProvider.LoadChallenge(challengeId, writeLock);
            if (!member.IsAdmin && member.Id != challenge.AuthorID)
            {
                throw new Exception("Access denied for User " + member.Id);
            }

            var tests = domain.ProviderStore.FileProvider.LoadTestProperties(challenge).ToList();
            var cloneTest = tests.ElementAt(testid);
            tests.Add(new TestParameters
            {
                ClearSandbox = cloneTest.ClearSandbox,
                CustomTestRunner = cloneTest.CustomTestRunner,
                ExpectedOutput = cloneTest.ExpectedOutput,
                ExpectedOutputFile = cloneTest.ExpectedOutputFile,
                Hint = cloneTest.Hint,
                IncludePreviousTests = cloneTest.IncludePreviousTests,
                Input = cloneTest.Input,
                InputFiles = cloneTest.InputFiles,
                Parameters = cloneTest.Parameters,
                Timeout = cloneTest.Timeout
            });
            domain.ProviderStore.FileProvider.SaveTestProperties(challenge, tests, writeLock);
        }

        public void IncreaseChallengeFeasibilityIndex(IMember member, string challengeId, int levels = 1)
        {
            if (domain.IsMaintenanceMode)
            {
                domain.Log.Warning("Interaktion: System ist im Wartungsmodus");
                return;
            }

            domain.Log.Information("Interaktion: Verbessere Schwierigkeitseinstufung für {challenge} durch {member}", challengeId, member.Name);

            var challenge = domain.ProviderStore.FileProvider.LoadChallenge(challengeId);
            if (!member.IsAdmin && member.Id != challenge.AuthorID)
            {
                throw new Exception("Access denied for User " + member.Id);
            }

            IncreaseFeasibility(challengeId, levels);
        }

        private void IncreaseFeasibility(string challengeId, int levels = 1)
        {
            using var writeLock = domain.ProviderStore.FileProvider.GetLock();
            var challenge = domain.ProviderStore.FileProvider.LoadChallenge(challengeId, writeLock);
            var challenges = domain.ProviderStore.FileProvider.LoadChallenges().ToList();
            var max = challenges.Max(x => x.State.FeasibilityIndex);
            var sorted = challenges.OrderBy(x => x.State.FeasibilityIndex > 0 ? x.State.FeasibilityIndex : max).ToList();
            var feasibilityIndex = challenge.State.FeasibilityIndex;
            var challengeBefore = sorted.FirstOrDefault(x => x.State.FeasibilityIndex > feasibilityIndex);
            if (challengeBefore != null && !challengeBefore.StickAsBeginner)
            {
                var diff = levels + challengeBefore.State.FeasibilityIndex - challenge.State.FeasibilityIndex;
                challenge.State.FeasibilityIndexMod += diff;
                challenge.State.FeasibilityIndex += diff;
                domain.StatisticsOperations.UpdateFeasibilityIndexForChallenge(challenge);
                domain.ProviderStore.FileProvider.SaveChallenge(challenge, writeLock);
            }
        }

        public void DecreaseChallengeFeasibilityIndex(IMember member, string challengeId, int levels = 1)
        {
            if (domain.IsMaintenanceMode)
            {
                domain.Log.Warning("Interaktion: System ist im Wartungsmodus");
                return;
            }

            domain.Log.Information("Interaktion: Veringere Schwierigkeitseinstufung für {challenge} durch {member}", challengeId, member.Name);
            var challenge = domain.ProviderStore.FileProvider.LoadChallenge(challengeId);
            if (!member.IsAdmin && member.Id != challenge.AuthorID)
            {
                throw new Exception("Access denied for User " + member.Id);
            }

            DecreaseFeasibility(challengeId, levels);
        }

        private void DecreaseFeasibility(string challengeId, int levels = 1)
        {
            using var writeLock = domain.ProviderStore.FileProvider.GetLock();
            var challenge = domain.ProviderStore.FileProvider.LoadChallenge(challengeId, writeLock);
            if (challenge.State.FeasibilityIndex == 0 || challenge.StickAsBeginner)
            {
                return;
            }

            var challenges = domain.ProviderStore.FileProvider.LoadChallenges().ToList();
            var feasibilityIndex = challenge.State.FeasibilityIndex;
            var challengeAfter = challenges.Where(x => x.State.FeasibilityIndex < feasibilityIndex).OrderByDescending(x => x.State.FeasibilityIndex)
                .FirstOrDefault();
            if (challengeAfter != null)
            {
                var diff = levels + feasibilityIndex - challengeAfter.State.FeasibilityIndex;
                challenge.State.FeasibilityIndexMod -= diff;
                challenge.State.FeasibilityIndex -= diff;
                domain.StatisticsOperations.UpdateFeasibilityIndexForChallenge(challenge);
                domain.ProviderStore.FileProvider.SaveChallenge(challenge, writeLock);
            }
        }

        public void PublishChallenge(string challengeId, IMember member)
        {
            if (domain.IsMaintenanceMode)
            {
                domain.Log.Warning("Interaktion: System ist im Wartungsmodus");
                return;
            }

            domain.Log.Information("Interaktion: Veröffentliche {challenge} durch {member}", challengeId, member.Name);
            using (var writeLock = domain.ProviderStore.FileProvider.GetLock())
            {
                var challenge = domain.ProviderStore.FileProvider.LoadChallenge(challengeId, writeLock);
                if (!challenge.IsDraft)
                {
                    throw new Exception("Challenge can not be published");
                }

                if (challenge.State.HasError)
                {
                    throw new Exception("Challenge can not be published");
                }

                if (!member.IsAdmin && challenge.AuthorID != member.Id)
                {
                    throw new Exception("Challenge can not be published");
                }

                challenge.IsDraft = false;
                challenge.Date = DateTime.Now;
                domain.ProviderStore.FileProvider.SaveChallenge(challenge, writeLock);
            }

            domain.Log.Activity(member.Id, ActivityType.NewChallenge, challengeId);
        }

        public IMember AddMember(string fullname, string mail, string uid = null, bool temporaryUser = false)
        {
            var member = domain.MemberProvider.AddMember(fullname, mail, uid, temporaryUser);
            StatisticsOperations.UnlockChallengesForMember(member, domain.ProviderStore.FileProvider, domain.MemberProvider);
            return member;
        }

        public void CreateGroup(IMember member, string id, string title, List<string> groupAdminIds, bool isSuperGroup, string[] subGroups, string[] forcedChallenges, string[] availableChallenges,
            int maxUnlockedChallenges, int? requiredPoints, DateTime? startDate, DateTime? endDate)
        {
            if (requiredPoints == 0)
            {
                requiredPoints = null;
            }

            domain.ProviderStore.FileProvider.CreateGroup(id, title, groupAdminIds, isSuperGroup, subGroups, forcedChallenges, availableChallenges, maxUnlockedChallenges,
                requiredPoints, startDate, endDate);
        }

        public void EditGroup(IMember member, string id, string title, List<string> groupAdminIds, bool isSuperGroup, string[] subGroups, string[] forcedChallenges, string[] availableChallenges,
            int maxUnlockedChallenges, int? requiredPoints, DateTime? startDate, DateTime? endDate)
        {
            using var writeLock = domain.ProviderStore.FileProvider.GetLock();
            var group = domain.ProviderStore.FileProvider.LoadGroup(id, writeLock);
            @group.Title = title;
            @group.GroupAdminIds = groupAdminIds ?? new List<string>();
            @group.ForcedChallenges = forcedChallenges;
            @group.AvailableChallenges = availableChallenges;
            @group.MaxUnlockedChallenges = maxUnlockedChallenges;
            @group.RequiredPoints = requiredPoints;
            @group.StartDate = startDate;
            @group.EndDate = endDate;
            @group.IsSuperGroup = isSuperGroup;
            @group.SubGroups = subGroups;
            domain.ProviderStore.FileProvider.SaveGroup(@group, writeLock);
        }

        public void DeleteGroup(string id)
        {
            var membersWithGroup = domain.MemberProvider.GetMembers().Where(x => x.Groups.Contains(id));
            foreach (var member in membersWithGroup)
            {
                using var writeLock = domain.ProviderStore.FileProvider.GetLock();
                var updated = domain.ProviderStore.FileProvider.LoadMember(member.Id, writeLock);
                updated.Groups = updated.Groups.Where(x => x != id).ToArray();
                domain.ProviderStore.FileProvider.SaveMember(updated, writeLock);
            }

            domain.ProviderStore.FileProvider.DeleteGroup(id);
        }

        public void VerifyMembers()
        {
            try
            {
                domain.Log.Warning("Interaktion: Prüfe auf nicht existente Member");
                var members = domain.MemberProvider.GetMembers().ToDictionary(x => x.Id);
                var firstAdmin = members.Values.OrderBy(x => x.DateOfEntry).First(x => x.IsAdmin);
                foreach (var challenge in domain.ProviderStore.FileProvider.LoadChallenges())
                {
                    if (!members.ContainsKey(challenge.AuthorID))
                    {
                        using var writeLock = domain.ProviderStore.FileProvider.GetLock();
                        var update = domain.ProviderStore.FileProvider.LoadChallenge(challenge.Id, writeLock);
                        update.AuthorID = firstAdmin.Id;
                        domain.ProviderStore.FileProvider.SaveChallenge(update, writeLock);
                    }
                }

                foreach (var bundle in domain.ProviderStore.FileProvider.LoadAllBundles())
                {
                    if (!members.ContainsKey(bundle.Author))
                    {
                        using var writeLock = domain.ProviderStore.FileProvider.GetLock();
                        var update = domain.ProviderStore.FileProvider.LoadBundle(bundle.Id, writeLock);
                        update.Author = firstAdmin.Id;
                        domain.ProviderStore.FileProvider.SaveBundle(update, writeLock);
                    }
                }
            }
            catch (InvalidOperationException)
            {
                domain.Log.Warning("No user found. Starting initial setup. If the system already got setup, there may be an error.");
            }
        }

        public void UploadChallenge(byte[] data)
        {
            if (domain.IsMaintenanceMode)
            {
                domain.Log.Warning("Interaktion: System ist im Wartungsmodus");
                return;
            }

            domain.Log.Information("Interaktion: Hochladen einer Challenge");

            var challenge = domain.ProviderStore.FileProvider.LoadChallengeFromZip(data);

            if (domain.MemberProvider.GetMemberById(challenge.Item1.AuthorID) == null)
            {
                challenge.Item1.AuthorID = domain.MemberProvider.GetMembers().OrderBy(x => x.DateOfEntry).First(x => x.IsAdmin).Id;
            }

            if (domain.ProviderStore.FileProvider.ChallengeExists(challenge.Item1.Id))
            {
                using var writeLock = domain.ProviderStore.FileProvider.GetLock();
                UpdateChallenge(challenge.Item1, writeLock);
            }
            else
            {
                domain.ProviderStore.FileProvider.CreateChallenge(challenge.Item1);
            }

            var updated = domain.ProviderStore.FileProvider.LoadChallenge(challenge.Item1.Id);
            ChallengeOperations.UpdateTestsForChallenge(domain, updated, challenge.Item2);
            foreach (var add in challenge.Item3)
            {
                domain.ProviderStore.FileProvider.SaveAdditionalFile(updated, add.name, add.data);
            }

            ChallengeOperations.VerifyChallenge(domain, challenge.Item1.Id);
        }

        public void RateChallenge(IMember member, string id, RatingType rating)
        {
            if (domain.IsMaintenanceMode)
            {
                domain.Log.Warning("Interaktion: System ist im Wartungsmodus");
                return;
            }

            domain.Log.Information($"Interaktion: User {member.Name} bewertet {id}");
            if (member.CanRate.Contains(id))
            {
                switch (rating)
                {
                    case RatingType.Good: break;
                    case RatingType.ToEasy:
                        IncreaseFeasibility(id);
                        break;
                    case RatingType.ToHard:
                        DecreaseFeasibility(id);
                        break;
                    case RatingType.Unclear: break;
                }

                var canRate = member.CanRate.Where(x => x != id).ToArray();
                domain.MemberProvider.UpdateCanRate(member, canRate);
            }
        }
    }
}
