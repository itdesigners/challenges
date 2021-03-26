using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using SubmissionEvaluation.Contracts.Data;
using SubmissionEvaluation.Contracts.Exceptions;
using SubmissionEvaluation.Server.Classes.JekyllHandling;
using SubmissionEvaluation.Shared.Classes.Config;
using SubmissionEvaluation.Shared.Classes.Messages;
using SubmissionEvaluation.Shared.Models;
using SubmissionEvaluation.Shared.Models.Bundle;
using SubmissionEvaluation.Shared.Models.Permissions;
using Challenge = SubmissionEvaluation.Contracts.ClientPocos.Challenge;
using Member = SubmissionEvaluation.Contracts.ClientPocos.Member;

namespace SubmissionEvaluation.Server.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Authorize(Policy = "IsChallengePlattformUser")]
    public class BundleController : ControllerBase
    {
        private readonly ILogger _logger;

        public BundleController(ILogger<BundleController> logger)
        {
            _logger = logger;
        }

        [Authorize(Roles = "admin, creator")]
        [HttpGet("List")]
        public IActionResult List()
        {
            var member = JekyllHandler.GetMemberForUser(User);
            if (JekyllHandler.CheckPermissions(Actions.VIEW, "Bundles", member))
            {
                var bundles = JekyllHandler.Domain.Query.GetAllBundles(member, true);
                var model = new BundleOverviewModel
                {
                    Categories = Settings.Customization.Categories,
                    Bundles = bundles.Where(x => member.IsAdmin || x.Author.Equals(member.Id)).Select(x => new BundleModel
                    {
                        Id = x.Id,
                        Title = x.Title,
                        Challenges = x.Challenges,
                        Category = x.Category,
                        AuthorId = x.Author,
                        Author = JekyllHandler.MemberProvider.GetMemberById(x.Author, true).Name,
                        IsDraft = x.IsDraft
                    }).ToList()
                };

                AccountController.FillProfileMenuModel(model, member, ProfileMenuType.Bundles);
                return Ok(model);
            }
            else
            {
                return Ok(new GenericModel {HasError = true, Message = ErrorMessages.NoPermission});
            }
        }

        [Authorize(Roles = "admin, creator")]
        [HttpGet("Create")]
        public IActionResult Create()
        {
            var member = JekyllHandler.GetMemberForUser(User);
            if (JekyllHandler.CheckPermissions(Actions.CREATE, "Bundles", member))
            {
                var model = new BundleModel {Author = member.Name, AuthorId = member.Id, IsDraft = true, Challenges = new List<string>()};
                FillLists(model);
                return Ok(model);
            }
            else
            {
                return Ok(new GenericModel {HasError = true, Message = ErrorMessages.NoPermission});
            }
        }

        [HttpPost("Create")]
        [Authorize(Roles = "admin, creator")]
        public IActionResult Create([FromBody] BundleModel model)
        {
            var member = JekyllHandler.GetMemberForUser(User);
            if (JekyllHandler.CheckPermissions(Actions.CREATE, "Bundles", member))
            {
                if (!ModelState.IsValid)
                {
                    model.HasError = true;
                    return Ok(model);
                }

                if (model.Challenges.Count == 0)
                {
                    model.HasError = true;
                    model.Message = ErrorMessages.HasNoChallenges;
                    return Ok(model);
                }

                if (model.Category == null)
                {
                    model.HasError = true;
                    model.Message = ErrorMessages.CategoryMissing;
                    return Ok(model);
                }

                try
                {
                    JekyllHandler.Domain.Interactions.CreateBundle(model.Id, model.Title, model.Description, model.AuthorId, model.Category, model.Challenges);
                    ModelState.Clear();
                    FillLists(model);
                    model.HasSuccess = true;
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
            else
            {
                return Ok(new GenericModel {HasError = true, Message = ErrorMessages.NoPermission});
            }
        }

        private void FillLists(BundleModel model)
        {
            var member = JekyllHandler.GetMemberForUser(User);
            var bundled = JekyllHandler.Domain.Query.GetAllBundles(member).SelectMany(x => x.Challenges).Concat(model.Challenges).ToList();
            model.AvailableChallenges = JekyllHandler.Domain.Query.GetAllChallenges(member).Where(x => !x.IsDraft && !bundled.Contains(x.Id))
                .Select(x => new Challenge(x)).ToList();
        }

        [Authorize(Roles = "admin, creator")]
        [HttpGet("Edit/{id}")]
        public IActionResult Edit(string id, bool created)
        {
            var member = JekyllHandler.GetMemberForUser(User);
            if (JekyllHandler.CheckPermissions(Actions.EDIT, "Bundles", member, Restriction.BUNDLES, id))
            {
                IBundle bundle;
                try
                {
                    bundle = JekyllHandler.Domain.Query.GetBundle(member, id);
                }
                catch (IOException)
                {
                    return Ok(new GenericModel {HasError = true, Message = ErrorMessages.IdError});
                }
                catch (DeserializationException)
                {
                    return Ok(new GenericModel {HasError = true, Message = ErrorMessages.IdError});
                }

                if (bundle.Author != member.Id && !member.IsAdmin)
                {
                    return Ok(new GenericModel {HasError = true, Message = ErrorMessages.NoPermission});
                }

                var model = new BundleModel
                {
                    Id = bundle.Id,
                    AuthorId = bundle.Author,
                    Author = JekyllHandler.MemberProvider.GetMemberById(bundle.Author)?.Name ?? bundle.Author,
                    Description = bundle.Description,
                    Title = bundle.Title,
                    IsDraft = bundle.IsDraft,
                    Category = bundle.Category,
                    Challenges = bundle.Challenges,
                    HasPreviousChallengesCheck = bundle.HasPreviousChallengesCheck
                };
                if (created)
                {
                    model.Message = SuccessMessages.CreateBundle;
                    model.HasSuccess = true;
                }

                FillLists(model);
                return Ok(model);
            }
            else
            {
                return Ok(new GenericModel {HasError = true, Message = ErrorMessages.NoPermission});
            }
        }

        [HttpGet("Get/{id}")]
        public ActionResult<BundleViewModel> GetBundleViewModel(string id)
        {
            var member = JekyllHandler.GetMemberForUser(User);

            IBundle bundle;
            try
            {
                bundle = JekyllHandler.Domain.Query.GetBundle(member, id);
            }
            catch (IOException)
            {
                return NoContent();
            }

            var model = new BundleViewModel
            {
                Id = bundle.Id,
                AuthorId = bundle.Author,
                Author = JekyllHandler.MemberProvider.GetMemberById(bundle.Author)?.Name ?? bundle.Author,
                Description = bundle.Description,
                Title = bundle.Title,
                IsDraft = bundle.IsDraft,
                Category = bundle.Category,
                Challenges = bundle.Challenges.Select(x => JekyllHandler.Domain.Query.GetChallenge(member, x)).Where(x => !x.IsDraft)
                    .Select(c => new Challenge(c)).ToList(),
                Member = new Member(member),
                Content = ""
            };
            return Ok(model);
        }

        [AllowAnonymous]
        [HttpGet("AllBundles")]
        public ActionResult<List<BundleModel>> AllBundles()
        {
            var member = JekyllHandler.GetMemberForUser(User);
            if (member == null)
            {
                return Ok(new List<BundleModel>());
            }

            IEnumerable<IBundle> bundles;
            try
            {
                bundles = JekyllHandler.Domain.Query.GetAllBundles(member);
            }
            catch (IOException)
            {
                return NoContent();
            }

            var model = bundles.Select(bundle => new BundleModel
            {
                Id = bundle.Id,
                AuthorId = bundle.Author,
                Author = JekyllHandler.MemberProvider.GetMemberById(bundle.Author)?.Name ?? bundle.Author,
                Description = bundle.Description,
                Title = bundle.Title,
                IsDraft = bundle.IsDraft,
                Category = bundle.Category,
                Challenges = bundle.Challenges,
                Content = ""
            }).ToList();
            return Ok(model);
        }

        [HttpGet("GetAllBundlesAdminView")]
        [Authorize(Roles = "admin, groupAdmin")]
        public IActionResult AllBundlesAdminView()
        {
            var member = JekyllHandler.GetMemberForUser(User);
            if (JekyllHandler.CheckPermissions(Actions.VIEW, "ChallengeOverview", member))
            {
                IEnumerable<IBundle> bundles;
                try
                {
                    bundles = JekyllHandler.Domain.Query.GetAllBundles(new Member() {IsAdmin = true, Id = "_-=42=-_"});
                }
                catch (IOException)
                {
                    return NoContent();
                }

                var model = bundles.Select(bundle => new BundleModel
                {
                    Id = bundle.Id,
                    AuthorId = bundle.Author,
                    Author = JekyllHandler.MemberProvider.GetMemberById(bundle.Author)?.Name ?? bundle.Author,
                    Description = bundle.Description,
                    Title = bundle.Title,
                    IsDraft = bundle.IsDraft,
                    Category = bundle.Category,
                    Challenges = bundle.Challenges,
                    Content = ""
                }).ToList();
                return Ok(model);
            }
            else
            {
                return Forbid();
            }
        }

        [HttpPost("Edit/{command}")]
        [Authorize(Roles = "admin, creator")]
        public IActionResult Edit([FromBody] BundleModel model, string command)
        {
            var member = JekyllHandler.GetMemberForUser(User);
            if (JekyllHandler.CheckPermissions(Actions.EDIT, "Bundles", member, Restriction.BUNDLES, model.Id))
            {
                if (!ModelState.IsValid)
                {
                    model.HasError = true;
                    return Ok(model);
                }

                try
                {
                    if (model.Challenges == null)
                    {
                        model.Challenges = new List<string>();
                    }

                    switch (command)
                    {
                        default:
                            JekyllHandler.Domain.Interactions.EditBundle(member,
                                new Bundle
                                {
                                    Id = model.Id,
                                    Title = model.Title,
                                    Description = model.Description,
                                    Author = model.AuthorId,
                                    Category = model.Category,
                                    Challenges = model.Challenges,
                                    IsDraft = model.IsDraft,
                                    HasPreviousChallengesCheck = model.HasPreviousChallengesCheck
                                });

                            if (command == "Publish")
                            {
                                JekyllHandler.Domain.Interactions.PublishBundle(member, model.Id);
                            }

                            model.HasError = false;
                            model.HasSuccess = true;
                            model.Message = SuccessMessages.EditBundle;
                            break;
                    }

                    ModelState.Clear();
                    FillLists(model);
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
            else
            {
                return Ok(new GenericModel {HasError = true, Message = ErrorMessages.NoPermission});
            }
        }
    }
}
