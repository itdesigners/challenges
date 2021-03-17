using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using SubmissionEvaluation.Contracts.Data;
using SubmissionEvaluation.Contracts.Data.Ranklist;
using SubmissionEvaluation.Contracts.Data.Review;
using SubmissionEvaluation.Contracts.Providers;

namespace SubmissionEvaluationTest.Domain.Operations
{
    internal class TestFileProvider : IFileProvider
    {
        private static readonly List<IMember> members = new List<IMember>();
        private static readonly List<IChallenge> challenges = new List<IChallenge>();
        private static readonly List<IBundle> bundles = new List<IBundle>();
        private static readonly List<IGroup> groups = new List<IGroup>();

        static TestFileProvider()
        {
            InitializeMemberFundament();
            InitializeChallenges();
            InitializeGroups();
            InitializeLeftMembers();
        }

        public string GetPathToChallengeProperties(string challenge)
        {
            throw new NotImplementedException();
        }

        public string GetSourceZipPathForSubmission(ISubmission submission)
        {
            throw new NotImplementedException();
        }

        public IEnumerable<string> GetSubmissionFilesPath(ISubmission submission)
        {
            throw new NotImplementedException();
        }

        public string GetTournamentSubmissionPath(IMember member, string tournament)
        {
            throw new NotImplementedException();
        }

        public string GetSourceZipPathForTournamentEntry(TournamentEntry tournamentEntry)
        {
            throw new NotImplementedException();
        }

        public string GetPathToGame(string game)
        {
            throw new NotImplementedException();
        }

        public string GetBuildPathForSubmissionSource(string pathToContent)
        {
            throw new NotImplementedException();
        }

        public string ExtractContent(string pathToZip, bool forceClean = true)
        {
            throw new NotImplementedException();
        }

        public IEnumerable<string> GetAllEntriesForTournament(string tournament)
        {
            throw new NotImplementedException();
        }

        public string GetPathToTournamentEntry(string tournament, string id)
        {
            throw new NotImplementedException();
        }

        public string GetExtractPathForZip(TournamentEntry author)
        {
            throw new NotImplementedException();
        }

        public MemoryStream CreateZipInMemory((string, string)[] paths, (string, byte[])[] additionalFiles = null)
        {
            throw new NotImplementedException();
        }

        public IEnumerable<string> GetFilenamesInsideZip(byte[] data)
        {
            throw new NotImplementedException();
        }

        public T DeserializeFromText<T>(string text, HandleMode mode = HandleMode.ThrowException) where T : class, new()
        {
            throw new NotImplementedException();
        }

        public byte[] BuildZipFor(IEnumerable<(string filename, byte[] data)> files)
        {
            throw new NotImplementedException();
        }

        public void AddFileToZip(MemoryStream zipStream, byte[] fileToAdd, string pathInZip)
        {
            throw new NotImplementedException();
        }

        public IWriteLock GetLock()
        {
            throw new NotImplementedException();
        }

        public void DeleteAllSubmissions()
        {
            throw new NotImplementedException();
        }

        public void DeleteAllStatistics()
        {
            throw new NotImplementedException();
        }

        public void CreateMissingDirectories()
        {
            throw new NotImplementedException();
        }

        public IEnumerable<Member> LoadMembers()
        {
            return members.Select(x => (Member) x);
        }

        public Member LoadMember(string id, IWriteLock writeLock = null)
        {
            return LoadMembers().First(x => x.Id.Equals(id));
        }

        public void SaveMember(Member member, IWriteLock writeLock)
        {
            throw new NotImplementedException();
        }

        public void CreateMember(Member member)
        {
            throw new NotImplementedException();
        }

        public void DeleteMember(IMember member, IWriteLock writeLock)
        {
            throw new NotImplementedException();
        }

        public List<Activity> LoadRecentActivities(IWriteLock writeLock = null)
        {
            throw new NotImplementedException();
        }

        public GlobalRanklist LoadGlobalRanklist(IWriteLock writeLock = null)
        {
            throw new NotImplementedException();
        }

        public IEnumerable<GlobalRanklist> LoadAllSemesterRanklists()
        {
            throw new NotImplementedException();
        }

        public Awards LoadAwards(IWriteLock writeLock = null)
        {
            throw new NotImplementedException();
        }

        public void SaveAwards(Awards awards, IWriteLock writeLock)
        {
            throw new NotImplementedException();
        }

        public void SaveTournamentStats(IDictionary<string, TournamentStats> stats, IWriteLock writeLock)
        {
            throw new NotImplementedException();
        }

