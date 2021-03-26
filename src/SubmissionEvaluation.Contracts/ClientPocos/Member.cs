using System;
using SubmissionEvaluation.Contracts.Data;
using SubmissionEvaluation.Contracts.Data.Review;

namespace SubmissionEvaluation.Contracts.ClientPocos
{
    public class Member : IMember
    {
        public Member()
        {
        }

        public Member(IMember member, bool fullInfo = true, bool isPasswordRequested = false)
        {
            if (fullInfo)
            {
                AverageDifficultyLevel = member.AverageDifficultyLevel;
                CanRate = member.CanRate;
                DateOfEntry = member.DateOfEntry;
                Groups = member.Groups;
                IsAdmin = member.IsAdmin;
                IsReviewer = member.IsReviewer;
                IsCreator = member.IsCreator;
                IsGroupAdmin = member.IsGroupAdmin;
                LastActivity = member.LastActivity;
                LastNotificationCheck = member.LastNotificationCheck;
                LastReview = member.LastReview;
                Mail = member.Mail;
                Password = isPasswordRequested ? member.Password : "hidden";
                ReviewCounter = member.ReviewCounter;
                ReviewFrequency = member.ReviewFrequency;
                ReviewLanguages = member.ReviewLanguages;
                ReviewLevel = member.ReviewLevel;
                Roles = member.Roles;
                Type = member.Type;
                UnlockedChallenges = member.UnlockedChallenges;
            }

            FirstName = member.FirstName;
            Id = member.Id;
            Name = member.Name;
            State = member.State;
            Uid = member.Uid;
            SolvedChallenges = member.SolvedChallenges ?? new string[] { };
        }

        public string Name { get; set; }
        public string Mail { get; set; }
        public string Id { get; set; }
        public string Uid { get; set; }
        public DateTime DateOfEntry { get; set; }
        public bool IsReviewer { get; set; }
        public int ReviewCounter { get; set; }
        public ReviewLevel ReviewLevel { get; set; }
        public int ReviewFrequency { get; set; }
        public DateTime LastReview { get; set; }
        public string[] ReviewLanguages { get; set; }
        public string FirstName { get; set; }
        public DateTime LastActivity { get; set; }
        public string[] Roles { get; set; }
        public string[] Groups { get; set; }
        public string[] UnlockedChallenges { get; set; }
        public string[] SolvedChallenges { get; set; }
        public string[] CanRate { get; set; }
        public string Password { get; set; }
        public DateTime? LastNotificationCheck { get; set; }
        public MemberType Type { get; set; }
        public MemberState State { get; set; }
        public bool IsAdmin { get; set; }
        public bool IsCreator { get; set; }
        public bool IsGroupAdmin { get; set; }
        public int AverageDifficultyLevel { get; set; }
    }
}
