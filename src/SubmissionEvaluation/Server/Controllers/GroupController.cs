using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using SubmissionEvaluation.Contracts.Data;
using SubmissionEvaluation.Server.Classes.JekyllHandling;
using SubmissionEvaluation.Shared.Classes;
using SubmissionEvaluation.Shared.Classes.Messages;
using SubmissionEvaluation.Shared.Models;
using SubmissionEvaluation.Shared.Models.Admin;
using SubmissionEvaluation.Shared.Models.Permissions;
using Challenge = SubmissionEvaluation.Contracts.ClientPocos.Challenge;
using Group = SubmissionEvaluation.Contracts.ClientPocos.Group;
using Member = SubmissionEvaluation.Contracts.ClientPocos.Member;

namespace SubmissionEvaluation.Server.Controllers
{
    [Authorize(Policy = "IsChallengePlattformUser")]
    [Authorize(Roles = "admin,groupAdmin")]
    [ApiController]
    [Route("api/[controller]")]
    public class GroupController : Controller
    {
        private readonly ILogger _logger;

        public GroupController(ILogger<GroupController> logger)
        {
            _logger = logger;
        }

        [HttpGet("Users")]
        public IActionResult Users()
        {
            var member = JekyllHandler.GetMemberForUser(User);
            if (JekyllHandler.CheckPermissions(Actions.VIEW, "Users", member))
            {
                var groups = JekyllHandler.GetPermissionsForMember(member).GroupsAccessible.Select(x => JekyllHandler.Domain.Query.GetGroup(x))
                    .Where(x => !x.IsSuperGroup).Select(x => x.Id);
                var membersContained = JekyllHandler.MemberProvider.GetMembers().Where(x => x.Groups.Intersect(groups).Any());
                var memberShips = groups.Select(x => new GroupMemberships<Member>
                {
                    Members = membersContained.Where(y => y.Groups.Contains(x)).Select(m => new Member(m, false)).ToList(), GroupName = x
                });
                var model = new AdminUserModel<Member>() {GroupMemberships = memberShips.ToList()};
                return Ok(model);
            }
            else
            {
                return Ok(new GenericModel {HasError = true, Message = ErrorMessages.NoPermission});
            }
        }

        [Authorize(Roles = "admin")]
        [HttpGet("CreateGroup")]
        public IActionResult CreateGroup()
        {
            var member = JekyllHandler.GetMemberForUser(User);
            var challenges = JekyllHandler.Domain.Query.GetAllChallenges(member).OrderBy(x => x.Id).ToList();
            var groupAdmins = JekyllHandler.MemberProvider.GetMembers().Where(x => x.IsAdmin || x.IsGroupAdmin);

            return Ok(new GroupModel<IChallenge, Member, Group>
            {
                SelectableAvailableChallenges = challenges.ToList(),
                SelectableForcedChallenges = challenges.ToList(),
                ForcedChallenges = new List<string>(),
                AvailableChallenges = new List<string>(),
                AdminsSelectable = groupAdmins.Select(x => new Member(x, false)).ToList(),
                GroupAdminsIds = new List<string>(),
                SelectableSubGroups = GetSelectableSubGroups(new string[] { })
            });
        }

