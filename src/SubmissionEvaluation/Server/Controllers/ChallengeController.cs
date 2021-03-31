using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Microsoft.Net.Http.Headers;
using SubmissionEvaluation.Classes.Config;
using SubmissionEvaluation.Contracts.Data;
using SubmissionEvaluation.Contracts.Exceptions;
using SubmissionEvaluation.Server.Classes;
using SubmissionEvaluation.Server.Classes.JekyllHandling;
using SubmissionEvaluation.Shared.Classes;
using SubmissionEvaluation.Shared.Classes.Config;
using SubmissionEvaluation.Shared.Classes.Messages;
using SubmissionEvaluation.Shared.Models;
using SubmissionEvaluation.Shared.Models.Admin;
using SubmissionEvaluation.Shared.Models.Challenge;
using SubmissionEvaluation.Shared.Models.Permissions;
using SubmissionEvaluation.Shared.Models.Shared;
using SubmissionEvaluation.Shared.Models.Test;
using File = SubmissionEvaluation.Shared.Models.Shared.File;
using Group = SubmissionEvaluation.Contracts.Data.Group;
using Member = SubmissionEvaluation.Contracts.ClientPocos.Member;

namespace SubmissionEvaluation.Server.Controllers
{
    [Authorize(Policy = "IsChallengePlattformUser")]
    [ApiController]
    [Route("api/[controller]")]
    public class ChallengeController : ControllerBase
    {
        private readonly ILogger _logger;

        public ChallengeController(ILogger<ChallengeController> logger)
        {
            _logger = logger;
        }

        [HttpGet("Category/{id}")]
        public IActionResult Category(string id)
        {
            var member = JekyllHandler.GetMemberForUser(User);
            var challenges = JekyllHandler.Domain.Query.GetAllChallenges(member).Where(x => x.IsAvailable && x.Category == id && !x.State.IsPartOfBundle);
            var bundles = JekyllHandler.Domain.Query.GetAllBundles(member).Where(x => !x.IsDraft && x.Category == id);

            var category = challenges.Select(x => new CategoryListEntryModel
            {
                IsBundle = false,
                IsPartOfBundle = x.State.IsPartOfBundle,
                Languages = x.Languages.Count > 0 ? string.Concat(",", x.Languages) : null,
                Id = x.Id,
                Title = x.Title,
                RatingMethod = WASMHelper.helper.ValueRatingMethod(x.RatingMethod),
                DifficultyRating = x.State.DifficultyRating,
                LearningFocus = x.LearningFocus
            }).Concat(bundles.Select(x => new CategoryListEntryModel
            {
                IsBundle = true,
                IsPartOfBundle = false,
                Languages = null, //stats["bundle_" + x.Name].Languages, // TODO: Languages for bundles missing
                Id = x.Id,
                Title = x.Title,
                DifficultyRating = x.State.DifficultyRating,
                LearningFocus = x.LearningFocus
            })).OrderBy(x => x.Title)
                .OrderBy(x => x.DifficultyRating > 0 ? x.DifficultyRating : 1000)
                .OrderByDescending(x => member.SolvedChallenges?.Contains(x.Id)).ToList();

            return Ok(new CategoryListModel<Member> {Category = Settings.Customization.Categories[id], Entries = category, Member = new Member(member)});
        }

        [HttpGet("GetAllChallengesToDoByMemberId/{id}")]
        [Authorize(Roles = "admin, groupAdmin")]
        public IActionResult GetAllChallengesToDoByMemberId([FromRoute] string id)
        {
            var model = new ChallengeOverviewModel();
            try
            {
                var member = JekyllHandler.GetMemberById(id);
                var challenges = JekyllHandler.Domain.Query.GetAllChallenges(member, false).Where(p => member.SolvedChallenges.All(p2 => p2 != p.Id));
                var bundles = JekyllHandler.Domain.Query.GetAllBundles(member);
                var groups = JekyllHandler.Domain.Query.GetAllGroups().ToList();
                IReadOnlyList<ChallengeModel> challenge = challenges.Select(x => ConvertChallengesToChallengeModel(x, bundles, groups)).ToList();

                model = new ChallengeOverviewModel
                {
                    Challenges = challenge.ToList(),
                    Categories = null,
                    RatingMethods = null
                };
            }
            catch (NullReferenceException e)
            {
                Console.WriteLine(e.StackTrace);
            }

            return Ok(model);
        }

