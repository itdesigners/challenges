using System;
using System.Text.Json.Serialization;
using SubmissionEvaluation.Contracts.Data.Review;
using YamlDotNet.Serialization;

namespace SubmissionEvaluation.Contracts.Data
{
    public class Result : ISubmission
    {
        private DateTime lastSubmissionDate;

        public Result()
        {
        }

        [YamlIgnore] [JsonIgnore] public string SubmissionPath { get; set; }

        public DateTime LastTestrun { get; set; }
        public string CompilerVersion { get; set; }
        public bool ReportFailing { get; set; }
        public string Hash { get; set; }
        public int? DuplicateScore { get; set; }
        public string DuplicateId { get; set; }

        public string MemberName { get; set; }
        public string? CompileError { get; set; }

        [JsonIgnore]
        [YamlIgnore]
        public bool IsPassed => EvaluationResult == EvaluationResult.Succeeded || EvaluationResult == EvaluationResult.SucceededWithTimeout;

        [YamlIgnore] [JsonIgnore] public bool IsTestsFailed => EvaluationResult == EvaluationResult.TestsFailed || EvaluationResult == EvaluationResult.Timeout;

        [YamlIgnore]
        [JsonIgnore]
        public bool HasTestsRun =>
            (EvaluationState == EvaluationState.Evaluated || EvaluationState == EvaluationState.RerunRequested) && (IsPassed || IsTestsFailed);

        [YamlIgnore] public string SubmissionId { get; set; }

        public DateTime SubmissionDate { get; set; }
        public int ExecutionDuration { get; set; }

        [YamlMember(Alias = "id")] public string MemberId { get; set; }

        public string Language { get; set; }
        public long SizeInBytes { get; set; }

        [YamlIgnore] public string Challenge { get; set; }

        public EvaluationState EvaluationState { get; set; }

        [YamlMember(Alias = "result")] public EvaluationResult EvaluationResult { get; set; }

        [YamlMember(Alias = "passed")] public int TestsPassed { get; set; }

        [YamlMember(Alias = "failed")] public int TestsFailed { get; set; }

        public int TestsSkipped { get; set; }
        public int? CustomScore { get; set; }
        public ReviewStateType ReviewState { get; set; }
        public string Reviewer { get; set; }
        public DateTime? ReviewDate { get; set; }
        public int ReviewRating { get; set; }
        [YamlIgnore] public bool HasReviewData { get; set; }

        public DateTime LastSubmissionDate
        {
            get => lastSubmissionDate > SubmissionDate ? lastSubmissionDate : SubmissionDate;
            set => lastSubmissionDate = value;
        }
    }
}
