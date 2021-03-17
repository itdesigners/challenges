using System;
using System.Collections.Generic;
using System.IO;
using SubmissionEvaluation.Contracts.Data;
using SubmissionEvaluation.Contracts.Data.Ranklist;
using SubmissionEvaluation.Contracts.Data.Review;

namespace SubmissionEvaluation.Contracts.Providers
{
    public interface IFileProvider
    {
        bool IsMaintenanceMode { get; set; }
        HelpPage LoadHelpPage(string path, bool includeDescription = true, IWriteLock writeLock = null);
        List<HelpPage> GetHelpHierarchy();
        (string name, byte[] data, string type, DateTime lastMod) GetHelpAdditionalFile(string path);
        void SaveHelpPage(HelpPage helpPage, IWriteLock writeLock);
        void SetDuplicateScore(ISubmission submission, int? score, string bestMatchId);
        string GetLastVersionHash();
        void SaveLastVersionHash(string version);
        IEnumerable<Group> LoadAllGroups();
        Group LoadGroup(string id, IWriteLock writeLock = null);
        void SaveGroup(Group group, IWriteLock writeLock);
        IEnumerable<string> GetGroupIds();

        void MoveChallengeSubmissionTo(IChallenge challenge, string newId);

        void CreateGroup(string id, string title, List<string> groupAdminIds, bool isSuperGroup, string[] subgroups, string[] forcedChallenges, string[] availableChallenges,
            int maxUnlockedChallenges, int? requiredPoints, DateTime? startDate, DateTime? endDate);

        void DeleteGroup(string id);
        void ChangeGroupId(IGroup group, string newId);
        IEnumerable<(string name, byte[] data)> GetZipFiles(byte[] data);

        (Challenge, IEnumerable<TestParameters>, IEnumerable<(string name, byte[] data)>) LoadChallengeFromZip(byte[] data);

        bool ChallengeExists(string challengeId);

        #region Dirty (Must be cleaned up, as they work with directory paths!)

        string GetPathToChallengeProperties(string challenge);

        string GetSourceZipPathForSubmission(ISubmission submission);
        IEnumerable<string> GetSubmissionFilesPath(ISubmission submission);

        string GetBuildPathForSubmissionSource(string pathToContent);
        string ExtractContent(string pathToZip, bool forceClean = true);
        #endregion

        #region Helper

        MemoryStream CreateZipInMemory((string, string)[] paths, (string, byte[])[] additionalFiles = null);
        IEnumerable<string> GetFilenamesInsideZip(byte[] data);
        T DeserializeFromText<T>(string text, HandleMode mode = HandleMode.ThrowException) where T : class, new();
        byte[] BuildZipFor(IEnumerable<(string filename, byte[] data)> files);
        void AddFileToZip(MemoryStream zipStream, byte[] fileToAdd, string pathInZip);
        IWriteLock GetLock();

        #endregion

        #region Maintenance/Operation

        void DeleteAllSubmissions();
        void DeleteAllStatistics();
        void CreateMissingDirectories();

        #endregion

        #region Members

        IEnumerable<Member> LoadMembers();
        Member LoadMember(string id, IWriteLock writeLock = null);
        void SaveMember(Member member, IWriteLock writeLock);
        void CreateMember(Member member);
        void DeleteMember(IMember member, IWriteLock writeLock);

        #endregion

        #region Statistics and Lists

        List<Activity> LoadRecentActivities(IWriteLock writeLock = null);
        GlobalRanklist LoadGlobalRanklist(IWriteLock writeLock = null);
        IEnumerable<GlobalRanklist> LoadAllSemesterRanklists();
        Awards LoadAwards(IWriteLock writeLock = null);
        void SaveAwards(Awards awards, IWriteLock writeLock);
        void SaveSemesterRankingList(GlobalRanklist ranklist);
        void SaveGlobalRankingList(GlobalRanklist ranklist, IWriteLock writeLock);
        void SaveRecentActivities(List<Activity> activities, IWriteLock writeLock);

        #endregion

        #region Challenges