        [Authorize(Roles = "admin")]
        [HttpPost("CreateGroup")]
        public IActionResult CreateGroup(GroupModel<Challenge, Member, Group> model)
        {
            var member = JekyllHandler.GetMemberForUser(User);
            if (model.ForcedChallenges == null)
            {
                model.ForcedChallenges = new List<string>();
            }

            if (model.AvailableChallenges == null)
            {
                model.AvailableChallenges = new List<string>();
            }

            JekyllHandler.Domain.Interactions.CreateGroup(member, model.Id, model.Title, model.GroupAdminsIds, model.IsSuperGroup, model.SubGroups.ToArray(),
                model.ForcedChallenges.ToArray(), model.AvailableChallenges.ToArray(), model.MaxUnlockedChallenges, model.RequiredPoints, model.StartDate,
                model.EndDate);

            var challenges = JekyllHandler.Domain.Query.GetAllChallenges(member).OrderBy(x => x.Id).ToList();
            model.SelectableAvailableChallenges = challenges.Where(x => !model.AvailableChallenges.Contains(x.Id)).Select(x => new Challenge(x)).ToList();
            model.SelectableForcedChallenges = challenges.Where(x => !model.ForcedChallenges.Contains(x.Id)).Select(x => new Challenge(x)).ToList();
            ModelState.Clear();
            model.HasSuccess = true;
            model.AdminsSelectable = JekyllHandler.MemberProvider.GetMembers().Where(x => x.IsAdmin || x.IsGroupAdmin).Select(x => new Member(x, false))
                .ToList();
            model.SelectableSubGroups = GetSelectableSubGroups(model.SubGroups.ToArray());
            return Ok(model);
        }

        [HttpPost("EditGroup")]
        public IActionResult EditGroup(GroupModel<Challenge, Member, Group> model)
        {
            var member = JekyllHandler.GetMemberForUser(User);
            if (JekyllHandler.CheckPermissions(Actions.EDIT, "Groups", member, Restriction.GROUPS, model.Id))
            {
                if (model.ForcedChallenges == null)
                {
                    model.ForcedChallenges = new List<string>();
                }

                if (model.AvailableChallenges == null)
                {
                    model.AvailableChallenges = new List<string>();
                }

                JekyllHandler.Domain.Interactions.EditGroup(member, model.Id, model.Title, model.GroupAdminsIds, model.IsSuperGroup, model.SubGroups.ToArray(),
                    model.ForcedChallenges.ToArray(), model.AvailableChallenges.ToArray(), model.MaxUnlockedChallenges, model.RequiredPoints, model.StartDate,
                    model.EndDate);

                var challenges = JekyllHandler.Domain.Query.GetAllChallenges(member).OrderBy(x => x.Id).ToList();
                model.SelectableAvailableChallenges = challenges.Select(x => new Challenge(x)).ToList();
                model.SelectableForcedChallenges = challenges.Select(x => new Challenge(x)).ToList();
                model.HasSuccess = true;
                ModelState.Clear();
                var groupAdmins = JekyllHandler.MemberProvider.GetMembers().Where(x => x.IsAdmin || x.IsGroupAdmin);
                model.AdminsSelectable = groupAdmins.Select(x => new Member(x, false)).ToList();
                model.SelectableSubGroups = GetSelectableSubGroups(model.SubGroups.ToArray());
                return Ok(model);
            }
            else
            {
                return Ok(new GenericModel {HasError = true, Message = ErrorMessages.NoPermission});
            }
        }

        [HttpGet("EditGroup/{id}")]
        public IActionResult EditGroup([FromRoute] string id)
        {
            var member = JekyllHandler.GetMemberForUser(User);
            var permissions = JekyllHandler.GetPermissionsForMember(member);
            if (JekyllHandler.CheckPermissions(Actions.EDIT, "Groups", member, Restriction.GROUPS, id))
            {
                var group = JekyllHandler.Domain.Query.GetGroup(id);
                var challenges = JekyllHandler.Domain.Query.GetAllChallenges(new Member() {IsAdmin = true}).OrderBy(x => x.Id).ToList();
                var groupAdmins = JekyllHandler.MemberProvider.GetMembers().Where(x => x.IsGroupAdmin);
                return Ok(new GroupModel<IChallenge, Member, Group>
                {
                    Id = id,
                    Title = group.Title,
                    SelectableAvailableChallenges = challenges.ToList(),
                    SelectableForcedChallenges = challenges.Where(x => !group.ForcedChallenges.Contains(x.Id)).ToList(),
                    ForcedChallenges = group.ForcedChallenges.ToList(),
                    AvailableChallenges = group.AvailableChallenges.OrderBy(x => x).ToList(),
                    MaxUnlockedChallenges = group.MaxUnlockedChallenges,
                    RequiredPoints = group.RequiredPoints,
                    StartDate = group.StartDate,
                    EndDate = group.EndDate,
                    AdminsSelectable = groupAdmins.Select(x => new Member(x, false)).ToList(),
                    GroupAdminsIds = group.GroupAdminIds ?? new List<string>(),
                    IsSuperGroup = group.IsSuperGroup,
                    SubGroups = group.SubGroups.ToList(),
                    SelectableSubGroups = GetSelectableSubGroups(group.SubGroups)
                });
            }
            else
            {
                return Ok(new GenericModel {HasError = true, Message = ErrorMessages.NoPermission});
            }
        }

