using System;
using System.Collections.Generic;
using System.Linq;
using SubmissionEvaluation.Contracts.Data;
using SubmissionEvaluation.Contracts.Data.Ranklist;
using SubmissionEvaluation.Contracts.Data.Review;
using SubmissionEvaluation.Contracts.Exceptions;
using SubmissionEvaluation.Contracts.Providers;
using SubmissionEvaluation.Domain.Review;

namespace SubmissionEvaluation.Domain.Operations
{
    internal class ReviewOperations
    {
        private const int OLD_REVIEW_THRESHOLD_DAYS = 30;

        public ReviewOperations(bool isReviewEnabled, bool enableReviewerPromotion)
        {
            IsReviewEnabled = isReviewEnabled;
            IsReviewerPromotionEnabled = enableReviewerPromotion;
        }

        public bool IsReviewEnabled { get; }
        public bool IsReviewerPromotionEnabled { get; }

        public ProviderStore ProviderStore { private get; set; }
        public ISmtpProvider SmtpProvider { private get; set; }
        public IMemberProvider MemberProvider { private get; set; }
        public ILog Log { set; private get; }
        public List<ICompiler> Compilers { private get; set; }
        public EvaluationOperations EvaluationOperations { private get; set; }

        public bool ProcessReview(ReviewData reviewData)
        {
            if (!IsReviewEnabled)
            {
                return false;
            }

            var result = ProviderStore.FileProvider.LoadResult(reviewData.Challenge, reviewData.Id);
            var reviewTemplate = LoadReviewTemplateForChallenge(reviewData.Challenge);
            new ReviewGrader(reviewTemplate).GradeReview(reviewData);
            ProviderStore.FileProvider.SaveReview(result, reviewData);
            var stars = CalculateStars(reviewData);

            var submissionAuthor = MemberProvider.GetMemberById(result.MemberId);
            var stateCorrect = result.ReviewState == ReviewStateType.InProgress;
            if (!stateCorrect)
            {
                Log.Warning("Durchlaufe Review erneut, obwohl schon bewertet!");
            }
            else
            {
                Log.Activity(submissionAuthor.Id, ActivityType.NewReviewResult);
            }

            var reviewer = MemberProvider.GetMemberById(result.Reviewer);
            SetReviewStateAsReviewd(result, stars);
            MemberProvider.IncreaseReviewFrequency(reviewer);
            MemberProvider.IncreaseReviewCounter(reviewer);
            return stateCorrect;
        }

        public ReviewRating GetReviewRating(ReviewData reviewData)
        {
            var reviewTemplate = LoadReviewTemplateForChallenge(reviewData.Challenge);
            return GenerateReviewRatingList(reviewTemplate, reviewData);
        }

        private int CalculateStars(ReviewData reviewData)
        {
            var result = ProviderStore.FileProvider.LoadResult(reviewData.Challenge, reviewData.Id);
            var reviewTemplate = LoadReviewTemplateForChallenge(reviewData.Challenge);

            var rating = GenerateReviewRatingList(reviewTemplate, reviewData); //Ist in Schulnoten gehalten
            var compiler = Compilers.SingleOrDefault(x => x.Name == result.Language);

            var stars = CalculateReviewRating(rating);
            return stars;
        }

        public List<Result> ReviewableSubmissions(IMember member = null)
        {
            var submissions = GetReviewableSubmissions();
            if (member != null)
            {
                submissions = submissions.Where(x =>
                {
                    var prop = ProviderStore.FileProvider.LoadChallenge(x.Challenge);
                    return CanReviewMoreReviewsAtCurrentTime(x, member, prop.State.IsPartOfBundle, prop.State.DifficultyRating ?? 100, 0, 100).Item1;
                });
            }

            return submissions.ToList();
        }

        private IEnumerable<Result> GetReviewableSubmissions()
        {
            var reviewableChallenges = LoadReviewableChallenges();
            var passed = reviewableChallenges.SelectMany(x => ProviderStore.FileProvider.LoadAllSubmissionsFor(x))
                .Where(x => x.EvaluationState == EvaluationState.Evaluated && x.IsPassed);
            var orderedSubmissions = passed.OrderByDescending(x => x.SubmissionDate).ToList();
            var bundles = ProviderStore.FileProvider.LoadAllBundles().SelectMany(x => x.Challenges.Select(y => new {Key = y, Value = x.Id}))
                .ToDictionary(x => x.Key, x => x.Value);
            RemoveOlderDuplicatedSubmissions(orderedSubmissions, bundles);
            var reviewableSubmissions = orderedSubmissions.Where(x => x.ReviewState != ReviewStateType.Reviewed && x.ReviewState != ReviewStateType.Skipped);
            return reviewableSubmissions.Where(x => !ProviderStore.FileProvider.HasReviewFile(x));
        }

