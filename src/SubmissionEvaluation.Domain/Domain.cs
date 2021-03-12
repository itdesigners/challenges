using System.Collections.Generic;
using System.IO;
using System.Linq;
using SubmissionEvaluation.Contracts.Data;
using SubmissionEvaluation.Contracts.Providers;
using SubmissionEvaluation.Domain.Achievements;
using SubmissionEvaluation.Domain.Operations;
using SubmissionEvaluation.Domain.RatingMethods;

namespace SubmissionEvaluation.Domain
{
    public class Domain
    {
        public Domain(IFileProvider fileProvider, IMemberProvider memberProvider, IProcessProvider processProvider, IProcessProvider sandboxedProcessProvider,
            IChallengeEstimator challengeEstimator, ISmtpProvider smtpProvider, List<ICompiler> compilers, ILog logger,
            bool enableReview, bool enableReviewerPromotion, bool enableAchievements, string siteUrl, string helpEmail)
        {
            Log = logger;
            MemberProvider = memberProvider;
            ChallengeEstimator = challengeEstimator;
            Compilers = compilers;

            ProviderStore = new ProviderStore
            {
                FileProvider = fileProvider,
                ProcessProvider = processProvider,
                SandboxedProcessProvider = sandboxedProcessProvider,
                Log = logger,
                MemberProvider = memberProvider,
                SiteUrl = siteUrl,
                HelpEmail = helpEmail
            };

            Query = new Query(this);
            Interactions = new Interactions(this, enableAchievements);

            Interactions.InitialSetup();

            var submissionRaters = new List<ISubmissionRater> {new RateExecTime(), new RateFixed(), new RateSubmissionTime(), new RateScore()};

            CompilerOperations = new CompilerOperations {ProviderStore = ProviderStore, Compilers = Compilers};

            EvaluationOperations = new EvaluationOperations
            {
                SmtpProvider = smtpProvider,
                ProviderStore = ProviderStore,
                MemberProvider = memberProvider,
                SandboxedProcessProvider = sandboxedProcessProvider,
                Log = logger,
                CompilerOperations = CompilerOperations
            };

            ReviewOperations = new ReviewOperations(enableReview, enableReviewerPromotion)
            {
                ProviderStore = ProviderStore,
                Log = logger,
                MemberProvider = memberProvider,
                SmtpProvider = smtpProvider,
                Compilers = compilers,
                EvaluationOperations = EvaluationOperations,
            };

            StatisticsOperations = new StatisticsOperations
            {
                ProviderStore = ProviderStore,
                Log = logger,
                MemberProvider = memberProvider,
                Raters = submissionRaters,
                Compilers = compilers,
                ChallengeEstimator = challengeEstimator
            };

            Maintenance = new MaintenanceOperations {ProviderStore = ProviderStore, Log = logger, MemberProvider = memberProvider, SmtpProvider = smtpProvider};

            AchievementOperations = new AchievementOperations
            {
                AchievementRaters =
                    new List<IAchievementRater>
                    {
                        new ContributorAchievementRater(),
                        new ChallengeAuthorAchievementRater(),
                        new SubmitterAchievementRater(),
                        new ReviewAchievementRater()
                    },
                Contributors = new List<string>(),
                StatisticsOperations = StatisticsOperations
            };
            StatisticsOperations.AchievementOperations = AchievementOperations;
            Achievements = AchievementOperations.GetAllAchievements().OrderBy(x => x.Quality).ToList();

            logger.ActivityAdded += Interactions.AddActivityEntry;
        }

        internal AchievementOperations AchievementOperations { get; }
        internal MaintenanceOperations Maintenance { get; }
        internal ProviderStore ProviderStore { get; }
        internal ILog Log { get; }
        internal IMemberProvider MemberProvider { get; }
        internal ReviewOperations ReviewOperations { get; }
        internal StatisticsOperations StatisticsOperations { get; }
        internal EvaluationOperations EvaluationOperations { get; }
        internal CompilerOperations CompilerOperations { get; }
        internal IChallengeEstimator ChallengeEstimator { get; }
        public List<ICompiler> Compilers { get; set; }
        public List<Achievement> Achievements { get; set; }
        public Query Query { get; }
        public Interactions Interactions { get; }

        public bool IsMaintenanceMode
        {
            get => ProviderStore.FileProvider.IsMaintenanceMode;
            internal set => ProviderStore.FileProvider.IsMaintenanceMode = value;
        }

        public static string GetVersionHash()
        {
            return File.Exists("version.txt") ? File.ReadAllText("version.txt") : "";
        }
    }
}