        [HttpGet("GetAllChallengesForMember")]
        [HttpGet("GetAllChallengesForMember/{task}")]
        public IActionResult List([FromRoute] string task)
        {
            var model = new ChallengeOverviewModel();
            try
            {
                var member = JekyllHandler.GetMemberForUser(User);
                if (!JekyllHandler.CheckPermissions(Actions.VIEW, "Challenges", member))
                {
                    return Ok(new GenericModel() {HasError = true, Message = ErrorMessages.NoPermission});
                }

                var challenges = JekyllHandler.Domain.Query.GetAllChallenges(member, true);
                var bundles = JekyllHandler.Domain.Query.GetAllBundles(member);
                var groups = JekyllHandler.Domain.Query.GetAllGroups().ToList();
                IReadOnlyList<ChallengeModel> challenge = challenges.Select(x => ConvertChallengesToChallengeModel(x, bundles, groups)).ToList();

                model = new ChallengeOverviewModel
                {
                    Challenges = challenge.ToList(),
                    Categories = Settings.Customization.Categories,
                    RatingMethods = WASMHelper.helper.RatingMethodsConverted ??
                                    new Dictionary<string, RatingMethodConfig> {{string.Empty, new RatingMethodConfig()}}
                };
                if (task != null && !string.IsNullOrEmpty(task))
                {
                    switch (task)
                    {
                        case "EditSuccess":
                            model.Message = SuccessMessages.EditChallenge;
                            model.HasSuccess = true;
                            break;
                        case "DeleteSuccess":
                            model.Message = SuccessMessages.DeleteChallenge;
                            model.HasSuccess = true;
                            break;
                        case "PublishSuccess":
                            model.Message = SuccessMessages.PublishChallenge;
                            model.HasSuccess = true;
                            break;
                        case "NameChangeSuccess":
                            model.Message = SuccessMessages.ChangeChallengeId;
                            model.HasSuccess = true;
                            break;
                        default:
                            model.Message = ErrorMessages.GenericError;
                            model.HasError = true;
                            break;
                    }
                }

                if (!member.IsAdmin)
                {
                    model.Challenges = model.Challenges.Where(x => x.AuthorID == member.Id).ToList();
                }
            }
            catch (NullReferenceException e)
            {
                Console.WriteLine(e.StackTrace);
            }

            return Ok(model);
        }

        private static string CalculateDifficultyColor(int? difficultyRating)
        {
            if (difficultyRating == null)
            {
                return "#666";
            }

            var r = difficultyRating <= 50 ? 13 * difficultyRating / 50.0f : 13;
            var g = difficultyRating <= 50 ? 13 : 13 - 13 * (difficultyRating - 50) / 50.0f;
            var color = $"#{(int) r + 2:X}{(int) g + 2:X}0";
            return color;
        }

