using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using SubmissionEvaluation.Contracts.Data;
using SubmissionEvaluation.Domain.Operations;

namespace SubmissionEvaluationTest.Domain.Operations
{
    [TestFixture]
    public class ChallengeOperationTests
    {
        /*
         * Setup TestFileProvider, which is a mock-up.
         */
        [SetUp]
        public void Setup()
        {
            provider = new TestFileProvider();
        }

        private TestFileProvider provider;

        /**
         * * Calls ChallengeOperations.AllChallenges with
         * * Member: Null
         * * FileProvider: TestFileProvider
         * * ContainUnavailable: true
         * * Return should be an empty list.
         */
        [Test]
        public void MemberIsNull_ReturnIsEmptyList()
        {
            var challenges = ChallengeOperations.GetAllChallengesForMember(null, provider, true);
            Assert.AreEqual(0, challenges.Count);
        }

        /**
         * Calls ChallengeOperations.AllChallenges with 
         * Member: Admin -> getById("admin")
         * FileProvider: TestFileProvider
         * ContainUnavailable: true
         * Return should be TestFileProvider.AllChallenges
         */
        [Test]
        public void MemberIsAdmin_ReturnIsAllChallenges()
        {
            var member = provider.LoadMember("admin");
            var challenges = ChallengeOperations.GetAllChallengesForMember(member, provider, true);
            CollectionAssert.AreEquivalent(provider.LoadChallenges(), challenges);
        }

        /**
         * Calls ChallengeOperations.AllChallenges with
         * Member: CreatorNoGroup -> getById("creatorNoGroup")
         * FileProvider: TestFileProvider
         * ContainUnavailable: true
         * Return should be all challenges created by this guy
         */
        [Test]
        public void MemberIsCreator_ReturnIsHisChallenges()
        {
            var member = provider.LoadMember("creatorNoGroup");
            var challenges = ChallengeOperations.GetAllChallengesForMember(member, provider, true);
            CollectionAssert.AreEquivalent(provider.LoadChallenges().Where(x => x.AuthorID.Equals(member.Id)), challenges);
        }

        /**
         * Calls ChallengeOperations.AllChallenges with
         * Member: GroupAdminOneGroup -> getById("groupAdminOneGroup")
         * FileProvider: TestFileProvider
         * ContainUnavailable: true
         * Return should be all challenges contained in his group
         */
        /*[Test]
        public void MemberIsGroupAdminOfOneGroup_ReturnIsHisGroupsChallenges()
        {
            var member = provider.LoadMember("groupAdminOneGroup");
            var challenges = ChallengeOperations.GetAllChallengesForMember(member, provider, true);
            var groups = provider.LoadAllGroups().Where(x => (x.GroupAdminIds ?? new List<string>()).Contains(member.Id));
            var allChallenges = provider.LoadChallenges();
            var groupChallenges = groups.SelectMany(x => x.AvailableChallenges).ToList();
            groupChallenges.AddRange(groups.SelectMany(x => x.ForcedChallenges));
            CollectionAssert.AreEquivalent(allChallenges.Where(x => groupChallenges.Any(y => x.Id.Equals(y))).ToList(), challenges);
        }*/

        /**
         * Calls ChallengeOperations.AllChallenges with
         * Member: GroupAdminTwoGroups -> getById("groupAdminTwoGroups")
         * FileProvider: TestFileProvider
         * ContainUnavailable: true
         * Groups: Should be two groups, that have some challenges existing in both.
         * List should return distinct challenges.
         */
        /*[Test]
        public void MemberIsGroupAdminOfTwoGroups_ReturnIsHisGroupsDistinctChallenges()
        {
            var member = provider.LoadMember("groupAdminTwoGroups");
            var challenges = ChallengeOperations.GetAllChallengesForMember(member, provider, true);
            var groups = provider.LoadAllGroups().Where(x => (x.GroupAdminIds ?? new List<string>()).Contains(member.Id));
            var allChallenges = provider.LoadChallenges();
            var groupChallenges = groups.SelectMany(x => x.AvailableChallenges).ToList();
            groupChallenges.AddRange(groups.SelectMany(x => x.ForcedChallenges));
            CollectionAssert.AreEquivalent(allChallenges.Where(x => groupChallenges.Any(y => x.Id.Equals(y))).ToList(), challenges);
        }*/

