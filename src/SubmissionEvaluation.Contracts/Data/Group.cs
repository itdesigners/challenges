using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;
using YamlDotNet.Serialization;

namespace SubmissionEvaluation.Contracts.Data
{
    public class Group : IGroup
    {
        private string[] availableChallenges;
        private string[] forcedChallenges;

        [YamlIgnore] [JsonIgnore] public string Id { get; set; }

        public string Title { get; set; }
        public List<string> GroupAdminIds { get; set; }

        public string[] AvailableChallenges { get => availableChallenges ?? new string[0]; set => availableChallenges = value; }

        public int MaxUnlockedChallenges { get; set; }

        public string[] ForcedChallenges { get => forcedChallenges ?? new string[0]; set => forcedChallenges = value; }

        public int? RequiredPoints { get; set; }
        public DateTime? StartDate { get; set; }

        public bool IsSuperGroup { get; set; }

        public string[] SubGroups { get; set; } = new string[] { };
        public DateTime? EndDate { get; set; }
    }
}