        private ReviewRating GenerateReviewRatingList(ReviewTemplate reviewTemplate, ReviewData reviewData)
        {
            var rating = new ReviewRating();
            BuildReviewRatingList(rating, reviewTemplate.Categories, reviewData);
            return rating;
        }

        private static void BuildReviewRatingList(ReviewRating parent, IEnumerable<ReviewCategory> categories, ReviewData reviewData)
        {
            // FIX: Order review ratings, as there are duplicates. Replace FirstOrDefault with SingleOrDefault after fix!
            var ratingsDescending = reviewData.CategoryResults.Where(p => p?.Grade.HasValue == true).OrderByDescending(x => x.Grade).ToList();
            foreach (var category in categories)
            {
                var ratingValue = ratingsDescending.FirstOrDefault(x => x.CategoryId == category.Id);
                var childRating = new ReviewRating
                {
                    Id = category.Id, Title = category.Title, Description = category.Description, Quantifier = category.Quantifier
                };
                if (ratingValue != null)
                {
                    if (ratingValue.Grade.HasValue)
                    {
                        var value = ratingValue.Grade.Value;
                        if (value < 1)
                        {
                            childRating.Rating = null;
                        }
                        else if (value < 6)
                        {
                            childRating.Rating = value;
                        }
                        else
                        {
                            childRating.Rating = 6;
                        }
                    }
                    else
                    {
                        childRating.Rating = null;
                    }

                    childRating.Comment = ratingValue.CategoryComments;
                }

                parent.Childs.Add(childRating);
            }
        }

        public void CheckPendingReview(Result result, Action onReset, Action onRunReview)
        {
            if (result.ReviewState == ReviewStateType.InProgress)
            {
                var runned = false;
                var hasReviewFile = ProviderStore.FileProvider.HasReviewFile(result);
                if (hasReviewFile)
                {
                    try
                    {
                        onRunReview();
                        runned = true;
                    }
                    catch (Exception e)
                    {
                        Log.Error(e, "Review durchführen fehlgeschlagen");
                    }
                }

                if (!runned && result.ReviewDate.Value.AddDays(1) < DateTime.Today)
                {
                    ResetReview(result);
                    onReset();
                }
            }
        }

        private void SendReviewMail(IMember reviewer, Result result, int outstandingReviews, DateTime reviewDueDate)
        {
            Log.Information("Review-Email wird an {Reviewer} gesendet", reviewer.Mail);
            var reviewDays = (reviewDueDate - DateTime.Today).Days;
            var mailBody = MailMessages.CreateReviewMailBody(reviewer, result, outstandingReviews, reviewDays, ProviderStore.SiteUrl, ProviderStore.HelpEmail);
            var mailSubject = MailMessages.CreateReviewSubject(result);
            SmtpProvider.SendMail(reviewer.Mail, mailSubject, mailBody);
        }

        private int CalculateReviewRating(ReviewRating rating)
        {
            if (rating.Rating == null)
            {
                return 0;
            }

            var ratingValue = rating.Rating.Value;
            if (ratingValue <= 1.5)
            {
                return 5;
            }

            if (ratingValue <= 2)
            {
                return 4;
            }

            if (ratingValue <= 2.7)
            {
                return 3;
            }

            if (ratingValue <= 4)
            {
                return 2;
            }

            return 1;
        }

        public void UpdateReviewerLanguages(GlobalRanklist ranklist)
        {
            foreach (var member in MemberProvider.GetMembers().Where(x => x.ReviewLevel != ReviewLevel.Deactivated && x.ReviewLevel != ReviewLevel.Inactive))
            {
                var submittor = ranklist.Submitters.FirstOrDefault(x => x.Id == member.Id);
                var languages = submittor?.Languages?.Split(',').Select(x => x.Trim());
                if (languages != null)
                {
                    foreach (var language in languages.Where(x => !string.IsNullOrWhiteSpace(x) && member.ReviewLanguages?.Contains(x) != true))
                    {
                        MemberProvider.AddReviewLanguage(member, language);
                    }
                }
            }
        }