        private List<Group> GetSelectableSubGroups(string[] AlreadySubGroups)
        {
            var subGroups = JekyllHandler.Domain.Query.GetAllGroups();
            var alreadySubGroup = subGroups.SelectMany(x => x.SubGroups).Distinct();
            // Remove last condition for multi-level hierarchies & adapt frontend
            subGroups = subGroups.Where(x => (!alreadySubGroup.Contains(x.Id) || AlreadySubGroups.Contains(x.Id)) && !x.IsSuperGroup);
            return subGroups.Select(x => new Group(x)).ToList();
        }

        [Authorize(Roles = "admin")]
        [HttpPost("RenameGroup")]
        public ActionResult<bool> RenameGroup([FromBody] RenameModel model)
        {
            var challenge = JekyllHandler.Domain.Query.GetGroup(model.Name);
            return JekyllHandler.Domain.Interactions.ChangeGroupId(challenge, model.NewName);
        }

        [Authorize(Roles = "admin")]
        [HttpPost("DeleteGroup")]
        public IActionResult DeleteGroup([FromBody] string id)
        {
            JekyllHandler.Domain.Interactions.DeleteGroup(id);
            return Ok(new GenericModel {HasSuccess = true, Message = SuccessMessages.GenericSuccess});
        }

        [HttpGet("Groups")]
        public IActionResult Groups()
        {
            var permissions = JekyllHandler.GetPermissionsForMember(JekyllHandler.GetMemberForUser(User));
            if (PermissionHelper.CheckPermissions(Actions.VIEW, "Groups", permissions))
            {
                var model = new AdminGroupsModel<IGroup>
                {
                    Groups = JekyllHandler.Domain.Query.GetAllGroups().Where(x => permissions.GroupsAccessible.Contains(x.Id) || permissions.isAdmin)
                };
                return Ok(model);
            }
            else
            {
                return Ok(new GenericModel {HasSuccess = true, Message = SuccessMessages.GenericSuccess});
            }
        }

        [HttpPost("Copy")]
        public IActionResult Copy(CopyModel model)
        {
            var member = JekyllHandler.GetMemberForUser(User);
            if (JekyllHandler.CheckPermissions(Actions.CREATE, "Groups", member, Restriction.GROUPS, model.NameCopyFrom))
            {
                IGroup group;
                if (string.IsNullOrEmpty(model.NameCopyTo))
                {
                    model.HasError = true;
                    model.Message = ErrorMessages.MissingId;
                }

                try
                {
                    group = JekyllHandler.Domain.Query.GetGroup(model.NameCopyFrom);
                }
                catch (IOException)
                {
                    return Ok(new GenericModel {HasError = true, Message = ErrorMessages.IdError});
                }

                try
                {
                    JekyllHandler.Domain.Interactions.CopyGroup(group, model.NameCopyTo);
                }
                catch (IOException ex)
                {
                    _logger.LogWarning(ex.Message);
                    model.HasError = true;
                    model.Message = ex.Message;
                    return Ok(model);
                }

                return Ok(model);
            }
            else
            {
                return Ok(new GenericModel {HasError = true, Message = ErrorMessages.NoPermission});
            }
        }
    }
}
