using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using SubmissionEvaluation.Contracts.Data;
using SubmissionEvaluation.Providers.CryptographyProvider;
using SubmissionEvaluation.Contracts.Interfaces;
using SubmissionEvaluation.Server.Classes;
using SubmissionEvaluation.Server.Classes.Authentication;
using SubmissionEvaluation.Server.Classes.JekyllHandling;
using SubmissionEvaluation.Shared.Classes.Config;
using SubmissionEvaluation.Shared.Classes.Messages;
using SubmissionEvaluation.Shared.Models;
using SubmissionEvaluation.Shared.Models.Account;
using SubmissionEvaluation.Shared.Models.Members;
using SubmissionEvaluation.Shared.Models.Submission;
using Group = SubmissionEvaluation.Contracts.ClientPocos.Group;

namespace SubmissionEvaluation.Server.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Authorize(Policy = "IsChallengePlattformUser")]
    public class AccountController : ControllerBase
    {
        private readonly ILogger _logger;

        public AccountController(ILogger<AccountController> logger)
        {
            _logger = logger;
        }

        [HttpGet("Activities")]
        public IActionResult Activities()
        {
            var member = JekyllHandler.GetMemberForUser(User);
            if (member == null)
            {
                return Ok(new GenericModel {Message = ErrorMessages.IdError, HasError = true});
            }

            var history = JekyllHandler.Domain.Query.GetSubmitterHistory(member);
            var model = new MemberModel<ISubmission, IMember> {Id = member.Id, Name = member.Name, History = history.Entries, DateOfEntry = member.DateOfEntry};
            FillProfileMenuModel(model, member, ProfileMenuType.Activity);
            return Ok(model);
        }

        [HttpGet("Submissions")]
        public IActionResult Submissions()
        {
            var userId = User.Claims.First(x => x.Type == ClaimTypes.Sid).Value;
            var member = JekyllHandler.MemberProvider.GetMemberById(userId);
            var solvedList = JekyllHandler.Domain.Query.GetSubmitterSolvedList(member);
            var compilers = JekyllHandler.Domain.Query.GetCompilerNames();
            var Challenges = JekyllHandler.Domain.Query.GetAllChallenges(member).ToList();

            var model = new MemberSolvedModel<IChallenge>
            {
                Id = userId,
                Solved = solvedList.Solved,
                Compilers = compilers,
                Challenges = Challenges,
                Referer = "/Account/Challenges"
            };
            FillProfileMenuModel(model, member, ProfileMenuType.Submissions);
            return Ok(model);
        }

        [HttpPost("UpdateSettings")]
        public IActionResult UpdateSettings([FromBody] SettingsModel model)
        {
            if (!ModelState.IsValid)
            {
                return Ok(model);
            }

            if (Settings.Features.EnableSendMail)
            {
                if (string.IsNullOrWhiteSpace(model.Mail))
                {
                    model.Message = ErrorMessages.MissingMail;
                    model.HasError = true;
                    return Ok(model);
                }
            }
            else
            {
                if (string.IsNullOrWhiteSpace(model.Mail))
                {
                    model.Mail = "";
                }
            }

            var id = JekyllHandler.GetMemberForUser(User).Id;
            if (!Settings.Features.EnableLdap)
            {
                JekyllHandler.Domain.Interactions.ChangeUidForMember(id, model.Uid);
            }

            JekyllHandler.Domain.Interactions.UpdateMember(id, model.Name, model.Mail);
            model.HasSuccess = true;
            return Ok(model);
        }

        [HttpPost("ChangePassword")]
        public IActionResult ChangePassword(ChangePasswordModel model)
        {
            if (!ModelState.IsValid || Settings.Features.EnableLdap)
            {
                return Ok(model);
            }

            var auth = new LocalAuthentication(JekyllHandler.MemberProvider);
            var member = JekyllHandler.GetMemberForUser(User);
            var result = auth.VerifyUser(member.Uid, model.OldPassword);
            if (result != null)
            {
                var pwdHash = CryptographyProvider.CreateArgon2Password(model.Password);
                JekyllHandler.MemberProvider.UpdatePassword(member, pwdHash);
                model.HasSuccess = true;
                return Ok(model);
            }

            model.HasError = true;
            model.Message = ErrorMessages.WrongUserPassword;
            return Ok(model);
        }

        [HttpPost("Logout")]
        public IActionResult Logout()
        {
            HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return Ok(new GenericModel());
        }

        [AllowAnonymous]
        [HttpPost("Login")]
        public async Task<IActionResult> Login(LoginModel model, string returnUrl)
        {
            if (!ModelState.IsValid)
            {
                model.HasSuccess = false;
                return Ok(model);
            }

            var member = JekyllHandler.GetMemberByUid(model.Username);
            if (member?.State == MemberState.Pending && !member.IsAdmin)
            {
                model.HasError = true;
                model.HasSuccess = false;
                model.Message = ErrorMessages.ActivationNeeded;
                return Ok(model);
            }

            IUserAuthentication authentication;
            if (Settings.Features.EnableLdap && member?.Type != MemberType.Local)
            {
                authentication = new LdapAuthentication(_logger);
            }
            else
            {
                authentication = new LocalAuthentication(JekyllHandler.MemberProvider);
            }

            var attributeTable = authentication.VerifyUser(model.Username, model.Password);

            if (attributeTable != null)
            {
                try
                {
                    _logger.LogDebug("User {username} successfully authenticated via Directory Service", model.Username);
                    await new IdentityAuthenticator().WriteIdentityCookie(model.Username, attributeTable, HttpContext);

                    if (Url.IsLocalUrl(returnUrl))
                    {
                        return Redirect(returnUrl);
                    }

                    _logger.LogDebug("User Cookie set");
                    return Ok(new LoginModel {HasSuccess = true, Message = "Logged in", Username = model.Username});
                }
                catch (Exception)
                {
                    model.HasError = true;
                    model.HasSuccess = false;
                    model.Message = ErrorMessages.InvalidJekyllUser;
                    return Ok(model);
                }
            }

            model.HasError = true;
            model.HasSuccess = false;
            model.Message = ErrorMessages.WrongUserPassword;
            return Ok(model);
        }


        [AllowAnonymous]
        [HttpPost("Register")]
        public async Task<IActionResult> Register([FromBody] RegisterModel model)
        {
            if (!ModelState.IsValid)
            {
                return Ok(model);
            }
            var result = RegistrationHelper.registrationHelper.RegisterMember(model, HttpContext, _logger);
            if (!Settings.Features.EnableSendMail)
            {
                await Login(new LoginModel() { Password = model.Password, Username = model.Username }, "Account/View");
            }
            return Ok(result);

        }

        [AllowAnonymous]
        [HttpGet("loggedin")]
        public ActionResult<bool> IsLoggedIn()
        {
            return Ok(User.Identity.IsAuthenticated);
        }

        [AllowAnonymous]
        public IActionResult NotificationsChecked()
        {
            var member = JekyllHandler.GetMemberForUser(User);
            JekyllHandler.MemberProvider.UpdateLastNotificationCheck(member);
            ControllerContext.HttpContext.Response.StatusCode = 200;
            return new EmptyResult();
        }

        private SettingsModel BuildSettingsModel()
        {
            var member = JekyllHandler.GetMemberForUser(User);
            var groups = JekyllHandler.Domain.Query.GetAllGroups();
            var model = new SettingsModel();
            if (member != null)
            {
                model.Id = member.Id;
                model.Name = member.Name;
                model.Uid = member.Uid;
                model.Mail = member.Mail;
                model.ReviewCounter = member.ReviewCounter;
                model.ReviewLanguages = member.ReviewLanguages.ToList();
                model.Roles = User.Claims.Where(x => x.Type == ClaimTypes.Role).Select(x => x.Value).ToList();
                model.Groups = member.Groups.Select(x => new Group(groups.FirstOrDefault(g => g.Id == x))).ToList();
                model.CanChooseGroup = true;
                FillProfileMenuModel(model, member, ProfileMenuType.Overview);

                var achievements = JekyllHandler.Domain.Query.GetAwardsFor(member);
                model.Achievements = achievements.ToDictionary(x => x.Id, x => x);
            }

            return model;
        }

        internal static void FillProfileMenuModel(ProfileHeaderModel model, IMember member, ProfileMenuType currentMenu)
        {
            var challengeCount = JekyllHandler.Domain.Query.GetAvailableChallengeCount();
            var globalSubmitter = JekyllHandler.Domain.Query.GetGlobalSubmitter(member);
            model.TotalPoints = globalSubmitter.Points;
            model.SolvedChallenges = globalSubmitter.SolvedCount;
            model.TotalChallenges = challengeCount;
            model.Stars = globalSubmitter.Stars;
            model.DateOfEntry = member.DateOfEntry;
            model.CurrentMenu = currentMenu;
        }

        [AllowAnonymous]
        [HttpGet("getusersettings")]
        public ActionResult<SettingsModel> GetUserSettings()
        {
            var settingsmodel = BuildSettingsModel();
            return Ok(settingsmodel);
        }
        [HttpGet("Groups")]
        public IActionResult Groups()
        {
            var member = JekyllHandler.GetMemberForUser(User);
            var groups = JekyllHandler.Domain.Query.GetAllGroups();
            var subgroups = groups.SelectMany(x => x.SubGroups);
            var model = new SelectGroupModel
            {
                Groups = GetGroups(groups.Where(x=> !subgroups.Contains(x.Id)).ToList(), member)
            };
            model.HasError = model.Groups.All(x => !x.Selected);
            if (model.HasError)
            {
                model.Message = ErrorMessages.MustJoinGroup;
            }

            FillProfileMenuModel(model, member, ProfileMenuType.Overview);
            return Ok(model);
        }
        public static GroupInfo[] GetGroups(List<Contracts.Data.Group> groups, IMember member)
        {
            var groupInfos = new List<GroupInfo>();
            foreach(var group in groups)
            {
                if(group.IsSuperGroup)
                {
                    var subgroups = GetGroups(JekyllHandler.Domain.Query.GetAllGroups().Where(x => group.SubGroups.Contains(x.Id)).ToList(), member);
                    groupInfos.Add(new GroupInfo { Title = group.Title, Id = group.Id, Selected = member.Groups.Any(y=> y==group.Id), IsSuperGroup = group.IsSuperGroup, SubGroups = subgroups});
                } else
                {
                    groupInfos.Add(new GroupInfo { Title = group.Title, Id = group.Id, Selected = member.Groups.Any(y => y == group.Id), IsSuperGroup = group.IsSuperGroup });
                }
            }
            return groupInfos.ToArray();
        }
        public static string[] UpdateGroupsSelected(IEnumerable<GroupInfo> selectedGroups)
        {
            var result = new List<string>();
            foreach(var group in selectedGroups)
            {
                if(group.IsSuperGroup)
                {
                    result.AddRange(UpdateGroupsSelected(group.SubGroups));
                    result.Add(group.Id);
                } else
                {
                    result.Add(group.Id);
                }
            }
            return result.ToArray();
        }

        [HttpPost("Groups")]
        public IActionResult Groups(SelectGroupModel model)
        {
            var member = JekyllHandler.GetMemberForUser(User);
            var groupsSelected = UpdateGroupsSelected(model.Groups.Where(x => x.Selected));
            JekyllHandler.MemberProvider.UpdateGroups(member, groupsSelected);
            model.HasSuccess = true;
            return Ok(model);
        }

        [HttpGet("getsubmitterhistory")]
        public ActionResult<SubmitterHistory> GetSubmitterHistory()
        {
            var member = JekyllHandler.GetMemberForUser(User);
            var history = JekyllHandler.Domain.Query.GetSubmitterHistory(member);
            return Ok(history);
        }

        [HttpGet("getnotifications")]
        public ActionResult<NotificationModel> GetNotificationsForMember()
        {
            var member = JekyllHandler.GetMemberForUser(User);
            var userHistory = JekyllHandler.Domain.Query.GetSubmitterHistory(member);
            var lastNotifCheck = member.LastNotificationCheck;

            var count = member.LastNotificationCheck == null ? 0 : userHistory.Entries.Count(x => x.Date >= member.LastNotificationCheck);
            var notifications = GetAllNotificationsForUser(userHistory, lastNotifCheck);
            var model = new NotificationModel {Count = count > 0 ? $"({count})" : "", Notifications = notifications};
            return model;
        }

        [AllowAnonymous]
        [HttpGet("getSettings")]
        public IActionResult GetFeatureSettings()
        {
            return Ok(Settings.Features);
        }
        [AllowAnonymous]
        [HttpGet("getMailAddress")]
        public IActionResult GetMailAddress()
        {
            return Ok(Settings.Mail.HelpMailAddress);
        }
        private List<Notification> GetAllNotificationsForUser(SubmitterHistory userHistory, DateTime? memberLastNotificationCheck)
        {
            var notifications = new List<Notification>();
            try
            {
                IEnumerable<HistoryEntry> entries = userHistory.Entries;
                foreach (var historyEntry in entries.Take(7))
                {
                    var notification = new Notification
                    {
                        Id = string.Concat(historyEntry.Challenge, historyEntry.Type),
                        Date = historyEntry.Date,
                        IsNew = memberLastNotificationCheck == null || historyEntry.Date >= memberLastNotificationCheck,
                        Header = historyEntry.Challenge
                    };
                    notifications.Add(notification);
                    switch (historyEntry.Type)
                    {
                        case HistoryType.ChallengeSubmission:
                            notification.Content = $"Ergebnis: {historyEntry.Result}";
                            notification.SourceUrl = $"/Submission/Add/{historyEntry.Challenge}";
                            switch (historyEntry.Result)
                            {
                                case EvaluationResult.Succeeded:
                                case EvaluationResult.SucceededWithTimeout:
                                    notification.Image = "/images/rating_star0.png";
                                    break;
                                case EvaluationResult.Undefined:
                                    notification.Image = "/images/rating_missing.png";
                                    break;
                                default:
                                    notification.Image = "/images/rating_unsolved.png";
                                    break;
                            }

                            break;
                        case HistoryType.ReviewAvailable:
                            notification.Content = "Reviewanfrage";
                            notification.SourceUrl = "/Review/Overview";
                            notification.Image = "/images/rating_missing.png";
                            break;
                        case HistoryType.SubmissionRated:
                            notification.Content = $"Reviewergebnis: {historyEntry.Stars} Sterne";
                            notification.SourceUrl = $"/Submission/Add/{historyEntry.Challenge}";
                            notification.Image = $"/images/rating_star{historyEntry.Stars}.png";
                            break;
                        case HistoryType.SubmissionNowFailing:
                            notification.Content = "Einreichung läuft nicht mehr";
                            notification.SourceUrl = $"/Submission/Add/{historyEntry.Challenge}";
                            notification.Image = "/images/rating_unsolved.png";
                            break;
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, ex.Message);
            }

            return notifications;
        }

        [AllowAnonymous]
        [HttpGet("GetUser")]
        public ActionResult<ClaimsPrincipal> GetUser()
        {
            return Ok(User);
        }

        [AllowAnonymous]
        [HttpGet("GetCustomSettings")]
        public ActionResult<ClaimsPrincipal> GetCustomizationSettings()
        {
            var converted = new CustomizationSettingsClient
            {
                Results = Settings.Customization.Results,
                RatingMethods = WASMHelper.helper.RatingMethodsConverted,
                Achievements = Settings.Customization.Achievements.ToDictionary(entry => entry.Key, entry => entry.Value),
                Categories = Settings.Customization.Categories
            };
            return Ok(converted);
        }

        [HttpPut("setLastNotifCheck")]
        public ActionResult<bool> SetLastNotif()
        {
            var member = JekyllHandler.GetMemberForUser(User);
            JekyllHandler.MemberProvider.UpdateLastNotificationCheck(member);
            return Ok(true);
        }

        [HttpGet("PointsList")]
        public IActionResult PointsList()
        {
            var member = JekyllHandler.GetMemberForUser(User);
            if (member != null)
            {
                var points = JekyllHandler.Domain.Query.GetSubmitterRanklist(member).Submissions
                    .Where(x => x.Challenge != "ChallengeCreators" && x.Challenge != "Achievements" && x.Challenge != "Reviews").ToList();
                return Ok(points);
            }
            else
            {
                return NotFound(0);
            }
        }

        [AllowAnonymous]
        [HttpGet("GetRegistrationMessage")]
        public IActionResult GetRegistrationMessage()
        {
            return Ok(Settings.Authentication.RegistrationMessage);
        }
    }
}