        /**
         * Calls ChallengeOperations.AllChallenges with
         * Member: GroupAdminOfNoGroup -> getById("groupAdminOfNoGroup")
         * FileProvider: TestFileProvider
         * ContainUnavailable: true
         * Groups: One group that has some challenges
         * List should contain all challenges
         */
        [Test]
        public void MemberIsGroupAdminButNotOfAnyGroup_ReturnIsEmpty()
        {
            var member = provider.LoadMember("groupAdminOfNoGroup");
            var challenges = ChallengeOperations.GetAllChallengesForMember(member, provider, true);
            var group = provider.LoadGroup(member.Groups.First());
            var groupChallenge = provider.LoadChallenges();
            CollectionAssert.AreEquivalent(groupChallenge, challenges);
        }

        /**
         * Calls ChallengeOperations.AllChallenges with
         * Member: MemberWithoutGroup -> getById("memberWithoutGroup") -> has a few solved challenges and a few unlocked challenges 
         * FileProvider: TestFileProvider
         * ContainUnavailable: true
         * Return should be all challenges solved and unlocked
         */
        [Test]
        public void MemberHasSolvedAndUnlocked_ReturnIsHisSolvedAndUnlocked()
        {
            var member = provider.LoadMember("memberWithoutGroup");
            var challenges = ChallengeOperations.GetAllChallengesForMember(member, provider, true);
            var allChallenges = provider.LoadChallenges();
            var memberChallenges = member.UnlockedChallenges.ToList();
            memberChallenges.AddRange(member.SolvedChallenges);
            CollectionAssert.AreEquivalent(allChallenges.Where(x => memberChallenges.Any(y => x.Id.Equals(y))).ToList(), challenges);
        }

        /**
         * Calls ChallengeOperations.AllChallenges with
         * Member: MemberWithForcedGroup -> getById("memberWithForcedGroup")
         * -> is in 1 group
         * -> has no solved or unlocked challenges
         * -> group has few forcedChallenges
         * FileProvider: TestFileProvider
         * ContainUnavailable: true
         * Return should be the first forcedChallenge
         */
        [Test]
        public void MemberIsInGroup_ReturnIsFirstForced()
        {
            var member = provider.LoadMember("memberWithForcedGroup");
            var challenges = ChallengeOperations.GetAllChallengesForMember(member, provider, true);
            var allChallenges = provider.LoadChallenges();
            var groups = member.Groups.Select(x => provider.LoadGroup(x));
            var expectedChallenge = groups.FirstOrDefault().ForcedChallenges[0];
            CollectionAssert.AreEquivalent(allChallenges.Where(x => x.Id.Equals(expectedChallenge)), challenges);
        }

        /**
         * Calls ChallengeOperations.AllChallenges with
         * Member: MemberWithTwoForcedGroups -> getById("memberWithTwoForcedGroups")
         * -> is in 2 groups
         * -> has no solved or unlocked challenges
         * -> both groups have few forcedChallenges
         * FileProvider: TestFileProvider
         * ContainUnavailable: true
         * Return should be the first forcedChallenge of both groups
         */
        [Test]
        public void MemberIsInTwoGroups_ReturnIsBothFirstForced()
        {
            var member = provider.LoadMember("memberWithTwoForcedGroups");
            var challenges = ChallengeOperations.GetAllChallengesForMember(member, provider, true);
            var allChallenges = provider.LoadChallenges();
            var groups = member.Groups.Select(x => provider.LoadGroup(x));
            var expectedChallenges = groups.Select(x => x.ForcedChallenges[0]);
            CollectionAssert.AreEquivalent(allChallenges.Where(x => expectedChallenges.Any(y => x.Id.Equals(y))).ToList(), challenges);
        }

