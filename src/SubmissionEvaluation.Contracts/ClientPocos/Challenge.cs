using System;
using System.Collections.Generic;
using SubmissionEvaluation.Contracts.Data;

namespace SubmissionEvaluation.Contracts.ClientPocos
{
    public class Challenge : IChallenge
    {
        public Challenge(IChallenge c)
        {
            AdditionalFiles = c.AdditionalFiles;
            AuthorID = c.AuthorID;
            Category = c.Category;
            Date = c.Date;
            DependsOn = c.DependsOn;
            Description = c.Description;
            Id = c.Id;
            IncludeTests = c.IncludeTests;
            IsAvailable = c.IsAvailable;
            IsDraft = c.IsDraft;
            IsReviewable = c.IsReviewable;
            Languages = c.Languages;
            LastEdit = c.LastEdit;
            LastEditorID = c.LastEditorID;
            RatingMethod = c.RatingMethod;
            Source = c.Source;
            State = c.State;
            FreezeDifficultyRating = c.FreezeDifficultyRating;
            Title = c.Title;
            LearningFocus = c.LearningFocus;
        }

        public Challenge()
        {
        }

        public string AuthorID { get; set; }

        public string LastEditorID { get; set; }

        public string Id { get; set; }

        public string Title { get; set; }

        public bool IsDraft { get; set; }

        public string Source { get; set; }

        public DateTime Date { get; set; }

        public string Category { get; set; }

        public RatingMethod RatingMethod { get; set; }

        public List<string> AdditionalFiles { get; set; }

        public List<string> IncludeTests { get; set; }

        public List<string> DependsOn { get; set; }

        public bool IsReviewable { get; set; }

        public bool IsAvailable { get; set; }

        public ChallengeState State { get; set; }

        public bool FreezeDifficultyRating { get; set; }

        public DateTime LastEdit { get; set; }

        public List<string> Languages { get; set; }

        public string Description { get; set; }

        public string LearningFocus { get; set; }
    }
}
