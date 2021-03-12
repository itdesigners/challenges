using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Novell.Directory.Ldap;
using SubmissionEvaluation.Contracts.Data;
using SubmissionEvaluation.Providers.CryptographyProvider;
using SubmissionEvaluation.Server.Classes.Authentication;
using SubmissionEvaluation.Server.Classes.JekyllHandling;
using SubmissionEvaluation.Server.Classes.Messages;
using SubmissionEvaluation.Shared.Classes;
using SubmissionEvaluation.Shared.Models;
using SubmissionEvaluation.Shared.Models.Account;
using SubmissionEvaluation.Shared.Models.Admin;
using SubmissionEvaluation.Shared.Models.Help;
using Member = SubmissionEvaluation.Contracts.ClientPocos.Member;

namespace SubmissionEvaluation.Server.Controllers
{
    [Authorize(Policy = "IsChallengePlattformUser")]
    [Authorize(Roles = "admin")]
    [ApiController]
    [Route("api/[controller]")]
    public class AdminController : Controller
    {
        private readonly ILogger _logger;

        public AdminController(ILogger<AdminController> logger)
        {
            _logger = logger;
        }

        [HttpGet("Users")]
        public IActionResult Users()
        {
            return Ok(FetchBasicModel());
        }

        private AdminUserModel<Member> FetchBasicModel()
        {
            var members = JekyllHandler.MemberProvider.GetMembers();
            var memberShips = new List<GroupMemberships<Member>> { new GroupMemberships<Member>()
            {
                Members = members.Select(x => new Member(x)).ToList(),
                GroupName = string.Empty
            }};
            var model = new AdminUserModel<Member>
            {
                GroupMemberships = memberShips
            };
            return model;
        }

        [HttpPost("AddUser")]
        public IActionResult AddUser([FromBody] AddTempUserModel model)
        {
            var pwdHash = CryptographyProvider.CreateArgon2Password(model.Password);
            var member = JekyllHandler.Domain.Interactions.AddMember(model.Name, model.Mail, model.Name, true);
            JekyllHandler.MemberProvider.UpdatePassword(member, pwdHash);
            return Ok(new GenericModel {Message = SuccessMessages.GenericSuccess, HasSuccess = true});
        }

        [HttpGet("ConfirmActionModel/{id}/{activity}")]
        public IActionResult ConfirmAction([FromRoute] string id, [FromRoute] string activity)
        {
            var current = JekyllHandler.GetMemberForUser(User);
            var actionMessage = "?";
            IMember member;
            if (activity == "DeleteMember")
            {
                member = JekyllHandler.MemberProvider.GetMemberById(id);
                actionMessage = $"Wollen sie den Nutzer {member.Name} wirklich löschen?";
            }

            if (activity == "ResetMemberPassword")
            {
                member = JekyllHandler.MemberProvider.GetMemberById(id);
                actionMessage = $"Wollen sie das Password für Nutzer {member.Name} wirklich zurücksetzen?";
            }

            if (activity == "DeleteGroup")
            {
                actionMessage = $"Wollen sie die Gruppe {id} wirklich löschen?";
            }
            var model = new ConfirmActionModel {Id = id, Action = activity, ActionMessage = actionMessage};
            return Ok(model);
        }

        [HttpGet("ExportMembers")]
        public IActionResult ExportMembers()
        {
            try
            {
                var members = JekyllHandler.MemberProvider.GetMembers();
                var memberLines = members.Select(x => $"{x.Name};{x.Mail};{x.DateOfEntry.ToShortDateString()}");
                var header = "Name;Mail;Datum";
                var txtData = header + Environment.NewLine + string.Join(Environment.NewLine, memberLines);
                var data = Encoding.UTF8.GetBytes(txtData);
                return Ok(new DownloadInfo(data));
            }
            catch
            {
                return Ok(new DownloadInfo(ErrorMessages.GenericError));
            }
        }

        [HttpGet("ExportSolutions")]
        public IActionResult ExportSolutions()
        {
            try
            {
                var data = JekyllHandler.Domain.Query.ExportSolutionsAsZip();
                return Ok(new DownloadInfo(data));
            }
            catch (Exception ex)
            {
                JekyllHandler.Log.Error(ex, "Erstellen des Sources.zip fehlgeschlagen");
                return Ok(new DownloadInfo(ErrorMessages.GenericError));
            }
        }

        [HttpPost("DisableMaintenanceMode")]
        public IActionResult DisableMaintenanceMode()
        {
            JekyllHandler.Domain.Interactions.DisableMaintenanceMode();
            return Ok(JekyllHandler.Domain.IsMaintenanceMode);
        }

        [HttpPost("EnableMaintenanceMode")]
        public IActionResult EnableMaintenanceMode()
        {
            JekyllHandler.Domain.Interactions.EnableMaintenanceMode();
            return Ok(JekyllHandler.Domain.IsMaintenanceMode);
        }

        [HttpPost("DeleteMember")]
        public IActionResult DeleteMember([FromBody] string id)
        {
            JekyllHandler.Domain.Interactions.DeleteMember(id);
            return Ok(new GenericModel {HasSuccess = true, Message = SuccessMessages.GenericSuccess});
        }

        [HttpPost("ActivatePendingMember")]
        public IActionResult ActivatePendingMember([FromBody] string id)
        {
            var member = JekyllHandler.MemberProvider.GetMemberById(id);
            JekyllHandler.Domain.Interactions.ActivatePendingMember(member);
            var model = FetchBasicModel();
            model.Message = SuccessMessages.GenericSuccess;
            model.HasSuccess = true;
            return Ok(model);
        }