        /**
         * Calls ChallengeOperations.AllChallenges with
         * Member: MemberWithForcedGroupForcedSolved -> getById("memberWithForcedGroupForcedSolved")
         * -> is in 1 group
         * -> has solved all forcedChallenges
         * -> group has no maxAvailable
         * FileProvider: TestFileProvider
         * ContainUnavailable: true
         * Return should be allAvailableChallenges and the solved (so all challenges attached to this group)
         */
        [Test]
        public void MemberIsInGroupForcedSolvedNoMax_ReturnIsAllAvailableAndSolved()
        {
            var member = provider.LoadMember("memberWithForcedGroupForcedSolved");
            var challenges = ChallengeOperations.GetAllChallengesForMember(member, provider, true);
            var allChallenges = provider.LoadChallenges();
            var groups = member.Groups.Select(x => provider.LoadGroup(x));
            var expectedChallenges = groups.FirstOrDefault().AvailableChallenges.ToList();
            expectedChallenges.AddRange(member.SolvedChallenges);
            CollectionAssert.AreEquivalent(allChallenges.Where(x => expectedChallenges.Any(y => x.Id.Equals(y))).ToList(), challenges);
        }

        /**
         * Calls ChallengeOperations.AllChallenges with
         * Member: MemberWithGroupNoForcedMax3 -> getById("memberWithGroupNoForcedMax3")
         * -> is in 1 group
         * -> has solved no challenges
         * -> group has 3 maxAvailable
         * FileProvider: TestFileProvider
         * ContainUnavailable: true
         * Return should be 3 AvailableChallenges
         */
        [Test]
        public void MemberIsInGroupWithoutForcedMax3_ReturnIs3OfAvailable()
        {
            var member = provider.LoadMember("memberWithGroupNoForcedMax3");
            var group = provider.LoadGroup(member.Groups.FirstOrDefault());
            var challenges = ChallengeOperations.GetAllChallengesForMember(member, provider, true);
            Assert.True(challenges.Count() == 3);
            var intersection = group.AvailableChallenges.Intersect(challenges.Select(x => x.Id));
            Assert.True(intersection.Count() == 3);
        }

        /**
         * Calls ChallengeOperations.AllChallenges with
         * Member: MemberWithGroupNoForcedMax3DiffTooLow -> getById("memberWithGroupNoForcedMax3DiffTooLow")
         * -> is in 1 group
         * -> has solved no challenges
         * -> group has 3 maxAvailable
         * -> member has averageDifficulty of 10
         * -> most challenges (except 2) have difficulty below 9
         * FileProvider: TestFileProvider
         * ContainUnavailable: true
         * Return should be the two challenges difficult enough
         */
        [Test]
        public void MemberIsInGroupWithoutForcedMax3AverageDiffMostlyTooLow_ReturnIs2DifficultEnough()
        {
            var member = provider.LoadMember("memberWithGroupNoForcedMax3DiffTooLow");
            var group = provider.LoadGroup(member.Groups.FirstOrDefault());
            var expected = group.AvailableChallenges.Skip(3).Take(2);
            var allChallenges = provider.LoadChallenges();
            var challenges = ChallengeOperations.GetAllChallengesForMember(member, provider, true);
            CollectionAssert.AreEquivalent(allChallenges.Where(x => expected.Any(y => x.Id.Equals(y))).ToList(), challenges);
        }

        /**
         * Calls ChallengeOperations.AllChallenges with
         * Member: MemberWithBundleGroupNoMax -> getById("memberWithBundleGroupNoMax")
         * -> is in 1 group
         * -> has solved no challenges
         * -> group has no maxAvailable
         * -> group has one bundle
         * -> group has no forced
         * FileProvider: TestFileProvider
         * ContainUnavailable: true
         * Return should be the first bundle challenge
         */
        [Test]
        public void MemberIsInGroupWithOneBundleNoMax_ReturnIsFirstChallengeOfBundle()
        {
            var member = provider.LoadMember("memberWithBundleGroupNoMax");
            var group = provider.LoadGroup(member.Groups.FirstOrDefault());
            var expected = group.AvailableChallenges[0];
            var challenges = ChallengeOperations.GetAllChallengesForMember(member, provider, true);
            var allChallenges = provider.LoadChallenges();
            CollectionAssert.AreEquivalent(allChallenges.Where(x => x.Id.Equals(expected)).ToList(), challenges);
        }

