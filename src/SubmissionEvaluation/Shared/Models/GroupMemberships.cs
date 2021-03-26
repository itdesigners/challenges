using System.Collections.Generic;
using SubmissionEvaluation.Contracts.Data;

namespace SubmissionEvaluation.Shared.Models
{
    public class GroupMemberships<T> where T : IMember
    {
        public string GroupName { get; set; }
        public List<T> Members { get; set; } = new List<T>();
    }
}