        [HttpPost("EditHelpPage")]
        public IActionResult EditHelpPage([FromBody] string path)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(path))
                {
                    return Ok(new GenericModel {HasError = true, Message = ErrorMessages.IdError});
                }

                if (path.Contains(".."))
                {
                    throw new Exception("Illegal path");
                }

                var helpPage = JekyllHandler.Domain.Query.GetHelpPage(path);
                var helpHierarchie = JekyllHandler.Domain.Query.GetHelpHierarchy();
                return Ok(new HelpModel
                {
                    Path = path,
                    Description = helpPage.Description,
                    Title = helpPage.Title,
                    Parent = helpPage.Parent,
                    Pages = helpHierarchie
                });
            }
            catch (Exception)
            {
                return Ok(new GenericModel {HasError = true, Message = ErrorMessages.IdError});
            }
        }

        [HttpPost("UpdateHelpPage")]
        public IActionResult EditHelpPage(HelpModel model)
        {
            model.Pages = JekyllHandler.Domain.Query.GetHelpHierarchy();
            if (!ModelState.IsValid)
            {
                return Ok(model);
            }

            try
            {
                JekyllHandler.Domain.Interactions.EditHelpPage(new HelpPage
                {
                    Path = model.Path, Title = model.Title, Parent = model.Parent, Description = model.Description
                });
                model.HasError = false;
                model.HasSuccess = true;
                model.Message = SuccessMessages.EditBundle;

                return Ok(model);
            }
            catch (IOException ex)
            {
                _logger.LogWarning(ex.Message);
                model.HasError = true;
                model.Message = ex.Message;
                return Ok(model);
            }
        }

        [HttpPost("ImpersonateMember")]
        public IActionResult ImpersonateMember([FromBody] string id)
        {
            var member = JekyllHandler.MemberProvider.GetMemberById(id);
            IdentityAuthenticator.LoginForMember(member, "Impersonate", HttpContext).WaitAndUnwrap(5000);
            return Ok(new GenericModel {Message = SuccessMessages.GenericSuccess, HasSuccess = true});
        }

        [HttpGet("AllPossibleMemberRoles/{id}")]
        public IActionResult ManageMemberRoles([FromRoute] string id)
        {
            var member = JekyllHandler.MemberProvider.GetMemberById(id);
            var groups = new[] {"admin", "creator", "groupAdmin"};
            var model = new ManageMemberRolesModel
            {
                Roles = groups.Select(x => new GroupInfo {Title = x, Id = x, Selected = member.Roles.Any(y => y == x)}).ToArray(),
                Name = member.Name,
                Id = id
            };

            return Ok(model);
        }

        [HttpPost("NewMemberRoles")]
        public IActionResult ManageMemberRoles([FromBody] ManageMemberRolesModel model)
        {
            var member = JekyllHandler.MemberProvider.GetMemberById(model.Id);
            JekyllHandler.MemberProvider.UpdateRoles(member, model.Roles.Where(x => x.Selected).Select(x => x.Id).ToArray());
            return Ok(new GenericModel {HasSuccess = true, Message = SuccessMessages.GenericSuccess});
        }

        [HttpGet("AllPossibleMemberGroups/{id}")]
        public IActionResult ManageMemberGroups([FromRoute] string id)
        {
            var member = JekyllHandler.MemberProvider.GetMemberById(id);
            var groups = JekyllHandler.Domain.Query.GetAllGroups();
            var subgroups = groups.SelectMany(x => x.SubGroups);
            var model = new ManageMemberGroupsModel
            {
                Groups = AccountController.GetGroups(groups.Where(x => !subgroups.Contains(x.Id)).ToList(), member),
                Name = member.Name,
                Id = id
            };

            return Ok(model);
        }

        [HttpPost("NewMemberGroups")]
        public IActionResult ManageMemberGroups([FromBody] ManageMemberGroupsModel model)
        {
            var member = JekyllHandler.MemberProvider.GetMemberById(model.Id);
            var groupsSelected = AccountController.UpdateGroupsSelected(model.Groups.Where(x => x.Selected));
            JekyllHandler.MemberProvider.UpdateGroups(member, groupsSelected);
            return Ok(new GenericModel {HasSuccess = true, Message = SuccessMessages.GenericSuccess});
        }

        [HttpPost("IncreaseMemberReviewLevel")]
        public IActionResult IncreaseMemberReviewLevel([FromBody] string id)
        {
            var member = JekyllHandler.MemberProvider.GetMemberById(id);
            JekyllHandler.Domain.Interactions.IncreaseReviewLevel(member);
            var model = FetchBasicModel();
            model.HasSuccess = true;
            model.Message = SuccessMessages.GenericSuccess;
            return Ok(model);
        }

        [HttpPost("ResetMemberPassword")]
        public IActionResult ResetMemberPassword([FromBody] string id)
        {
            var member = JekyllHandler.MemberProvider.GetMemberById(id);
            var newPwd = "$" + new Random().Next(1000000, int.MaxValue);
            var pwdHash = CryptographyProvider.CreateArgon2Password(newPwd);
            JekyllHandler.MemberProvider.UpdatePassword(member, pwdHash);
            var model = new ResetPasswordModel<IMember> {Member = member, Password = newPwd};
            return Ok(model);
        }
    }
}
