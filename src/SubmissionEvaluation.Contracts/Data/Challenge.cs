using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;
using YamlDotNet.Serialization;

namespace SubmissionEvaluation.Contracts.Data
{
    public class Challenge : IChallenge
    {
        private List<string> dependsOn = new List<string>();
        private List<string> includeTests = new List<string>();
        private List<string> languages = new List<string>();
        private DateTime lastEdit;

        [YamlIgnore] [JsonIgnore] public string ReviewTemplate => "standard";

        [YamlIgnore] [JsonIgnore] public string Id { get; set; }

        [YamlMember(Alias = "Author")] public string AuthorID { get; set; }

        [YamlIgnore]
        [JsonIgnore]
        public string LastEditorID
        {
            get => string.IsNullOrEmpty(State.LastEditorId) ? AuthorID : State.LastEditorId;
            set => State.LastEditorId = value;
        }

        public string Title { get; set; }
        public RatingMethod RatingMethod { get; set; }
        public string Category { get; set; }
        public bool FreezeDifficultyRating { get; set; }
        public DateTime Date { get; set; }
        public string Source { get; set; }
        public string LearningFocus { get; set; }
        public bool IsDraft { get; set; }

        [YamlIgnore] [JsonIgnore] public bool IsAvailable => !(State.HasError || IsDraft);

        [YamlIgnore] [JsonIgnore] public bool IsReviewable => RatingMethod == RatingMethod.Fixed;

        public List<string> IncludeTests
        {
            get => includeTests ?? new List<string>();
            set => includeTests = value;
        }

        public List<string> DependsOn
        {
            get => dependsOn ?? new List<string>();
            set => dependsOn = value;
        }

        public List<string> Languages
        {
            get => languages ?? new List<string>();
            set => languages = value;
        }

        [YamlIgnore] [JsonIgnore] public string Description { get; set; }

        [YamlIgnore] [JsonIgnore] public List<string> AdditionalFiles { get; set; }

        public ChallengeState State { get; set; } = new ChallengeState();

        public DateTime LastEdit
        {
            get => lastEdit < Date ? Date : lastEdit;
            set => lastEdit = value;
        }

        public override string ToString()
        {
            return $"Challenge: {Id}";
        }
    }
}