        public void SaveSemesterRankingList(GlobalRanklist ranklist)
        {
            throw new NotImplementedException();
        }

        public void SaveGlobalRankingList(GlobalRanklist ranklist, IWriteLock writeLock)
        {
            throw new NotImplementedException();
        }

        public void SaveTournamentRanklist(TournamentRankings ranklist)
        {
            throw new NotImplementedException();
        }

        public void SaveTournamentMatches(TournamentMatches matches, IWriteLock writeLock)
        {
            throw new NotImplementedException();
        }

        public void SaveRecentActivities(List<Activity> activities, IWriteLock writeLock)
        {
            throw new NotImplementedException();
        }

        public IEnumerable<string> GetChallengeIds()
        {
            throw new NotImplementedException();
        }

        public IEnumerable<IChallenge> LoadChallenges()
        {
            return challenges;
        }

        public Challenge LoadChallenge(string challenge, IWriteLock writeLock = null)
        {
            return (Challenge) challenges.FirstOrDefault(x => x.Id.Equals(challenge));
        }

        public void CreateChallenge(IChallenge challenge)
        {
            throw new NotImplementedException();
        }

        public void SaveChallenge(IChallenge challenge, IWriteLock writeLock)
        {
            throw new NotImplementedException();
        }

        public IEnumerable<TestParameters> LoadTestProperties(IChallenge challenge)
        {
            throw new NotImplementedException();
        }

        public void SaveTestProperties(IChallenge challenge, IEnumerable<TestParameters> testParameters, IWriteLock writeLock)
        {
            throw new NotImplementedException();
        }

        public IEnumerable<string> GetAdditionalFiles(IChallenge challenge)
        {
            throw new NotImplementedException();
        }

        public void RenameAdditionalFile(IChallenge challenge, string oldname, string newname)
        {
            throw new NotImplementedException();
        }

        public void SaveAdditionalFile(IChallenge challenge, string filename, byte[] data)
        {
            throw new NotImplementedException();
        }

        public void DeleteAdditionalFile(IChallenge challenge, string filename)
        {
            throw new NotImplementedException();
        }

        public string GetCustomTestRunnerPath(IChallenge challenge, string relativePath)
        {
            throw new NotImplementedException();
        }

        public void DeleteChallenge(IChallenge challenge)
        {
            throw new NotImplementedException();
        }

        public (string name, byte[] data, string type, DateTime lastMod) GetChallengeAdditionalFile(string challengeName, string fileName)
        {
            throw new NotImplementedException();
        }

        public (string name, byte[] data, string type, DateTime lastMod) GetChallengeZip(IChallenge challenge)
        {
            throw new NotImplementedException();
        }

        public string GetChallengeAdditionalFileContentAsText(string challengeName, string fileName)
        {
            throw new NotImplementedException();
        }

        public string GetMimeTypeForFile(string fileName)
        {
            throw new NotImplementedException();
        }

        public void WriteChallengeAdditionalFileContent(string challengeName, string fileName, string content)
        {
            throw new NotImplementedException();
        }

        public ReviewTemplate LoadReviewTemplate(IChallenge challenge)
        {
            throw new NotImplementedException();
        }

        public void ChangeChallengeId(IChallenge challenge, string newId)
        {
            throw new NotImplementedException();
        }

        public byte[] LoadChallengeFileContent(string challengeId, string fileName)
        {
            throw new NotImplementedException();
        }

        public List<Result> LoadTestedResults(IChallenge challenge)
        {
            throw new NotImplementedException();
        }

        public Result LoadResult(string submissionPath, IWriteLock writeLock = null)
        {
            throw new NotImplementedException();
        }

        public Result LoadResult(string challenge, string id, IWriteLock writeLock = null)
        {
            throw new NotImplementedException();
        }

        public IEnumerable<Result> LoadAllSubmissions(bool includeDead = false)
        {
            throw new NotImplementedException();
        }

        public IEnumerable<Result> LoadAllSubmissionsFor(IChallenge challenge, bool includeDead = false)
        {
            throw new NotImplementedException();
        }

        public IEnumerable<Result> LoadAllSubmissionsFor(IMember member, bool includeDead = false)
        {
            throw new NotImplementedException();
        }

        public ISubmission StoreNewSubmission(IMember member, DateTime date, string challengeName, byte[] zipData, IEnumerable<string> compilableFiles)
        {
            throw new NotImplementedException();
        }

