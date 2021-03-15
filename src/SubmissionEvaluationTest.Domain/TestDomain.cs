using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using NSubstitute;
using NUnit.Framework;
using SubmissionEvaluation.Contracts.Data;
using SubmissionEvaluation.Contracts.Data.Ranklist;
using SubmissionEvaluation.Contracts.Providers;
using SubmissionEvaluation.Domain.Achievements;
using SubmissionEvaluation.Domain.DiffCreators;
using SubmissionEvaluation.Domain.Operations;

namespace SubmissionEvaluationTest.Domain
{
    [TestFixture]
    public class TestDomain
    {
        [Test]
        public void AddAchievementsForSubmitters_Should_Use_Rater_Correctly()
        {
            var achievementRater = Substitute.For<IAchievementRater>();
            achievementRater.WhenForAnyArgs(x => x.AddAwards(null, null)).Do(x => x.Arg<Awards>().AwardWith("1", "first"));

            var domain = new SubmissionEvaluation.Domain.Domain(Substitute.For<IFileProvider>(), Substitute.For<IMemberProvider>(),
                Substitute.For<IProcessProvider>(), Substitute.For<IProcessProvider>(), Substitute.For<IChallengeEstimator>(), Substitute.For<ISmtpProvider>(),
                new List<ICompiler>(), Substitute.For<ILog>(), true, true, true, "localhost", "substituteMail");
            domain.AchievementOperations.AchievementRaters = new List<IAchievementRater> {achievementRater};
            var awards = new Awards();
            var result = domain.AchievementOperations.AddAchievementsForSubmitters(Substitute.For<IFileProvider>(), Substitute.For<IMemberProvider>(), null,
                awards);
            Assert.AreEqual("1", result.First().Key);
            Assert.AreEqual("first", result.First().Value.Single().Id);
        }

        [Test]
        public void BuildGlobalRanklist_Should_Build_Ranking_When_1_Submitter_Is_Passed()
        {
            var list = new ChallengeRanklist();
            list.Submitters.Add(new SubmissionEntry {Id = "User1", Points = 5});

            var ranklists = new List<ChallengeRanklist> {list};

            var memberProvider = Substitute.For<IMemberProvider>();
            memberProvider.GetMemberById(Arg.Any<string>()).Returns(x => new Member {Id = x.Arg<string>(), Name = x.Arg<string>()});
            memberProvider.GetMembers().Returns(x => new List<IMember> {new Member {Id = "User1"}});
            var builder = new StatisticsOperations {MemberProvider = memberProvider, Compilers = new List<ICompiler>()};
            var result = builder.BuildGlobalRanklist(ranklists, new GlobalRanklist());

            Assert.AreEqual("User1", result.Submitters[0].Id);
            Assert.AreEqual(5, result.Submitters[0].Points);
        }

        [Test]
        public void BuildGlobalRanklist_Should_Combine_Submissions_When_Submitter_Has_Several_Submissions()
        {
            var list1 = new ChallengeRanklist();
            list1.Submitters.Add(new SubmissionEntry {Id = "User1", Points = 5});
            list1.Submitters.Add(new SubmissionEntry {Id = "User2", Points = 3});

            var list2 = new ChallengeRanklist();
            list1.Submitters.Add(new SubmissionEntry {Id = "User1", Points = 1});

            var ranklists = new List<ChallengeRanklist> {list1, list2};

            var memberProvider = Substitute.For<IMemberProvider>();
            memberProvider.GetMemberById(Arg.Any<string>()).Returns(x => new Member {Id = x.Arg<string>(), Name = x.Arg<string>()});
            memberProvider.GetMembers().Returns(_ => new List<IMember> {new Member {Id = "User1"}, new Member {Id = "User2"}});
            var builder = new StatisticsOperations {MemberProvider = memberProvider, Compilers = new List<ICompiler>()};
            var result = builder.BuildGlobalRanklist(ranklists, new GlobalRanklist());
            var submitter = result.Submitters.Single(x => x.Id == "User1");

            Assert.AreEqual(6, submitter.Points);
        }

        [Test]
        public void BuildGlobalRanklist_Should_Sort_Correctly_When_Several_Submitters_Are_Passed()
        {
            var list = new ChallengeRanklist();
            list.Submitters.Add(new SubmissionEntry {Id = "User3", Points = 1});
            list.Submitters.Add(new SubmissionEntry {Id = "User2", Points = 6});
            list.Submitters.Add(new SubmissionEntry {Id = "User1", Points = 9});

            var ranklists = new List<ChallengeRanklist> {list};

            var memberProvider = Substitute.For<IMemberProvider>();
            memberProvider.GetMemberById(Arg.Any<string>()).Returns(x => new Member {Id = x.Arg<string>(), Name = x.Arg<string>()});
            memberProvider.GetMembers().Returns(_ => new List<IMember> {new Member {Id = "User1"}, new Member {Id = "User2"}, new Member {Id = "User3"}});
            var builder = new StatisticsOperations {MemberProvider = memberProvider, Compilers = new List<ICompiler>()};
            var result = builder.BuildGlobalRanklist(ranklists, new GlobalRanklist());

            Assert.AreEqual("User1", result.Submitters[0].Id);
            Assert.AreEqual("User2", result.Submitters[1].Id);
            Assert.AreEqual("User3", result.Submitters[2].Id);
        }

