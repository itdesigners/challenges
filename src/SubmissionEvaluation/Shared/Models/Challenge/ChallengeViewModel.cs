using System;
using System.Collections.Generic;
using SubmissionEvaluation.Classes.Config;
using SubmissionEvaluation.Contracts.Data;
using SubmissionEvaluation.Contracts.Data.Ranklist;
using Member = SubmissionEvaluation.Contracts.ClientPocos.Member;

namespace SubmissionEvaluation.Shared.Models.Challenge
{
    public class ChallengeViewModel : GenericModel
    {
        public string Title { get; set; }
        public bool IsDraft { get; set; }
        public string Name { get; set; }
        public string Source { get; set; }
        public string Bundle { get; set; }
        public string BundleTitle { get; set; }
        public string LastChallenge { get; set; }
        public string NextChallenge { get; set; }
        public string Description { get; set; }
        public string MinEffort { get; set; }
        public string MaxEffort { get; set; }
        public RatingMethodConfig RatingMethod { get; set; }
        public string Category { get; set; }
        public Member Author { get; set; }
        public Member LastEditor { get; set; }
        public DateTime PublishDate { get; set; }
        public string Languages { get; set; }
        public string Features { get; set; }
        public ChallengeRanklist Ranklist { get; set; }
        public int? DifficultyRating { get; set; }
        public string DifficultyRatingColor { get; set; }
        public RatingPoints Points { get; set; }
        public bool Solved { get; set; }
        public bool CanRate { get; set; }
        public string LearningFocus { get; set; }
        public List<string> PartOfGroups { get; set; }
    }
}