        public void DeleteSubmission(Result result)
        {
            throw new NotImplementedException();
        }

        public bool UpdateEvaluationResult(ISubmission submission, EvaluationParameters evaluationParameters, bool resetStats = false)
        {
            throw new NotImplementedException();
        }

        public void AbortRunningReview(ISubmission submission)
        {
            throw new NotImplementedException();
        }

        public void SetReviewStateAsStarted(ISubmission submission, string reviewer, DateTime reviewDueDate)
        {
            throw new NotImplementedException();
        }

        public void SetReviewStateAsReviewed(ISubmission submission, int rating)
        {
            throw new NotImplementedException();
        }

        public void SetReviewStateAsSkipped(ISubmission submission)
        {
            throw new NotImplementedException();
        }

        public void UnsetReviewerAndResetIfInProgress(ISubmission submission)
        {
            throw new NotImplementedException();
        }

        public List<HintCategory> LoadFailedSubmissionReport(ISubmission submission)
        {
            throw new NotImplementedException();
        }

        public void DeleteCurrentSubmissionBuild(string submissionPath)
        {
            throw new NotImplementedException();
        }

        public void MarkSubmissionForRerun(ISubmission submission)
        {
            throw new NotImplementedException();
        }

        public void MarkSubmissionAsDead(ISubmission submission)
        {
            throw new NotImplementedException();
        }

        public void SaveReview(ISubmission submission, ReviewData review)
        {
            throw new NotImplementedException();
        }

        public ReviewData LoadReview(ISubmission submission)
        {
            throw new NotImplementedException();
        }

        public bool HasReviewFile(ISubmission submission)
        {
            throw new NotImplementedException();
        }

        public IEnumerable<ISubmission> GetSubmissionsWithoutResult(IChallenge challenge)
        {
            throw new NotImplementedException();
        }

        public IEnumerable<ISubmission> SubmissionsOfChallengeWhichShouldRerun(string challenge)
        {
            throw new NotImplementedException();
        }

        public void LogFailedTestruns(ISubmission submission, EvaluationParameters evaluationParameters, byte[] data)
        {
            throw new NotImplementedException();
        }

        public IEnumerable<string> GetSubmissionFilesRelativePath(ISubmission submission)
        {
            throw new NotImplementedException();
        }

        public string GetSubmissionFileContent(ISubmission submission, string relativeFilePath)
        {
            throw new NotImplementedException();
        }

        public void CreateTournament(ITournament tournament)
        {
            throw new NotImplementedException();
        }

        public void SaveTournamentProperties(ITournament tournament, IWriteLock writeLock)
        {
            throw new NotImplementedException();
        }

        public IEnumerable<string> GetTournamentNames()
        {
            throw new NotImplementedException();
        }

        public void DeleteAdditionalFile(ITournament tournament, string filename)
        {
            throw new NotImplementedException();
        }

        public IEnumerable<Bundle> LoadAllBundles()
        {
            return bundles.Select(x => (Bundle) x);
        }

        public void CreateBundle(string id, string title, string description, string authorId, string category, IEnumerable<string> challenges)
        {
            throw new NotImplementedException();
        }

        public Bundle LoadBundle(string id, IWriteLock writeLock = null)
        {
            throw new NotImplementedException();
        }

        public void SaveBundle(Bundle bundle, IWriteLock writeLock)
        {
            throw new NotImplementedException();
        }

        public HelpPage LoadHelpPage(string path, bool includeDescription = true, IWriteLock writeLock = null)
        {
            throw new NotImplementedException();
        }

        public List<HelpPage> GetHelpHierarchy()
        {
            throw new NotImplementedException();
        }

        public (string name, byte[] data, string type, DateTime lastMod) GetHelpAdditionalFile(string path)
        {
            throw new NotImplementedException();
        }

        public void SaveHelpPage(HelpPage helpPage, IWriteLock writeLock)
        {
            throw new NotImplementedException();
        }

        public void SetDuplicateScore(ISubmission submission, int? score, string bestMatchId)
        {
            throw new NotImplementedException();
        }

        public string GetLastVersionHash()
        {
            throw new NotImplementedException();
        }

        public void SaveLastVersionHash(string version)
        {
            throw new NotImplementedException();
        }

        public IEnumerable<Group> LoadAllGroups()
        {
            return groups.Select(x => (Group) x);
        }

        public Group LoadGroup(string id, IWriteLock writeLock = null)
        {
            return (Group) groups.First(x => x.Id.Equals(id));
        }

