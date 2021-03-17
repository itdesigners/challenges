using System;
using System.Collections.Generic;

namespace SubmissionEvaluation.Contracts.Data
{
    public interface IGroup
    {
        string Id { get; }
        List<string> GroupAdminIds { get; }
        string Title { get; }
        string[] AvailableChallenges { get; }
        bool IsSuperGroup { get; }
        string[] SubGroups { get; }
        int MaxUnlockedChallenges { get; }
        string[] ForcedChallenges { get; }
        int? RequiredPoints { get; }
        DateTime? StartDate { get; }
        DateTime? EndDate { get; set; }
    }
}
