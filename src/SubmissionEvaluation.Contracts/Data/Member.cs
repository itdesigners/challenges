using System;
using System.Linq;
using System.Text.Json.Serialization;
using SubmissionEvaluation.Contracts.Data.Review;
using YamlDotNet.Serialization;

namespace SubmissionEvaluation.Contracts.Data
{
    public class Member : IMember
    {
        public static string REMOVED_ENTRY_ID = "MEMBER_REMOVED";
        private string[] canRate;
        private string[] groups;
        private string[] reviewLanguages;

        private string[] roles;
        private string[] solvedChallenges;
        private string[] unlockedChallenges;

        public string Name { get; set; }
        public string Mail { get; set; }
        public string Id { get; set; }
        public string Uid { get; set; }
        public DateTime DateOfEntry { get; set; } = DateTime.Now;

        [YamlIgnore]
        [JsonIgnore]
        public bool IsReviewer =>
            ReviewLevel != ReviewLevel.Inactive && ReviewLevel != ReviewLevel.Deactivated && ReviewLanguages != null && ReviewLanguages.Length > 0;

        public int ReviewCounter { get; set; }
        public ReviewLevel ReviewLevel { get; set; } = ReviewLevel.Inactive;
        public int ReviewFrequency { get; set; } = 14;
        public DateTime LastReview { get; set; }

        public string[] ReviewLanguages { get => reviewLanguages ?? new string[0]; set => reviewLanguages = value; }

        public string[] Roles { get => roles ?? new string[0]; set => roles = value; }

        public string[] Groups { get => groups ?? new string[0]; set => groups = value; }

        public string[] UnlockedChallenges { get => unlockedChallenges ?? new string[0]; set => unlockedChallenges = value; }

        public string[] SolvedChallenges { get => solvedChallenges ?? new string[0]; set => solvedChallenges = value; }

        public string[] CanRate { get => canRate ?? new string[0]; set => canRate = value; }

        public string Password { get; set; }
        public DateTime? LastNotificationCheck { get; set; }

        [YamlIgnore] [JsonIgnore] public string FirstName => Name.Split(' ')[0];

        public DateTime LastActivity { get; set; }
        public MemberType Type { get; set; }
        public MemberState State { get; set; }
        public bool IsAdmin => Roles.Any(x => x.Equals("admin"));
        public bool IsCreator => Roles.Any(x => x.Equals("creator"));
        public bool IsGroupAdmin => Roles.Any(x => x.Equals("groupAdmin"));
        public int AverageDifficultyLevel { get; set; }
    }
}
