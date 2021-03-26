using System;
using System.Collections.Generic;
using System.Linq;
using SubmissionEvaluation.Contracts.Data;
using SubmissionEvaluation.Contracts.Data.Ranklist;
using SubmissionEvaluation.Contracts.Providers;
using SubmissionEvaluation.Domain.Achievements;

namespace SubmissionEvaluation.Domain.Operations
{
    internal class AchievementOperations
    {
        public List<IAchievementRater> AchievementRaters { get; set; }
        public List<string> Contributors { private get; set; }
        internal StatisticsOperations StatisticsOperations { get; set; }

        private IEnumerable<Achievement> ListOfAchievements { get { return AchievementRaters.SelectMany(p => p.ListOfAchievements); } }

        public ChallengeRanklist BuildAchievementsRanklist(IFileProvider fileProvider)
        {
            var awards = fileProvider.LoadAwards();
            var ranklist = new ChallengeRanklist {Challenge = "Achievements"};
            foreach (var user in awards)
            {
                foreach (var achievementId in user.Value)
                {
                    var achievement = ListOfAchievements.SingleOrDefault(x => x.Id == achievementId.Id);
                    if (achievement != null)
                    {
                        var points = (int) Math.Pow(2, (int) achievement.Quality) * 2;
                        ranklist.Submitters.Add(new SubmissionEntry
                        {
                            Id = user.Key,
                            Points = points,
                            Language = achievementId.Id,
                            Rank = -1,
                            Date = achievementId.Date
                        });
                    }
                }
            }

            return ranklist;
        }

        public Awards AddAchievementsForSubmitters(IFileProvider fileProvider, IMemberProvider memberProvider, IEnumerable<string> compilerNames, Awards awards)
        {
            var challenges = ChallengeOperations.LoadAllChallengeProperties(fileProvider);
            var challengeAuthors = challenges.Where(x => x.IsAvailable).Select(x => memberProvider.GetMemberById(x.AuthorID)).Where(x => x != null)
                .Select(x => x.Id).ToList();

            var challengeRanklists = StatisticsOperations.GenerateAllChallengeRanklists(false);
            var submitterRanklists = StatisticsOperations.BuildSubmitterRanklist(challengeRanklists);

            var conditions = new AchievementConditions
            {
                GlobalRanklist = fileProvider.LoadGlobalRanklist(),
                SemesterRanklists = fileProvider.LoadAllSemesterRanklists(),
                Rankings = submitterRanklists,
                Histories = StatisticsOperations.BuildSubmitterHistories(),
                Contributors = Contributors,
                Challenges = challenges,
                ChallengeAuthors = challengeAuthors,
                ChallengeRanklists = challengeRanklists,
                Members = memberProvider.GetMembers(),
                CompilerNames = compilerNames
            };
            AchievementRaters.ForEach(x => x.AddAwards(awards, conditions));
            return awards;
        }

        public void DeleteAchievementForMember(ProviderStore providerStore, IMember member)
        {
            using var writeLock = providerStore.FileProvider.GetLock();
            var awards = providerStore.FileProvider.LoadAwards(writeLock);
            awards.RemoveAwardsFor(member.Id);
            providerStore.FileProvider.SaveAwards(awards, writeLock);
        }

        public IEnumerable<Achievement> GetAllAchievements()
        {
            return AchievementRaters.SelectMany(x => x.ListOfAchievements);
        }
    }
}