        [HttpGet("GetViewModel/{id}")]
        public ActionResult<ChallengeViewModel> GetViewModel([FromRoute] string id)
        {
            IChallenge challenge;
            var member = JekyllHandler.GetMemberForUser(User);
            try
            {
                challenge = JekyllHandler.Domain.Query.GetChallenge(member, id);
            }
            catch (IOException)
            {
                return Ok(new GenericModel {HasError = true, Message = ErrorMessages.IdError});
            }
            catch (DeserializationException)
            {
                return Ok(new GenericModel {HasError = true, Message = ErrorMessages.IdError});
            }
            catch (ChallengeLockedForUserException)
            {
                return Ok(new GenericModel {HasError = true, Message = ErrorMessages.ChallengeLockedForUser});
            }

            if (!HasUserPermissionToView(challenge))
            {
                return Ok(new GenericModel {HasError = true, Message = ErrorMessages.PreviousChallengesNotSolved});
            }

            var ranklist = JekyllHandler.Domain.Query.GetChallengeRanklist(challenge);

            var groupsOfMember = (member.Groups ?? new string[] { }).Select(x => JekyllHandler.Domain.Query.GetGroup(x));
            var challengesPerGroup = groupsOfMember.Select(x => new Tuple<string, List<string>>(x.Title, x.AvailableChallenges.Concat(x.ForcedChallenges).ToList()));
            var challengeViewModel = new ChallengeViewModel
            {
                Name = id,
                Description = challenge.Description,
                IsDraft = challenge.IsDraft,
                Source = challenge.Source,
                Title = challenge.Title,
                Category = challenge.Category,
                RatingMethod = Settings.Customization.RatingMethods[challenge.RatingMethod],
                DifficultyRating = challenge.State.DifficultyRating,
                DifficultyRatingColor = CalculateDifficultyColor(challenge.State.DifficultyRating),
                Author = new Member(JekyllHandler.MemberProvider.GetMemberById(challenge.AuthorID, true)),
                LastEditor = new Member(JekyllHandler.MemberProvider.GetMemberById(challenge.LastEditorID, true)),
                Features = challenge.State.Features?.Count > 0 ? string.Join(", ", challenge.State.Features) : null,
                Languages = string.Join(", ", challenge.Languages.Count > 0 ? challenge.Languages : JekyllHandler.Domain.Query.GetCompilerNames()),
                MaxEffort = challenge.State.MaxEffort,
                MinEffort = challenge.State.MinEffort,
                PublishDate = challenge.Date,
                Ranklist = ranklist,
                Points = JekyllHandler.Domain.Query.GetRatingPoints(challenge),
                Solved = member.SolvedChallenges.Contains(id),
                CanRate = member.SolvedChallenges.Contains(id) && member.CanRate.Contains(id),
                LearningFocus = challenge.LearningFocus,
                PartOfGroups = challengesPerGroup.Where(x => x.Item2.Contains(id)).Select(x => x.Item1).ToList()
            };

            if (JekyllHandler.Domain.Query.TryGetBundleForChallenge(member, challenge.Id, out var bundle))
            {
                challengeViewModel.BundleTitle = bundle.Title;
                challengeViewModel.Bundle = bundle.Id;

                var index = bundle.Challenges.IndexOf(challenge.Id);
                challengeViewModel.LastChallenge = index > 0 ? bundle.Challenges[index - 1] : null;
                challengeViewModel.NextChallenge = index < bundle.Challenges.Count - 1 ? bundle.Challenges[index + 1] : null;
            }


            return Ok(challengeViewModel);
        }

        [HttpGet("GetRatingMethodForChallenge/{id}")]
        [HttpGet("GetRatingMethodForChallenge")]
        public ActionResult<RatingMethod> GetRatingMethod([FromRoute] string id)
        {
            var member = JekyllHandler.GetMemberForUser(User);
            try
            {
                return Ok(JekyllHandler.Domain.Query.GetChallenge(member, id).RatingMethod);
            }
            catch (Exception)
            {
                return Ok(RatingMethod.Fixed);
            }
        }