        public void SaveGroup(Group group, IWriteLock writeLock)
        {
            throw new NotImplementedException();
        }

        public IEnumerable<string> GetGroupIds()
        {
            throw new NotImplementedException();
        }

        public void MoveChallengeSubmissionTo(IChallenge challenge, string newId)
        {
            throw new NotImplementedException();
        }

        public void CreateGroup(string id, string title, List<string> groupAdminIds, bool isSupergroup, string[] SubGroups, string[] forcedChallenges, string[] availableChallenges,
            int maxUnlockedChallenges, int? requiredPoints, DateTime? startDate, DateTime? endDate)
        {
            throw new NotImplementedException();
        }

        public void DeleteGroup(string id)
        {
            throw new NotImplementedException();
        }

        public void ChangeGroupId(IGroup group, string newId)
        {
            throw new NotImplementedException();
        }

        public bool IsMaintenanceMode { get; set; }

        public IEnumerable<(string name, byte[] data)> GetZipFiles(byte[] data)
        {
            throw new NotImplementedException();
        }

        public (Challenge, IEnumerable<TestParameters>, IEnumerable<(string name, byte[] data)>) LoadChallengeFromZip(byte[] data)
        {
            throw new NotImplementedException();
        }

        public bool ChallengeExists(string challengeId)
        {
            throw new NotImplementedException();
        }

        private static void InitializeChallenges()
        {
            var bundle = new Bundle {Title = "Bundle", Id = "bundle", Author = "creator"};
            for (var i = 1; i <= 5; i++)
            {
                var challenge = new Challenge
                {
                    AuthorID = "admin",
                    LastEditorID = "admin",
                    Id = "bundleChallenge" + i,
                    Title = "Bundle Challenge " + 1,
                    IsDraft = false,
                    State = new ChallengeState {DifficultyRating = 10}
                };
                challenges.Add(challenge);
                bundle.Challenges.Add(challenge.Id);
            }

            bundles.Add(bundle);
            for (var i = 0; i <= 3; i++)
            {
                challenges.Add(new Challenge
                {
                    AuthorID = "creator",
                    LastEditorID = "creator",
                    Id = "challenge" + i,
                    Title = "Challenge " + i,
                    State = new ChallengeState {DifficultyRating = 8}
                });
            }

            for (var i = 4; i <= 5; i++)
            {
                challenges.Add(new Challenge
                {
                    AuthorID = "admin",
                    LastEditorID = "admin",
                    Id = "challenge" + i,
                    Title = "Challenge " + i,
                    State = new ChallengeState {DifficultyRating = 10}
                });
            }

            for (var i = 6; i <= 10; i++)
            {
                challenges.Add(new Challenge
                {
                    AuthorID = "admin",
                    LastEditorID = "admin",
                    Id = "challenge" + i,
                    Title = "Challenge " + i,
                    State = new ChallengeState {DifficultyRating = 40}
                });
            }
        }

        private static void InitializeGroups()
        {
            groups.Add(InitializeDefaultGroup1());
            groups.Add(InitializeDefaultGroup2());
            groups.Add(InitializeForcedGroup1());
            groups.Add(InitializeForcedGroup2());
            groups.Add(InitializeMax3Group());
            groups.Add(InitializeBundleGroupNoMax());
            groups.Add(InitializeBundleGroupOneOtherNoMax());
            groups.Add(InitializeBundleGroup3Max());
            groups.Add(InitializeHardGroup1());
            groups.Add(InitializeHardGroup2());
        }

        private static IGroup InitializeDefaultGroup1()
        {
            return new Group
            {
                Id = "defaultGroup1",
                Title = "Default Group",
                ForcedChallenges = new[] {"challenge1"},
                AvailableChallenges = new[] {"challenge2", "challenge3"},
                GroupAdminIds = new List<string> {"groupAdminOneGroup", "groupAdminTwoGroups"}
            };
        }

        private static IGroup InitializeDefaultGroup2()
        {
            return new Group
            {
                Id = "defaultGroup2",
                Title = "Default Group 2",
                AvailableChallenges = new[] {"challenge2", "challenge1", "challenge5"},
                GroupAdminIds = new List<string> {"groupAdminTwoGroups"}
            };
        }

        private static IGroup InitializeForcedGroup1()
        {
            return new Group
            {
                Id = "forcedGroup1",
                Title = "Few forced Group 1",
                ForcedChallenges = new[] {"challenge1", "challenge2"},
                AvailableChallenges = new[] {"challenge3", "challenge4"}
            };
        }