        /**
         * Calls ChallengeOperations.AllChallenges with
         * Member: MemberWithBundleGroupOneOtherNoMax -> getById("memberWithBundleGroupOneOtherNoMax")
         * -> is in 1 group
         * -> has solved no challenges
         * -> group has no maxAvailable
         * -> group has one bundle
         * -> group has one other challenge
         * -> group has no forced
         * FileProvider: TestFileProvider
         * ContainUnavailable: true
         * Return should be the first bundle challenge and the other challenge
         */
        [Test]
        public void MemberIsInGroupWithOneBundleAndOneOtherChallengeNoMax_ReturnIsFirstChallengeOfBundleAndOtherChallenge()
        {
            var member = provider.LoadMember("memberWithBundleGroupOneOtherNoMax");
            var group = provider.LoadGroup(member.Groups.FirstOrDefault());
            var expected = group.AvailableChallenges.Take(2);
            var allChallenges = provider.LoadChallenges();
            var challenges = ChallengeOperations.GetAllChallengesForMember(member, provider, true);
            CollectionAssert.AreEquivalent(allChallenges.Where(x => expected.Any(y => x.Id.Equals(y))).ToList(), challenges);
        }

        /**
         * Calls ChallengeOperations.AllChallenges with
         * Member: MemberWithBundleGroup3Max -> getById("memberWithBundleGroup3Max")
         * -> is in 1 group
         * -> has solved first bundle challenge
         * -> group has 3 maxAvailable
         * -> group has one bundle
         * -> group has no forced
         * FileProvider: TestFileProvider
         * ContainUnavailable: true
         * Return should be the 1st(solved) & 2nd bundle challenge
         */
        [Test]
        public void MemberIsInGroupWithOneBundleFirstBundleChallengeSolvedMax3_ReturnIsSecondBundleChallengeAndSolved()
        {
            var member = provider.LoadMember("memberWithBundleGroup3Max");
            var group = provider.LoadGroup(member.Groups.FirstOrDefault());
            var challenges = ChallengeOperations.GetAllChallengesForMember(member, provider, true);
            var expectedChallenges = group.AvailableChallenges.Take(2);
            var allChallenges = provider.LoadChallenges();
            CollectionAssert.AreEquivalent(allChallenges.Where(x => expectedChallenges.Any(y => x.Id.Equals(y))).ToList(), challenges);
        }

        /**
         * Calls ChallengeOperations.AllChallenges with
         * Member: MemberWithBundleGroupNoMax -> getById("memberWithBundleGroupNoMaxFirstSolved")
         * -> is in 1 group
         * -> has solved first bundle challenge
         * -> group has no maxAvailable
         * -> group has one bundle
         * -> group has no forced
         * FileProvider: TestFileProvider
         * ContainUnavailable: true
         * Return should be the 1st (solved) & 2nd bundle challenge
         */
        [Test]
        public void MemberIsInGroupWithOneBundleFirstBundleChallengeSolvedNoMax_ReturnIsSecondBundleChallengeAndSolved()
        {
            var member = provider.LoadMember("memberWithBundleGroupNoMaxFirstSolved");
            var group = provider.LoadGroup(member.Groups.FirstOrDefault());
            var challenges = ChallengeOperations.GetAllChallengesForMember(member, provider, true);
            var expectedChallenges = group.AvailableChallenges.Take(2);
            var allChallenges = provider.LoadChallenges();
            CollectionAssert.AreEquivalent(allChallenges.Where(x => expectedChallenges.Any(y => x.Id.Equals(y))).ToList(), challenges);
        }

        /**
         * Calls ChallengeOperations.AllChallenges with
         * Member: MemberNotThatExperiencedTooHardGroups-> getById("memberNotThatExperiencedTooHardGroups")
         * -> is in 2 groups
         * -> group has 2 maxAvailable
         * -> groups have a lot of hard challenges and only few easy
         * -> groups have no forced
         * FileProvider: TestFileProvider
         * ContainUnavailable: true
         * Return should be four challenges, at least one should be easy
         */
        [Test]
        public void MemberIsInTwoHardGroups_ReturnIsAtLeastOneEasy()
        {
            var member = provider.LoadMember("memberNotThatExperiencedTooHardGroups");
            var challenges = ChallengeOperations.GetAllChallengesForMember(member, provider, true);
            Assert.AreEqual(4, challenges.Count());
            Assert.IsTrue(challenges.Any(x => x.State.DifficultyRating < 20));
        }
    }
}
