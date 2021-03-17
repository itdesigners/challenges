using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using Markdig.Helpers;
using SubmissionEvaluation.Contracts.Data;

namespace SubmissionEvaluation.Shared.Models.Admin
{
    public class GroupModel<T, S, R> : GenericModel where T : IChallenge where S : IMember where R: IGroup
    {
        [Required] public string Id { get; set; }

        [Required] public string Title { get; set; }
        public List<string> GroupAdminsIds { get; set; }
        public List<S> AdminsSelectable { get; set; }
        public List<string> AvailableChallenges { get; set; }
        public List<string> SubGroups { get; set; } = new List<string>();
        public bool IsSuperGroup { get; set; }
        public int MaxUnlockedChallenges { get; set; }
        public List<string> ForcedChallenges { get; set; }
        public int? RequiredPoints { get; set; }
        public string SelectedAvailableChallenge { get; set; }
        public string SelectedAddAvailableChallenge { get; set; }
        public string SelectedSubGroup { get; set; }
        public string SelectedAddSubGroup { get; set; }
        public string SelectedForcedChallenge { get; set; }
        public string SelectedAddForcedChallenge { get; set; }
        public List<T> SelectableForcedChallenges { get; set; }
        public List<T> SelectableAvailableChallenges { get; set; }
        public List<R> SelectableSubGroups { get; set; } = new List<R>();
        public DateTime? StartDate { get; set; }
        public DateTime? EndDate { get; set; }
    }
}