        private static IGroup InitializeForcedGroup2()
        {
            return new Group {Id = "forcedGroup2", Title = "Few forced Group 2", ForcedChallenges = new[] {"challenge3", "challenge4"}};
        }

        private static IGroup InitializeMax3Group()
        {
            return new Group
            {
                Id = "3MaxGroup",
                Title = "3 Max Group",
                AvailableChallenges = new[] {"challenge1", "challenge2", "challenge3", "challenge4", "challenge5"},
                MaxUnlockedChallenges = 3
            };
        }

        private static IGroup InitializeBundleGroupNoMax()
        {
            return new Group
            {
                Id = "bundleGroupNoMax",
                Title = "Bundle Group No Max",
                AvailableChallenges = new[] {"bundleChallenge1", "bundleChallenge2", "bundleChallenge3", "bundleChallenge4", "bundleChallenge5"}
            };
        }

        private static IGroup InitializeBundleGroup3Max()
        {
            return new Group
            {
                Id = "bundleGroup3Max",
                Title = "Bundle Group 3 Max",
                AvailableChallenges = new[] {"bundleChallenge1", "bundleChallenge2", "bundleChallenge3", "bundleChallenge4", "bundleChallenge5"},
                MaxUnlockedChallenges = 3
            };
        }

        private static IGroup InitializeBundleGroupOneOtherNoMax()
        {
            return new Group
            {
                Id = "bundleGroupOneOtherNoMax",
                Title = "Bundle Group No Max",
                AvailableChallenges = new[]
                {
                    "challenge1", "bundleChallenge1", "bundleChallenge2", "bundleChallenge3", "bundleChallenge4", "bundleChallenge5"
                }
            };
        }

        private static IGroup InitializeHardGroup1()
        {
            return new Group
            {
                Id = "hardGroup1",
                Title = "Hard group 1",
                AvailableChallenges = new[] {"challenge6", "challenge7", "challenge8", "challenge1"},
                MaxUnlockedChallenges = 2
            };
        }

        private static IGroup InitializeHardGroup2()
        {
            return new Group
            {
                Id = "hardGroup2",
                Title = "Hard group 2",
                AvailableChallenges = new[] {"challenge9", "challenge10", "challenge3"},
                MaxUnlockedChallenges = 2
            };
        }

        private static void InitializeMemberFundament()
        {
            members.Add(InitializeAdminMember());
            members.Add(InitializeCreatorMember());
        }

        private static void InitializeLeftMembers()
        {
            members.Add(InitializeGroupAdminOneGroupMember());
            members.Add(InitializeGroupAdminTwoGroupsMember());
            members.Add(InitializeGroupAdminOfNoGroupMember());
            members.Add(InitializeMemberWithOutGroup());
            members.Add(InitializeMemberWithForcedGroup());
            members.Add(InitializeMemberWithTwoForcedGroups());
            members.Add(InitializeMemberWithForcedGroupForcedSolved());
            members.Add(InitializeMemberWithGroupNoForcedMax3());
            members.Add(InitializeMemberWithGroupNoForcedMax3DiffTooLow());
            members.Add(InitializeMemberWithBundleGroupNoMax());
            members.Add(InitializeMemberWithBundleGroupOneOtherNoMax());
            members.Add(InitializeMemberWithBundleGroup3Max());
            members.Add(InitializeMemberWithBundleGroupNoMaxFirstSolved());
            members.Add(InitializeMemberNotThatExperiencedTooHardGroups());
        }

        private static IMember InitializeAdminMember()
        {
            return new Member {Name = "admin", Id = "admin", Uid = "admin", Roles = new[] {"admin"}};
        }

        private static IMember InitializeCreatorMember()
        {
            return new Member {Name = "creatorNoGroup", Id = "creatorNoGroup", Uid = "creatorNoGroup", Roles = new[] {"creator"}};
        }

        private static IMember InitializeGroupAdminOneGroupMember()
        {
            return new Member
            {
                Name = "groupAdminOneGroup",
                Id = "groupAdminOneGroup",
                Uid = "groupAdminOneGroup",
                Roles = new[] {"groupAdmin"},
                Groups = new[] {"defaultGroup1"}
            };
        }

        private static IMember InitializeGroupAdminTwoGroupsMember()
        {
            return new Member
            {
                Name = "groupAdminTwoGroups",
                Id = "groupAdminTwoGroups",
                Uid = "groupAdminTwoGroups",
                Roles = new[] {"groupAdmin"},
                Groups = new[] {"defaultGroup1", "defaultGroup2"}
            };
        }