        [Test]
        public void BuildRanklistFromSubmissions_Should_Build_Ranking_When_1_Submission_Is_Passed()
        {
            var ranklists = new List<Result>
            {
                new Result
                {
                    MemberId = "User1",
                    ExecutionDuration = 1337,
                    Language = "Java",
                    SubmissionDate = new DateTime(2015, 1, 1),
                    EvaluationResult = EvaluationResult.Succeeded
                }
            };
            var rater = Substitute.For<ISubmissionRater>();
            rater.WhenForAnyArgs(x => x.UpdatePoints(Arg.Any<List<SubmissionEntry>>(), Arg.Any<RatingPoints>()))
                .Do(x => x.Arg<List<SubmissionEntry>>().ForEach(y => y.Points = 5));
            rater.WhenForAnyArgs(x => x.UpdateRanking(Arg.Any<List<SubmissionEntry>>())).Do(x => x.Arg<List<SubmissionEntry>>().ForEach(y => y.Rank = 1));

            var builder = new StatisticsOperations();
            var result = builder.BuildRanklistFromSubmissions(new Challenge {Id = ""}, ranklists, rater, new RatingPoints(1, 1, 1));
            var submitter = result.Submitters[0];
            Assert.AreEqual("User1", submitter.Id);
            Assert.AreEqual(5, submitter.Points);
            Assert.AreEqual(new DateTime(2015, 1, 1), submitter.Date);
            Assert.AreEqual("Java", submitter.Language);
            Assert.AreEqual(1337, submitter.Exectime);
            Assert.AreEqual(1, submitter.Rank);
        }

        [Test]
        public void BuildRanklistFromSubmissions_Should_Count_Submissions_Correctly_When_Failed_Submissions_Are_Passed()
        {
            IList<Result> ranklists = new List<Result>
            {
                new Result {MemberId = "User1", ExecutionDuration = 2, EvaluationResult = EvaluationResult.Succeeded, Language = "Java"},
                new Result {MemberId = "User1", ExecutionDuration = 1, EvaluationResult = EvaluationResult.Succeeded, Language = "Java"},
                new Result {MemberId = "User2", ExecutionDuration = 10, EvaluationResult = EvaluationResult.Succeeded, Language = "Java"}
            };
            var rater = Substitute.For<ISubmissionRater>();
            rater.WhenForAnyArgs(x => x.UpdateRanking(Arg.Any<List<SubmissionEntry>>())).Do(x =>
                x.Arg<List<SubmissionEntry>>().ForEach(y => y.Rank = y.Exectime));

            var builder = new StatisticsOperations();
            var result = builder.BuildRanklistFromSubmissions(new Challenge {Id = ""}, ranklists, rater, new RatingPoints(1, 1, 1));
            Assert.AreEqual(2, result.Submitters[0].SubmissionCount);
        }

        [Test]
        public void BuildRanklistFromSumbittions_Should_Sort_Correctly_When_2_Submittions_Are_Passed()
        {
            var ranklists = new List<Result>
            {
                new Result {MemberId = "User1", ExecutionDuration = 2, EvaluationResult = EvaluationResult.Succeeded, Language = "Java"},
                new Result {MemberId = "User2", ExecutionDuration = 1, EvaluationResult = EvaluationResult.Succeeded, Language = "Java"}
            };
            var rater = Substitute.For<ISubmissionRater>();
            rater.WhenForAnyArgs(x => x.UpdateRanking(Arg.Any<List<SubmissionEntry>>())).Do(x =>
                x.Arg<List<SubmissionEntry>>().ForEach(y => y.Rank = y.Exectime));

            var builder = new StatisticsOperations();
            var result = builder.BuildRanklistFromSubmissions(new Challenge {Id = ""}, ranklists, rater, new RatingPoints(1, 1, 1));
            Assert.AreEqual("User2", result.Submitters[0].Id);
            Assert.AreEqual("User1", result.Submitters[1].Id);
        }

        [Test]
        public void BuildRanklistFromSumbittions_Should_Take_Better_When_2_Submittions_Are_Passed()
        {
            var ranklists = new List<Result>
            {
                new Result {MemberId = "User1", ExecutionDuration = 2, EvaluationResult = EvaluationResult.Succeeded, Language = "Java"},
                new Result {MemberId = "User1", ExecutionDuration = 1, EvaluationResult = EvaluationResult.Succeeded, Language = "Java"}
            };
            var rater = Substitute.For<ISubmissionRater>();
            rater.Compare(Arg.Any<SubmissionEntry>(), Arg.Any<SubmissionEntry>()).Returns(x =>
                x.ArgAt<SubmissionEntry>(1).Exectime - x.ArgAt<SubmissionEntry>(0).Exectime);

            var builder = new StatisticsOperations();
            var result = builder.BuildRanklistFromSubmissions(new Challenge {Id = ""}, ranklists, rater, new RatingPoints(1, 1, 1));
            Assert.AreEqual(1, result.Submitters[0].Exectime);
        }

