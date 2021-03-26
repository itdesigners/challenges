using System;
using System.Collections.Generic;
using System.Linq;

namespace SubmissionEvaluation.Contracts.Data
{
    public class EvaluationParameters
    {
        public bool IsPassed
        {
            get
            {
                if (Results == null || Results.Count == 0)
                {
                    return false;
                }

                return Results.All(x => x.IsPassed);
            }
        }

        public int ExecutionDuration { get { return HasTestRun ? (int) Math.Round(Results.Average(x => x.ExecutionDuration), 0) : int.MaxValue; } }

        public bool HasTestRun =>
            State != EvaluationResult.CompilationError && State != EvaluationResult.NotAllowedLanguage && State != EvaluationResult.Undefined &&
            State != EvaluationResult.UnknownError;

        public string Language { get; set; }
        public List<RunResult> Results { get; set; } = new List<RunResult>();
        public List<FailedTestRunDetails> ErrorDetails { get; set; } = new List<FailedTestRunDetails>();
        public long SizeInBytes { get; set; }
        public string CompilerVersion { get; set; }
        public EvaluationResult State { get; set; }
        public int TestsPassed => Results.Count(x => x.IsPassed);
        public int TestsFailed => Results.Count(x => !x.IsPassed);
        public int TestsSkipped { get; set; }
        public int? CustomScore { get; set; }
    }
}