        [HttpGet("GetAllChallengesAdminView")]
        [Authorize(Roles = "admin, groupAdmin")]
        public ActionResult GetAllChallengesAdminView()
        {
            var member = JekyllHandler.GetMemberForUser(User);
            if (JekyllHandler.CheckPermissions(Actions.VIEW, "ChallengeOverview", member))
            {
                var categoryStats = JekyllHandler.Domain.Query.GetCategoryStats(new Member() {IsAdmin = true, Id = "_-=42=-_"});
                var elements = categoryStats.ToDictionary(x => x.Key,
                    x => x.Value.Select(element => new CategoryListEntryExtendedModel
                    {
                        Activity = element.Activity,
                        Category = element.Category,
                        DifficultyRating = element.DifficultyRating,
                        Id = element.Id,
                        IsAvailable = element.IsAvailable,
                        IsBundle = element.IsBundle,
                        Languages = element.Languages != null ? string.Join(',', element.Languages) : null,
                        RatingMethod = WASMHelper.helper.ValueRatingMethod(element.RatingMethod),
                        Title = element.Title,
                        LearningFocus = element.LearningFocus
                    }).ToList());
                return Ok(elements);
            }
            else
            {
                return Forbid();
            }
        }

        private bool HasUserPermissionToView(IChallenge challenge)
        {
            if (User.IsInRole("admin"))
            {
                return true;
            }

            var member = JekyllHandler.GetMemberForUser(User);
            return !JekyllHandler.Domain.Query.TryGetBundleForChallenge(member, challenge.Id, out var bundle) ||
                   !bundle.HasPreviousChallengesCheck ||
                   JekyllHandler.Domain.Query.HasMemberSolvedAllPreviousChallengesInBundle(member, challenge);
        }

        private void SaveChallengeFiles(ExtendedChallengeModel model)
        {
            var member = JekyllHandler.GetMemberForUser(User);
            if (model.NewFiles == null || model.NewFiles.Count == 0)
            {
                return;
            }

            var challenge = JekyllHandler.Domain.Query.GetChallenge(member, model.Id);
            foreach (var newfile in model.NewFiles)
            {
                var toBeCopied = Encoding.UTF8.GetString(newfile.Content).Equals("Copy");
                switch (newfile.IsDelete)
                {
                    case false when !toBeCopied:
                        JekyllHandler.Domain.Interactions.AddAdditionalFileToChallenge(challenge, newfile.Name, newfile.Content);
                        break;
                    case false:
                    {
                        var toBeCopiedFile = model.Files.FirstOrDefault(x =>
                            x.OriginalName.Substring(x.OriginalName.IndexOf(Folder.pathSeperator, StringComparison.Ordinal))
                                .Equals(newfile.OriginalName.Substring(x.OriginalName.IndexOf(Folder.pathSeperator, StringComparison.Ordinal))));
                        if (toBeCopiedFile != null)
                        {
                            JekyllHandler.Domain.Interactions.CopyFileToOtherFile(toBeCopiedFile.OriginalName, newfile.OriginalName, challenge);
                        }

                        break;
                    }
                }
            }
        }


        [Authorize(Roles = "admin,creator")]
        [HttpGet("getDraftForUser")]
        public IActionResult Create()
        {
            var member = JekyllHandler.GetMemberForUser(User);
            if (JekyllHandler.CheckPermissions(Actions.CREATE, "Challenges", member))
            {
                var model = new ExtendedChallengeModel
                {
                    Author = User.Identity.Name,
                    AuthorID = member.Id,
                    LastEditor = User.Identity.Name,
                    LastEditorID = member.Id,
                    Date = DateTime.Today,
                    IsDraft = true,
                    Source = "none",
                    Files = new List<File>(),
                    IsGettingCreated = true,
                    Referer = "/Challenges",
                    RatingMethodInput = "Fixed",
                    Category = "Katas",
                    RatingMethods = Settings.Customization.RatingMethods.ToDictionary(p => WASMHelper.helper.Converter[p.Key], p => p.Value.Title),
                    Categories = Settings.Customization.Categories,
                    Tests = new List<ChallengeTest>(),
                    NewFiles = new List<DetailedInputFile>(),
                    Languages = new List<string>()
                };
                PopulateDropdowns(model);
                return Ok(model);
            }
            else
            {
                return Ok(new GenericModel {HasError = true, Message = ErrorMessages.NoPermission});
            }
        }