        public void UpgradeReviewerLevels()
        {
            if (!IsReviewerPromotionEnabled)
            {
                return;
            }

            foreach (var member in MemberProvider.GetMembers().Where(x => x.ReviewLevel != ReviewLevel.Deactivated && x.ReviewLevel != ReviewLevel.Inactive))
            {
                switch (member.ReviewLevel)
                {
                    case ReviewLevel.Beginner:
                        if (member.ReviewCounter >= 3)
                        {
                            MemberProvider.UpdateReviewLevel(member, ReviewLevel.Intermediate);
                        }

                        break;
                    case ReviewLevel.Intermediate:
                        if (member.ReviewCounter >= 10)
                        {
                            MemberProvider.UpdateReviewLevel(member, ReviewLevel.Advanced);
                        }

                        break;
                    case ReviewLevel.Advanced:
                        if (member.ReviewCounter >= 30)
                        {
                            MemberProvider.UpdateReviewLevel(member, ReviewLevel.Expert);
                        }

                        break;
                    case ReviewLevel.Expert:
                        if (member.ReviewCounter >= 100)
                        {
                            MemberProvider.UpdateReviewLevel(member, ReviewLevel.Master);
                        }

                        break;
                }
            }
        }

        public void ActivateNewReviewers()
        {
            if (!IsReviewerPromotionEnabled)
            {
                return;
            }

            foreach (var member in MemberProvider.GetMembers().Where(x =>
                x.ReviewLevel == ReviewLevel.Inactive && x.DateOfEntry <= DateTime.Today.AddDays(-30)))
            {
                MemberProvider.UpdateReviewLevel(member, ReviewLevel.Beginner);
            }
        }

        private Tuple<bool, string> CanReviewMoreReviewsAtCurrentTime(Result result, IMember reviewer, bool isBundle, int difficulty, int minDifficulty,
            int maxDifficulty)
        {
            var difficultyStep = (maxDifficulty - minDifficulty) / 3;

            if (result.MemberId == reviewer.Id)
            {
                return new Tuple<bool, string>(false, "Autor == Reviewer");
            }

            switch (reviewer.ReviewLevel)
            {
                case ReviewLevel.Beginner:
                    if (isBundle)
                    {
                        return new Tuple<bool, string>(false, "Es handelt sich um ein Bundle");
                    }

                    if (difficulty > minDifficulty + difficultyStep)
                    {
                        return new Tuple<bool, string>(false, "Aufgabe zu komplex");
                    }

                    break;
                case ReviewLevel.Intermediate:
                    if (difficulty > minDifficulty + difficultyStep)
                    {
                        return new Tuple<bool, string>(false, "Aufgabe zu komplex");
                    }

                    break;
                case ReviewLevel.Advanced:
                    if (difficulty > minDifficulty + 2 * difficultyStep)
                    {
                        return new Tuple<bool, string>(false, "Aufgabe zu komplex");
                    }

                    break;
                case ReviewLevel.Expert: break;
                case ReviewLevel.Master: return new Tuple<bool, string>(true, "");
            }

            var canReviewLanguage = reviewer.ReviewLanguages?.Contains(result.Language);
            if (canReviewLanguage.HasValue && canReviewLanguage.Value)
            {
                return new Tuple<bool, string>(true, "");
            }

            return new Tuple<bool, string>(false, "Sprache " + result.Language + " nicht erlaubt");
        }

        public void StartReview(Result reviewItem, IMember reviewer, int reviewsLeft)
        {
            try
            {
                var reviewDueDate = DateTime.Today.AddDays(8).AddSeconds(-1);
                SendReviewMail(reviewer, reviewItem, reviewsLeft, reviewDueDate);
                MemberProvider.UpdateLastReviewDate(reviewer);
                SetReviewStateAsStarted(reviewItem, reviewer.Id, reviewDueDate);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Neues Review starten fehlgeschlagen.");
            }
        }

        public Result FindReviewForReviewer(IEnumerable<Result> notYetReviewed, IMember reviewer, int minDifficulty, int maxDifficulty)
        {
            var rnd = new Random();
            return notYetReviewed.Where(x =>
            {
                var challenge = ProviderStore.FileProvider.LoadChallenge(x.Challenge);
                return CanReviewMoreReviewsAtCurrentTime(x, reviewer, challenge.State.IsPartOfBundle, challenge.State.DifficultyRating ?? 100, minDifficulty,
                    maxDifficulty).Item1;
            }).Skip(rnd.Next(4)).FirstOrDefault();
        }

        private static void RemoveOlderDuplicatedSubmissions(IList<Result> submissions, Dictionary<string, string> challengesInBundle)
        {
            bool Predicate(Result r1, Result r2)
            {
                if (r1.MemberId != r2.MemberId)
                {
                    return false;
                }

                if (r1.Challenge == r2.Challenge)
                {
                    return true;
                }

                challengesInBundle.TryGetValue(r1.Challenge, out var bundle1);
                if (bundle1 == null)
                {
                    return false;
                }

                challengesInBundle.TryGetValue(r2.Challenge, out var bundle2);
                return bundle1 == bundle2;
            }

            for (var i = 0; i < submissions.Count; i++)
            {
                var result = submissions[i];
                var dupes = submissions.Where(x => Predicate(x, result)).ToList();
                if (dupes.Count > 1)
                {
                    foreach (var dupe in dupes.Skip(1))
                    {
                        submissions.Remove(dupe);
                    }
                }
            }
        }

