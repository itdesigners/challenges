using System;
using System.Collections.Generic;
using SubmissionEvaluation.Contracts.Data;

namespace SubmissionEvaluation.Contracts.ClientPocos
{
    public class Group : IGroup
    {
        public Group(IGroup group)
        {
            Id = group.Id;
            ForcedChallenges = group.ForcedChallenges;
            AvailableChallenges = group.AvailableChallenges;
            MaxUnlockedChallenges = group.MaxUnlockedChallenges;
            RequiredPoints = group.RequiredPoints;
            Title = group.Title;
            StartDate = group.StartDate;
            EndDate = group.EndDate;
            GroupAdminIds = group.GroupAdminIds ?? new List<string>();
            IsSuperGroup = group.IsSuperGroup;
            SubGroups = group.SubGroups;
        }


        public Group()
        {
        }

        public string Id { get; set; }
        public List<string> GroupAdminIds { get; set; }

        public string Title { get; set; }

        public string[] AvailableChallenges { get; set; }

        public int MaxUnlockedChallenges { get; set; }

        public string[] ForcedChallenges { get; set; }

        public int? RequiredPoints { get; set; }
        public DateTime? StartDate { get; set; }
        public DateTime? EndDate { get; set; }

        public bool IsSuperGroup { get; set; }

        public string[] SubGroups { get; set; }
    }
}
