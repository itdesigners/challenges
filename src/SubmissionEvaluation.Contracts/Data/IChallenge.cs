using System;
using System.Collections.Generic;

namespace SubmissionEvaluation.Contracts.Data
{
    public interface IChallenge : ICompilerProperties, IWithDescription
    {
        string AuthorID { get; }
        string LastEditorID { get; }
        string Id { get; } //ChallengeId
        string Title { get; }
        bool IsDraft { get; }
        string Source { get; }
        DateTime Date { get; }
        string Category { get; }
        RatingMethod RatingMethod { get; }
        List<string> AdditionalFiles { get; }
        List<string> IncludeTests { get; }
        List<string> DependsOn { get; }
        bool IsReviewable { get; }
        bool IsAvailable { get; }
        ChallengeState State { get; }
        bool FreezeDifficultyRating { get; }
        DateTime LastEdit { get; }
        string LearningFocus { get; }
    }
}