        [Authorize(Roles = "admin,creator")]
        [HttpPost("CreateChallenge")]
        public IActionResult Create(ExtendedChallengeModel model)
        {
            var member = JekyllHandler.GetMemberForUser(User);
            if (JekyllHandler.CheckPermissions(Actions.CREATE, "Challenges", member))
            {
                PopulateDropdowns(model);
                if (!ModelState.IsValid)
                {
                    Console.WriteLine(model.Message);
                    Console.WriteLine($"{model.RatingMethodInput}, {model.RatingMethod}, {model.SourceType}, {model.Category}");
                    return Ok(model);
                }

                try
                {
                    model.IsDraft = true;
                    JekyllHandler.Domain.Interactions.CreateNewChallenge(ConvertToChallengeProperties(model));
                    var challengeProps = JekyllHandler.Domain.Query.GetChallenge(member, model.Id);
                    JekyllHandler.Domain.Interactions.UpdateTests(challengeProps, new List<TestParameters>());

                    SaveChallengeFiles(model);
                    model.Message = SuccessMessages.CreateChallenge;
                    model.HasSuccess = true;
                    //TODO: In best case you can directly return the return of Edit, but there´s some converting issue.
                    Edit(model, "Save");
                    return Ok(model);
                }
                catch (IOException ex)
                {
                    _logger.LogWarning(ex.Message);
                    model.HasError = true;
                    model.Message = ErrorMessages.IdAlreadyExists;
                    return Ok(model);
                }
            }
            else
            {
                return Ok(new GenericModel {HasError = true, Message = ErrorMessages.NoPermission});
            }
        }

