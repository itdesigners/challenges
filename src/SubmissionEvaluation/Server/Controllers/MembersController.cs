using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SubmissionEvaluation.Contracts.Data;
using SubmissionEvaluation.Contracts.Data.Ranklist;
using SubmissionEvaluation.Server.Classes.JekyllHandling;
using SubmissionEvaluation.Server.Classes.Messages;
using SubmissionEvaluation.Shared.Classes;
using SubmissionEvaluation.Shared.Classes.Config;
using SubmissionEvaluation.Shared.Models;
using SubmissionEvaluation.Shared.Models.Members;
using SubmissionEvaluation.Shared.Models.Permissions;
using Member = SubmissionEvaluation.Contracts.ClientPocos.Member;

namespace SubmissionEvaluation.Server.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Authorize(Policy = "IsChallengePlattformUser")]
    public class MembersController : ControllerBase
    {
        [HttpGet("Get/{id}")]
        public ActionResult<MemberModel<ISubmission, IMember>> Member([FromRoute] string id)
        {
            var member = JekyllHandler.MemberProvider.GetMemberById(id);
            if (member == null)
            {
                return Ok(new GenericModel {HasError = true, Message = ErrorMessages.IdError});
            }

            var currentUser = JekyllHandler.GetMemberForUser(User);
            if (Settings.Features.EnableRating || JekyllHandler.CheckPermissions(Actions.VIEW, "Member", currentUser, Restriction.MEMBERS, id))
            {
                var challengeCount = JekyllHandler.Domain.Query.GetAvailableChallengeCount();
                var globalSubmitter = JekyllHandler.Domain.Query.GetGlobalSubmitter(member);
                var achievements = JekyllHandler.Domain.Query.GetAwardsFor(member);
                var history = JekyllHandler.Domain.Query.GetSubmitterHistory(member);
                var points = JekyllHandler.Domain.Query.GetSubmitterRanklist(member);
                if (!currentUser.IsAdmin)
                {
                    var permissions = JekyllHandler.GetPermissionsForMember(currentUser);
                    history.Entries = history.Entries.Where(x =>
                        Settings.Features.EnableRating || permissions.ChallengesAccessible.Contains(x.Challenge) || currentUser.IsAdmin).ToList();
                    points.Submissions = points.Submissions.Where(x =>
                        Settings.Features.EnableRating || permissions.ChallengesAccessible.Contains(x.Challenge) || currentUser.IsAdmin).ToList();
                }

                var model = new MemberModel<ISubmission, IMember>
                {
                    Id = id,
                    Name = member.Name,
                    DateOfEntry = member.DateOfEntry,
                    GlobalScore = globalSubmitter.Points,
                    SolvedCount = globalSubmitter.SolvedCount,
                    TotalCount = challengeCount,
                    Achievements = achievements.ToDictionary(x => x.Id, x => x),
                    History = history.Entries,
                    Points = points.Submissions.Select(x => GetDuplicateInfo(member, x)).ToList()
                };

                return Ok(model);
            }
            else
            {
                return Ok(new GenericModel {HasError = true, Message = ErrorMessages.NoPermission});
            }
        }

        private PointsHoldTupel<ISubmission, IMember> GetDuplicateInfo(IMember member, SubmitterRankingEntry entry)
        {
            if (entry.DuplicateScore > 50)
            {
                var (submission, copySource) = JekyllHandler.Domain.Query.GetDuplicationSourceInfo(member, entry.Challenge);
                if (submission != null && copySource != null)
                {
                    submission.MemberName = JekyllHandler.MemberProvider.GetMemberById(submission.MemberId).Name;
                    var dupeAuthor = JekyllHandler.MemberProvider.GetMemberById(copySource.MemberId);
                    copySource.MemberName = dupeAuthor.Name;
                    return new PointsHoldTupel<ISubmission, IMember>(entry, submission, copySource, dupeAuthor);
                }
            }

            return new PointsHoldTupel<ISubmission, IMember>(entry, null, null, null);
        }

        [HttpGet("ShowList/{semester}/{order?}/{filterMode?}/{filterValue?}")]
        public IActionResult List(string order, string filterMode, string filterValue, bool semester)
        {
            var member = JekyllHandler.GetMemberForUser(User);
            var permissions = JekyllHandler.GetPermissionsForMember(member);
            if (!Settings.Features.EnableRating && !PermissionHelper.CheckPermissions(Actions.VIEW, "Members", permissions))
            {
                return Ok(new GenericModel {HasError = true, Message = ErrorMessages.GenericError});
            }

            var ranklist = semester ? JekyllHandler.Domain.Query.GetCurrentSemesterRanklist() : JekyllHandler.Domain.Query.GetGlobalRanklist();
            var members = JekyllHandler.MemberProvider.GetMembers()
                .Where(x => member.IsAdmin || Settings.Features.EnableRating || permissions.MembersAccessible.Contains(x.Id))
                .ToDictionary(x => x.Id, x => new Member(x));

            var submitters = ranklist.Submitters.Where(x => members.Any(y => x.Id.Equals(y.Key)));

            if (filterMode == "group")
            {
                submitters = submitters.Where(x => members[x.Id].Groups.Contains(filterValue));
            }

            if (Settings.Features.EnableRating || User.IsInRole("admin"))
            {
                switch (order)
                {
                    case "Name": break;
                    case "Group":
                        submitters = submitters.OrderByDescending(x => members[x.Id].Groups.FirstOrDefault());
                        break;
                    case "Solved":
                        submitters = submitters.OrderByDescending(x => x.SolvedCount);
                        break;
                    case "Challenges":
                        submitters = submitters.OrderByDescending(x => x.ChallengesCreated);
                        break;
                    case "Stars":
                        submitters = submitters.OrderByDescending(x => x.Stars);
                        break;
                    case "Points":
                        submitters = submitters.OrderByDescending(x => x.Points);
                        break;
                    case "Duplication":
                        submitters = submitters.OrderByDescending(x => x.DuplicationScore);
                        break;
                    default:
                        submitters = submitters.OrderByDescending(x => x.Points);
                        break;
                }
            }

            return Ok(new MemberListModel<Member>
            {
                CurrentSemester = FormatSemester(ranklist.CurrentSemester),
                Members = members,
                Submitters = submitters,
                IsSemesterRanking = semester,
                Order = order,
                FilterMode = filterMode,
                FilterValue = filterValue
            });
        }

        [AllowAnonymous]
        [HttpGet("ExistsAnyMember")]
        public IActionResult CheckForMembers()
        {
            return JekyllHandler.MemberProvider.GetMembers().Any() ? Ok(true) : Ok(false);
        }

        [AllowAnonymous]
        [HttpGet("GetAchievements")]
        public IActionResult GetAchievements()
        {
            return Ok(JekyllHandler.Domain.Achievements);
        }

        [AllowAnonymous]
        [HttpGet("Permissions")]
        public IActionResult GetPermissionsForMember()
        {
            var member = JekyllHandler.GetMemberForUser(User);
            if (member != null)
            {
                return Ok(JekyllHandler.GetPermissionsForMember(member));
            }
            else
            {
                return Ok(new Permissions());
            }
        }

        [HttpGet("PointsList/{id}")]
        public IActionResult PointsList([FromRoute] string id)
        {
            var member = JekyllHandler.MemberProvider.GetMemberById(id);
            var points = JekyllHandler.Domain.Query.GetSubmitterRanklist(member).Submissions
                .Where(x => x.Challenge != "ChallengeCreators" && x.Challenge != "Achievements" && x.Challenge != "Reviews").ToList()
                .Select(x => GetDuplicateInfo(member, x)).ToList();
            return Ok(points);
        }

        [HttpPost("PointsList")]
        public IActionResult PointsList([FromBody] List<string> memberIds)
        {
            var points = memberIds.AsParallel().Select(id =>
            {
                var member = JekyllHandler.MemberProvider.GetMemberById(id);
                return (id, JekyllHandler.Domain.Query.GetSubmitterRanklist(member).Submissions
                    .Where(x => x.Challenge != "ChallengeCreators" && x.Challenge != "Achievements" && x.Challenge != "Reviews")
                    .Select(x => GetDuplicateInfo(member, x)).ToList());
            });
            return Ok(points);
        }

        private string FormatSemester(Semester semester)
        {
            var semesterPeriod = semester.Period == SemesterPeriod.SS ? "SS" : "WS";
            return $"{semesterPeriod}{semester.Years}: {semester.FirstDay.ToShortDateString()} - {semester.LastDay.ToShortDateString()}";
        }
    }
}