        [Test]
        public void BuildSubmitterHistory_Should_Add_Submitter_From_Single_Result()
        {
            var builder = new StatisticsOperations();
            var result = builder.BuildSubmitterHistory(new Member {Id = "123"},
                new List<Result> {new Result {MemberId = "123", SubmissionDate = DateTime.Now, Language = "Java"}});
            Assert.AreEqual("Java", result.Entries[0].Language);
        }

        [Test]
        public void BuildSubmitterHistory_Should_Add_Two_Submitters_From_Single_Results()
        {
            var builder = new StatisticsOperations();
            var result = builder.BuildSubmitterHistory(new Member {Id = "123"},
                new List<Result>
                {
                    new Result {MemberId = "123", SubmissionDate = DateTime.Now, Language = "C#"},
                    new Result {MemberId = "123", SubmissionDate = DateTime.Now.AddDays(1), Language = "Java"}
                });
            Assert.AreEqual("Java", result.Entries[0].Language);
            Assert.AreEqual("C#", result.Entries[1].Language);
        }

        [Test]
        public void BuildSubmitterRanklist_Should_Build_Correct_List()
        {
            var builder = new StatisticsOperations();
            var result = builder.BuildSubmitterRanklist(new List<ChallengeRanklist>
            {
                new ChallengeRanklist {Challenge = "Challenge1", Submitters = new List<SubmissionEntry> {new SubmissionEntry {Id = "123", Points = 42}}}
            });
            Assert.AreEqual("123", result[0].Name);
            Assert.AreEqual("Challenge1", result[0].Submissions.Single().Challenge);
            Assert.AreEqual(42, result[0].Submissions.Single().Points);
        }

        [Test]
        public void GetDifference_Should_Return_Added_Lines_When_Modified_Strings_Are_Passed()
        {
            var text1 = string.Format("Hallo Welt!{0}Noch mal Hallo!{0}Und noch mal", Environment.NewLine);
            var text2 = string.Format("Hallo Welt!{0}Noch mal Hallo!{0}", Environment.NewLine);
            var (_, details, _) = new ExactDiffCreator(TrimMode.StartEnd, false, true, false, WhitespacesMode.LeaveAsIs, false).GetDiff(text1, text2);
            Assert.That(details.Contains("- Und&middot;noch&middot;mal&para;"));
        }

        [Test]
        public void GetDifference_Should_Return_Diff_When_Modified_Strings_Are_Passed()
        {
            var text1 = string.Format("Hallo Welt!{0}Noch mal Hallo!{0}", Environment.NewLine);
            var text2 = string.Format("Hallo World!{0}Noch mal Hallo!", Environment.NewLine);
            var (_, details, _) = new ExactDiffCreator(TrimMode.StartEnd, false, true, false, WhitespacesMode.LeaveAsIs, false).GetDiff(text1, text2);
            var lines = Regex.Split(details, "\r\n|\r|\n");

            Assert.That(lines[0].Contains("- Hallo&middot;Welt!&para;"));
            Assert.That(lines[1].Contains("+ Hallo&middot;World!&para;"));
        }

        [Test]
        public void GetDifference_Should_Return_Empty_String_When_Same_Strings_Are_Passed()
        {
            var text1 = string.Format("Hallo Welt!{0}Noch mal Hallo!{0}", Environment.NewLine);
            var text2 = string.Format("Hallo Welt!{0}Noch mal Hallo!", Environment.NewLine);
            var (_, details, _) = new ExactDiffCreator(TrimMode.StartEnd, true, true, false, WhitespacesMode.LeaveAsIs, false).GetDiff(text1, text2);
            Assert.AreEqual(string.Empty, details);
        }

        [Test]
        public void GetDifference_Should_Return_Empty_String_When_Same_Strings_Are_Passed_With_Different_Case()
        {
            var text1 = string.Format("HALLO WELT!{0}noch mal hallo!{0}", Environment.NewLine);
            var text2 = string.Format("hallo welt!{0}NOCH MAL HALLO!{0}", Environment.NewLine);
            var (_, details, _) = new ExactDiffCreator(TrimMode.StartEnd, false, true, false, WhitespacesMode.LeaveAsIs, false).GetDiff(text1, text2);
            Assert.AreEqual(string.Empty, details);
        }

        [Test]
        public void GetDifference_Should_Return_Missing_Lines_When_Modified_Strings_Are_Passed()
        {
            var text1 = string.Format("Hallo Welt!{0}Noch mal Hallo!{0}", Environment.NewLine);
            var text2 = string.Format("Hallo Welt!{0}Noch mal Hallo!{0}Und noch mal", Environment.NewLine);
            var (_, details, _) = new ExactDiffCreator(TrimMode.StartEnd, false, true, false, WhitespacesMode.LeaveAsIs, false).GetDiff(text1, text2);
            var lines = Regex.Split(details, "\r\n|\r|\n");
            Assert.That(lines[2].Contains("+ Und&middot;noch&middot;mal&para;"));
        }
    }
}