        [Authorize(Roles = "admin,creator")]
        [HttpPost("Copy")]
        public ActionResult<CopyModel> Copy(CopyModel model)
        {
            var member = JekyllHandler.GetMemberForUser(User);
            if (JekyllHandler.CheckPermissions(Actions.CREATE, "Challenges", member, Restriction.CHALLENGES, model.NameCopyFrom))
            {
                IChallenge challenge;
                if (string.IsNullOrEmpty(model.NameCopyTo))
                {
                    model.HasError = true;
                    model.Message = ErrorMessages.MissingId;
                }

                try
                {
                    challenge = JekyllHandler.Domain.Query.GetChallenge(member, model.NameCopyFrom, true);
                }
                catch (IOException)
                {
                    return Ok(new GenericModel {HasError = true, Message = ErrorMessages.IdError});
                }

                try
                {
                    JekyllHandler.Domain.Interactions.CopyChallenge(challenge, model.NameCopyTo, member.Id);
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

        [HttpGet("GetModel/{id}")]
        public ActionResult<ChallengeModel> GetModel([FromRoute] string id)
        {
            var member = JekyllHandler.GetMemberForUser(User);
            if (JekyllHandler.CheckPermissions(Actions.EDIT, "Challenges", member, Restriction.CHALLENGES, id) ||
                JekyllHandler.CheckPermissions(Actions.CREATE, "Challenges", member, Restriction.CHALLENGES, id))
            {
                ChallengeModel model;
                try
                {
                    if (id == null)
                    {
                        return null;
                    }

                    model = LoadChallengeModel(id);
                }
                catch (IOException)
                {
                    return Ok(new GenericModel {HasError = true, Message = ErrorMessages.IdError});
                }

                return Ok(model);
            }
            else
            {
                return Ok(new GenericModel {HasError = true, Message = ErrorMessages.NoPermission});
            }
        }

        [HttpGet]
        public FileContentResult Show(string challengeName, string fileName)
        {
            var (_, data, type, lastMod) = JekyllHandler.Domain.Query.GetChallengeAdditionalFile(challengeName, fileName);
            return File(data, type, lastMod, EntityTagHeaderValue.Any);
        }

        [Authorize(Roles = "admin")]
        [HttpGet("Download/{id}")]
        public IActionResult DownloadChallenge(string id)
        {
            var (_, data, _, _) = JekyllHandler.Domain.Query.GetChallengeZip(id);
            return Ok(new DownloadInfo(data));
        }

        [Authorize(Roles = "admin,creator")]
        [HttpPost("Edit/{command}")]
        public ActionResult<ExtendedChallengeModel> Edit([FromBody] ExtendedChallengeModel model, [FromRoute] string command)
        {
            ModelState.Clear();
            PopulateDropdowns(model);
            var member = JekyllHandler.GetMemberForUser(User);
            if (JekyllHandler.CheckPermissions(Actions.EDIT, "Challenges", member, Restriction.CHALLENGES, model.Id))
            {
                try
                {
                    model.LastEditorID = member.Id;
                    if (model.Files != null)
                    {
                        foreach (var challengeFile in model.Files)
                        {
                            if (challengeFile.IsDelete)
                            {
                                JekyllHandler.Domain.Interactions.RemoveAdditionalFileFromChallenge(model.Id, challengeFile.OriginalName);
                            }
                            else if (challengeFile.Name != challengeFile.OriginalName)
                            {
                                JekyllHandler.Domain.Interactions.ChangeAdditionalFilenameOfChallenge(model.Id, challengeFile.OriginalName, challengeFile.Name);
                            }
                        }
                    }

                    SaveChallengeFiles(model);
                    JekyllHandler.Domain.Interactions.EditChallenge(ConvertToChallengeProperties(model));
                    if (command != null && command.Equals("Publish") && model.IsDraft)
                    {
                        JekyllHandler.Domain.Interactions.PublishChallenge(model.Id, member);
                    }

                    model = LoadChallengeModel(model.Id);
                    model.Message = SuccessMessages.EditChallenge;
                    model.HasSuccess = true;
                    if (command != null && command.Equals("Publish"))
                    {
                        SchedulesAndTasks.Schedule_ChallengeStatsUpdate();
                    }

                    return Ok(model);
                }
                catch (IOException ioex)
                {
                    _logger.LogWarning("Could not edit challenge: " + ioex.Message);
                    model.HasError = true;
                    model.Message = ErrorMessages.GenericError;
                    return Ok(model);
                }
            }
            else
            {
                return Ok(new GenericModel {HasError = true, Message = ErrorMessages.NoPermission});
            }
        }

        [Authorize(Roles = "admin")]
        [HttpGet]
        public ActionResult<RenameModel> Rename(string id)
        {
            return Ok(new RenameModel {Name = id});
        }

        [Authorize(Roles = "admin")]
        [HttpPost("Rename")]
        public ActionResult<bool> Rename(RenameModel model)
        {
            var member = JekyllHandler.GetMemberForUser(User);
            var challenge = JekyllHandler.Domain.Query.GetChallenge(member, model.Name);
            var result = JekyllHandler.Domain.Interactions.ChangeChallengeId(challenge, model.NewName);
            return result;
        }

        [Authorize(Roles = "admin,creator")]
        [HttpDelete("DeleteChallenge/{id}")]
        public ActionResult<string> Delete(string id)
        {
            var member = JekyllHandler.GetMemberForUser(User);
            if (JekyllHandler.CheckPermissions(Actions.EDIT, "Challenges", member, Restriction.CHALLENGES, id))
            {
                var challenge = JekyllHandler.Domain.Query.GetChallenge(member, id);
                JekyllHandler.Domain.Interactions.DeleteChallenge(member, challenge);

                return Ok(SuccessMessages.DeleteChallenge);
            }
            else
            {
                return Ok(ErrorMessages.NoPermission);
            }
        }

        [Authorize(Roles = "admin, creator")]
        [HttpGet("GetConfirmActionModel/{id}/{activity}")]
        public ActionResult<ConfirmChallengeActionModel> ConfirmAction(string id, string activity)
        {
            var model = new ConfirmChallengeActionModel
            {
                Challenge = id, Activity = activity, ActivityMessage = activity == "Delete" ? $"Wollen sie die Challenge {id} wirklich löschen?" : ""
            };
            return Ok(model);
        }

        [Authorize(Roles = "admin,creator")]
        [HttpGet("RateUp/{id}")]
        [Produces("application/json")]
        public ActionResult<ChallengeModel> RateUp([FromRoute] string id)
        {
            Console.WriteLine("hit Rate Action");
            var member = JekyllHandler.GetMemberForUser(User);
            JekyllHandler.Domain.Interactions.IncreaseChallengeFeasibilityIndex(member, id);
            var model = LoadChallengeModel(id);
            return Ok(model);
        }

        [Authorize(Roles = "admin,creator")]
        [HttpGet("RateUp10/{id}")]
        public ActionResult<ChallengeModel> RateUp10([FromRoute] string id)
        {
            var member = JekyllHandler.GetMemberForUser(User);
            JekyllHandler.Domain.Interactions.IncreaseChallengeFeasibilityIndex(member, id, 10);
            return Ok(LoadChallengeModel(id));
        }

        [Authorize(Roles = "admin,creator")]
        [HttpGet("RateDown/{id}")]
        public ActionResult<ChallengeModel> RateDown([FromRoute] string id)
        {
            var member = JekyllHandler.GetMemberForUser(User);
            JekyllHandler.Domain.Interactions.DecreaseChallengeFeasibilityIndex(member, id);
            return LoadChallengeModel(id);
        }

        [Authorize(Roles = "admin,creator")]
        [HttpGet("RateDown10/{id}")]
        public ActionResult<ChallengeModel> RateDown10([FromRoute] string id)
        {
            var member = JekyllHandler.GetMemberForUser(User);
            JekyllHandler.Domain.Interactions.DecreaseChallengeFeasibilityIndex(member, id, 10);
            return LoadChallengeModel(id);
        }

        [HttpPost("RateChallenge/{id}")]
        public ActionResult<string> Rate([FromRoute] string id, [FromBody] RatingType rating)
        {
            var member = JekyllHandler.GetMemberForUser(User);
            JekyllHandler.Domain.Interactions.RateChallenge(member, id, rating);
            return Ok(SuccessMessages.GenericSuccess);
        }

        public ActionResult<UploadChallengeModel> UploadChallenge()
        {
            return Ok(new UploadChallengeModel());
        }

        [HttpPost("UploadChallenge")]
        public ActionResult<string> UploadChallenge(List<DetailedInputFile> files)
        {
            var member = JekyllHandler.GetMemberForUser(User);
            if (JekyllHandler.CheckPermissions(Actions.CREATE, "Challenges", member))
            {
                foreach (var newfile in files)
                {
                    if (newfile.Content.Length > 0)
                    {
                        JekyllHandler.Domain.Interactions.UploadChallenge(newfile.Content);
                    }
                }

                return Ok(SuccessMessages.EditChallenge);
            }
            else
            {
                return Ok(new GenericModel {HasError = true, Message = ErrorMessages.NoPermission});
            }
        }


        private ExtendedChallengeModel LoadChallengeModel(string challengeId)
        {
            var member = JekyllHandler.GetMemberForUser(User);
            var challenge = JekyllHandler.Domain.Query.GetChallenge(member, challengeId, true);

            var model = new ExtendedChallengeModel(challenge)
            {
                Author = JekyllHandler.MemberProvider.GetMemberById(challenge.AuthorID).Name,
                LastEditor = JekyllHandler.MemberProvider.GetMemberById(challenge.LastEditorID).Name,
                Files = challenge.AdditionalFiles.Select(x => new File {Name = x, OriginalName = x}).ToList(),
                Languages = challenge.Languages,
                IncludeTests = string.Join(Environment.NewLine, challenge.IncludeTests),
                DependsOn = string.Join(Environment.NewLine, challenge.DependsOn),
                RatingMethods = WASMHelper.helper.RatingMethodsConverted.ToDictionary(entry => entry.Key, entry => entry.Value.Title),
                Categories = Settings.Customization.Categories,
                Tests = TestChallengeHelper.GetTests(challenge, _logger),
                FeasibilityIndex = challenge.State.FeasibilityIndex,
                Points = JekyllHandler.Domain.Query.GetRatingPoints(challenge),
                RatingMethodInput = WASMHelper.helper.Converter[challenge.RatingMethod],
                //Fetching groups for certain challenge
                Groups = JekyllHandler.Domain.Query.GetAllGroups().Where(x => x.AvailableChallenges.Length == 0 || x.AvailableChallenges.Contains(challenge.Id))
                    .Select(x => new Contracts.ClientPocos.Group(x)).ToList(),
                LearningFocus = challenge.LearningFocus
            };
            PopulateDropdowns(model);
            model.Bundle = JekyllHandler.Domain.Query.GetAllBundles(member).FirstOrDefault(x => x.Challenges.Contains(model.Id))?.Title;

            if (model.Referer == null)
            {
                model.Referer = "/Challenges";
            }

            return model;
        }

        private void PopulateDropdowns(ChallengeModel model)
        {
            model.SourceTypes = new Dictionary<string, string> {{"own", "Eigene Idee"}, {"other", "Idee basiert auf anderer Quelle"}, {"\"\"", "!<empty>!"}};

            model.KnownLanguages = JekyllHandler.Domain.Query.GetCompilerNames().ToList();

            model.RatingMethods = Settings.Customization.RatingMethods.ToDictionary(p => WASMHelper.helper.Converter[p.Key], p => p.Value.Title);
            model.Categories = Settings.Customization.Categories;
        }

        private ChallengeModel ConvertChallengesToChallengeModel(IChallenge challenge, IEnumerable<IBundle> bundles, IReadOnlyList<Group> groups)
        {
            var author = JekyllHandler.MemberProvider.GetMemberById(challenge.AuthorID, true);
            var model = new ChallengeModel(challenge)
            {
                Author = author.Name,
                LastEditor = JekyllHandler.MemberProvider.GetMemberById(challenge.LastEditorID, true).Name,
                Groups =
                    groups.Where(x => x.ForcedChallenges.Contains(challenge.Id) || x.AvailableChallenges.Contains(challenge.Id))
                        .Select(x => new Contracts.ClientPocos.Group(x)).ToList(),
                Points = JekyllHandler.Domain.Query.GetRatingPoints(challenge),
                Bundle = bundles.FirstOrDefault(x => x.Challenges.Contains(challenge.Id))?.Title,
                RatingMethods = WASMHelper.helper.RatingMethodsConverted.ToDictionary(entry => entry.Key, entry => entry.Value.Title),
                RatingMethodInput = WASMHelper.helper.Converter[challenge.RatingMethod],
                Categories = Settings.Customization.Categories,
                LearningFocus = challenge.LearningFocus
            };
            return model;
        }

        private Challenge ConvertToChallengeProperties(ChallengeModel model)
        {
            var includeTest = Regex.Split(model.IncludeTests ?? "", "\r\n|\r|\n").Where(x => !string.IsNullOrWhiteSpace(x)).ToList();
            var dependsOn = Regex.Split(model.DependsOn ?? "", "\r\n|\r|\n").Where(x => !string.IsNullOrWhiteSpace(x)).ToList();

            return new Challenge
            {
                Id = model.Id,
                Title = model.Title,
                AuthorID = model.AuthorID,
                Date = model.Date,
                Category = model.Category,
                RatingMethod = model.RatingMethod,
                IsDraft = model.IsDraft,
                Description = model.Description,
                Source = model.Source,
                Languages = model.Languages,
                IncludeTests = includeTest,
                DependsOn = dependsOn,
                LearningFocus = model.LearningFocus
            };
        }
    }
}
