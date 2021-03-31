using System;
using SubmissionEvaluation.Contracts.Data.Review;

namespace SubmissionEvaluation.Contracts.Data
{
    public interface ISubmission
    {
        DateTime SubmissionDate { get; }
        DateTime LastSubmissionDate { get; }
        string SubmissionId { get; }
        int ExecutionDuration { get; }
        bool IsPassed { get; }
        bool IsTestsFailed { get; }
        bool HasTestsRun { get; }
        long SizeInBytes { get; }
        string MemberId { get; }
        EvaluationResult EvaluationResult { get; }
        int TestsPassed { get; }
        int TestsFailed { get; }
        int TestsSkipped { get; }
        string Challenge { get; }
        string Language { get; }
        int ReviewRating { get; }
        bool HasReviewData { get; }
        ReviewStateType ReviewState { get; }
        EvaluationState EvaluationState { get; }
        string Reviewer { get; }
        DateTime? ReviewDate { get; }
        int? CustomScore { get; }
        string MemberName { get; set; }
    }
}