        private static IMember InitializeGroupAdminOfNoGroupMember()
        {
            return new Member
            {
                Name = "groupAdminOfNoGroup",
                Id = "groupAdminOfNoGroup",
                Uid = "groupAdminOfNoGroup",
                Roles = new[] {"groupAdmin"},
                Groups = new[] {"defaultGroup1"}
            };
        }

        private static IMember InitializeMemberWithOutGroup()
        {
            return new Member
            {
                Name = "memberWithoutGroup",
                Id = "memberWithoutGroup",
                Uid = "memberWithoutGroup",
                SolvedChallenges = new[] {"challenge1", "challenge2"},
                UnlockedChallenges = new[] {"challenge3"}
            };
        }

        private static IMember InitializeMemberWithForcedGroup()
        {
            return new Member {Name = "memberWithForcedGroup", Id = "memberWithForcedGroup", Uid = "memberWithForcedGroup", Groups = new[] {"forcedGroup1"}};
        }

        private static IMember InitializeMemberWithTwoForcedGroups()
        {
            return new Member
            {
                Name = "memberWithTwoForcedGroups",
                Id = "memberWithTwoForcedGroups",
                Uid = "memberWithTwoForcedGroups",
                Groups = new[] {"forcedGroup1", "forcedGroup2"}
            };
        }

        private static IMember InitializeMemberWithForcedGroupForcedSolved()
        {
            return new Member
            {
                Name = "memberWithForcedGroupForcedSolved",
                Id = "memberWithForcedGroupForcedSolved",
                Uid = "memberWithForcedGroupForcedSolved",
                Groups = new[] {"forcedGroup1"},
                SolvedChallenges = new[] {"challenge1", "challenge2"}
            };
        }

        private static IMember InitializeMemberWithGroupNoForcedMax3()
        {
            return new Member
            {
                Name = "memberWithGroupNoForcedMax3", Id = "memberWithGroupNoForcedMax3", Uid = "memberWithGroupNoForcedMax3", Groups = new[] {"3MaxGroup"}
            };
        }

        private static IMember InitializeMemberWithGroupNoForcedMax3DiffTooLow()
        {
            return new Member
            {
                Name = "memberWithGroupNoForcedMax3DiffTooLow",
                Id = "memberWithGroupNoForcedMax3DiffTooLow",
                Uid = "memberWithGroupNoForcedMax3DiffTooLow",
                Groups = new[] {"3MaxGroup"},
                AverageDifficultyLevel = 10
            };
        }

        private static IMember InitializeMemberWithBundleGroupNoMax()
        {
            return new Member
            {
                Name = "memberWithBundleGroupNoMax",
                Id = "memberWithBundleGroupNoMax",
                Uid = "memberWithBundleGroupNoMax",
                Groups = new[] {"bundleGroupNoMax"}
            };
        }

        private static IMember InitializeMemberWithBundleGroupOneOtherNoMax()
        {
            return new Member
            {
                Name = "memberWithBundleGroupOneOtherNoMax",
                Id = "memberWithBundleGroupOneOtherNoMax",
                Uid = "memberWithBundleGroupOneOtherNoMax",
                Groups = new[] {"bundleGroupOneOtherNoMax"}
            };
        }

        private static IMember InitializeMemberWithBundleGroup3Max()
        {
            return new Member
            {
                Name = "memberWithBundleGroup3Max",
                Id = "memberWithBundleGroup3Max",
                Uid = "memberWithBundleGroup3Max",
                Groups = new[] {"bundleGroup3Max"},
                SolvedChallenges = new[] {"bundleChallenge1"}
            };
        }

        private static IMember InitializeMemberWithBundleGroupNoMaxFirstSolved()
        {
            return new Member
            {
                Name = "memberWithBundleGroupNoMaxFirstSolved",
                Id = "memberWithBundleGroupNoMaxFirstSolved",
                Uid = "memberWithBundleGroupNoMaxFirstSolved",
                Groups = new[] {"bundleGroupNoMax"},
                SolvedChallenges = new[] {"bundleChallenge1"}
            };
        }

        private static IMember InitializeMemberNotThatExperiencedTooHardGroups()
        {
            return new Member
            {
                Name = "memberNotThatExperiencedTooHardGroups",
                Id = "memberNotThatExperiencedTooHardGroups",
                Uid = "memberNotThatExperiencedTooHardGroups",
                Groups = new[] {"hardGroup1", "hardGroup2"}
            };
        }
    }
}