        IEnumerable<string> GetChallengeIds();
        IEnumerable<IChallenge> LoadChallenges();
        Challenge LoadChallenge(string challenge, IWriteLock writeLock = null);
        void CreateChallenge(IChallenge challenge);
        void SaveChallenge(IChallenge challenge, IWriteLock writeLock);
        IEnumerable<TestParameters> LoadTestProperties(IChallenge challenge);
        void SaveTestProperties(IChallenge challenge, IEnumerable<TestParameters> testParameters, IWriteLock writeLock);
        IEnumerable<string> GetAdditionalFiles(IChallenge challenge);
        void RenameAdditionalFile(IChallenge challenge, string oldname, string newname);
        void SaveAdditionalFile(IChallenge challenge, string filename, byte[] data);
        void DeleteAdditionalFile(IChallenge challenge, string filename);
        string GetCustomTestRunnerPath(IChallenge challenge, string relativePath);
        void DeleteChallenge(IChallenge challenge);

        (string name, byte[] data, string type, DateTime lastMod) GetChallengeAdditionalFile(string challengeName, string fileName);

        (string name, byte[] data, string type, DateTime lastMod) GetChallengeZip(IChallenge challenge);
        string GetChallengeAdditionalFileContentAsText(string challengeName, string fileName);
        string GetMimeTypeForFile(string fileName);
        void WriteChallengeAdditionalFileContent(string challengeName, string fileName, string content);
        ReviewTemplate LoadReviewTemplate(IChallenge challenge);
        void ChangeChallengeId(IChallenge challenge, string newId);
        byte[] LoadChallengeFileContent(string challengeId, string fileName);

        #endregion

        #region Submissions

        List<Result> LoadTestedResults(IChallenge challenge);
        Result LoadResult(string submissionPath, IWriteLock writeLock = null);
        Result LoadResult(string challenge, string id, IWriteLock writeLock = null);
        IEnumerable<Result> LoadAllSubmissions(bool includeDead = false);
        IEnumerable<Result> LoadAllSubmissionsFor(IChallenge challenge, bool includeDead = false);
        IEnumerable<Result> LoadAllSubmissionsFor(IMember member, bool includeDead = false);

        ISubmission StoreNewSubmission(IMember member, DateTime date, string challengeName, byte[] zipData, IEnumerable<string> compilableFiles);

        void DeleteSubmission(Result result);

        bool UpdateEvaluationResult(ISubmission submission, EvaluationParameters evaluationParameters, bool resetStats = false);

        void AbortRunningReview(ISubmission submission);
        void SetReviewStateAsStarted(ISubmission submission, string reviewer, DateTime reviewDueDate);
        void SetReviewStateAsReviewed(ISubmission submission, int rating);
        void SetReviewStateAsSkipped(ISubmission submission);
        void UnsetReviewerAndResetIfInProgress(ISubmission submission);
        List<HintCategory> LoadFailedSubmissionReport(ISubmission submission);
        void DeleteCurrentSubmissionBuild(string submissionPath);
        void MarkSubmissionForRerun(ISubmission submission);
        void MarkSubmissionAsDead(ISubmission submission);

        void SaveReview(ISubmission submission, ReviewData review);
        ReviewData LoadReview(ISubmission submission);
        bool HasReviewFile(ISubmission submission);
        IEnumerable<ISubmission> GetSubmissionsWithoutResult(IChallenge challenge);
        IEnumerable<ISubmission> SubmissionsOfChallengeWhichShouldRerun(string challenge);
        void LogFailedTestruns(ISubmission submission, EvaluationParameters evaluationParameters, byte[] data);
        IEnumerable<string> GetSubmissionFilesRelativePath(ISubmission submission);
        string GetSubmissionFileContent(ISubmission submission, string relativeFilePath);

        #endregion

        #region Bundles

        IEnumerable<Bundle> LoadAllBundles();

        void CreateBundle(string id, string title, string description, string authorId, string category, IEnumerable<string> challenges);

        Bundle LoadBundle(string id, IWriteLock writeLock = null);
        void SaveBundle(Bundle bundle, IWriteLock writeLock);

        #endregion
    }
}