        public void FindAndSkipOldReviews()
        {
            foreach (var oldReview in ProviderStore.FileProvider.LoadAllSubmissions().Where(x =>
                x.ReviewState == ReviewStateType.NotReviewed && x.LastSubmissionDate < DateTime.Today.AddDays(-OLD_REVIEW_THRESHOLD_DAYS)))
            {
                SetReviewStateAsSkipped(oldReview);
            }
        }

        public void LogUnreviewedSubmissions(IReadOnlyCollection<Result> unreviewd)
        {
            Log.Information("Ausstehende Reviews ({count}):", unreviewd.Count);
            foreach (var language in unreviewd.GroupBy(x => x.Language))
            {
                Log.Information("- " + language.Key + " (" + language.Count() + ")");
            }
        }

        public Result StoreReviewSubmission(ReviewData reviewData)
        {
            var submission = ProviderStore.FileProvider.LoadResult(reviewData.Challenge, reviewData.Id);
            ProviderStore.FileProvider.SaveReview(submission, reviewData);
            return submission;
        }

        public ReviewTemplate LoadReviewTemplateForChallenge(string challenge)
        {
            var properties = ProviderStore.FileProvider.LoadChallenge(challenge);
            return ProviderStore.FileProvider.LoadReviewTemplate(properties);
        }

        public void ResetReview(ISubmission submission)
        {
            Log.Information("Ausstehendes Review wird abgebrochen {id}", submission.SubmissionId);
            ProviderStore.FileProvider.AbortRunningReview(submission);
        }

        public void SetReviewStateAsStarted(ISubmission submission, string reviewer, DateTime? reviewDueDate = null)
        {
            if (!submission.IsPassed)
            {
                throw new ReviewException("Submission not passed");
            }

            if (submission.MemberId == reviewer)
            {
                throw new ReviewException("Can't start review for own submission");
            }

            if (submission.ReviewState == ReviewStateType.Reviewed)
            {
                throw new ReviewException("Review already finished");
            }

            if (submission.ReviewState == ReviewStateType.Skipped)
            {
                throw new ReviewException("Review is skipped for submission");
            }

            if (submission.ReviewState == ReviewStateType.InProgress)
            {
                if (submission.Reviewer != reviewer)
                {
                    throw new ReviewException("Review is assigned someone else");
                }
            }

            reviewDueDate ??= submission.ReviewDate;
            var dueDate = DateTime.Today.AddDays(2);
            if (reviewDueDate.HasValue && reviewDueDate.Value > dueDate)
            {
                dueDate = reviewDueDate.Value;
            }

            Log.Information("Starte Review für {id}", submission.SubmissionId);
            ProviderStore.FileProvider.SetReviewStateAsStarted(submission, reviewer, dueDate);
        }

        private void SetReviewStateAsReviewd(ISubmission submission, int rating)
        {
            Log.Information("Starte Review für {id}", submission.SubmissionId);
            ProviderStore.FileProvider.SetReviewStateAsReviewed(submission, rating);
        }

        private void SetReviewStateAsSkipped(ISubmission submission)
        {
            Log.Information("Skippe Review für {id}", submission.SubmissionId);
            ProviderStore.FileProvider.SetReviewStateAsSkipped(submission);
        }

        public List<IChallenge> LoadReviewableChallenges()
        {
            return ProviderStore.FileProvider.LoadChallenges().Where(x => x.IsReviewable && x.IsAvailable).ToList();
        }

        public static bool CanReviewMoreReviewsAtCurrentTime(IMember x, Dictionary<string, int> pendingCounts, Dictionary<string, DateTime> reviewDates)
        {
            if (!x.IsReviewer)
            {
                return false;
            }

            if (!reviewDates.TryGetValue(x.Id, out var reviewDate) || !pendingCounts.TryGetValue(x.Id, out var pendingCount))
            {
                if (DateTime.Today >= x.LastReview.AddDays(x.ReviewFrequency))
                {
                    return true;
                }

                return false;
            }

            if (DateTime.Today > reviewDate)
            {
                if (pendingCount < 1)
                {
                    return true;
                }
            }

            return false;
        }

        public static void DeleteAsReviewer(ProviderStore providerStore, IMember member)
        {
            foreach (var submission in providerStore.FileProvider.LoadAllSubmissions())
            {
                if (submission.Reviewer == member.Id)
                {
                    providerStore.FileProvider.UnsetReviewerAndResetIfInProgress(submission);
                }
            }
        }
    }
}
