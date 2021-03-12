using System;
using System.Linq;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SubmissionEvaluation.Contracts.Data;
using SubmissionEvaluation.Server.Classes;
using SubmissionEvaluation.Server.Classes.JekyllHandling;
using SubmissionEvaluation.Shared.Models;
using SubmissionEvaluation.Shared.Models.Bundle;
using Challenge = SubmissionEvaluation.Contracts.ClientPocos.Challenge;
using Member = SubmissionEvaluation.Contracts.ClientPocos.Member;

namespace SubmissionEvaluation.Server.Controllers
{
    [ApiController]
    [AllowAnonymous]
    [Route("api/[controller]")]
    public class HomeController : ControllerBase
    {
        [HttpGet("IndexModel")]
        public ActionResult<IndexHomeModel> IndexModel()
        {
            var member = JekyllHandler.GetMemberForUser(User);
            var activities = JekyllHandler.Domain.Query.GetRecentActivities();
            if (member == null)
            {
                return new IndexHomeModel { Member = null };
            }
            var categoryStats = JekyllHandler.Domain.Query.GetCategoryStats(member);
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
            var bundleelements = elements.Values.SelectMany(x => x.Where(y => y.IsBundle));
            var bundles = bundleelements.Select(x =>
            {
                var bundle = JekyllHandler.Domain.Query.GetBundle(member, x.Id);
                return new BundleViewModel
                {
                    Id = bundle.Id,
                    AuthorId = bundle.Author,
                    Author = JekyllHandler.MemberProvider.GetMemberById(bundle.Author)?.Name ?? bundle.Author,
                    Description = bundle.Description,
                    Title = bundle.Title,
                    IsDraft = bundle.IsDraft,
                    Category = bundle.Category,
                    Challenges = bundle.Challenges.Select(c => JekyllHandler.Domain.Query.GetChallenge(member, c)).Where(c => !c.IsDraft)
                        .Select(c => new Challenge(c)).ToList(),
                    Member = new Member(member)
                };
            }).ToList();
            var indexmodel = new IndexHomeModel
            {
                Activities = activities,
                CategoryStats = elements,
                Member = new Member(member)
                //Bundles = bundles
            };

            return Ok(indexmodel);
        }

        [HttpGet("Error")]
        public IActionResult Error(string message)
        {
            if (message == null)
            {
                return Ok(new GenericModel());
            }

            return Ok(new GenericModel {HasError = true, Message = message});
        }

        [HttpGet("getGitVersionHash")]
        public IActionResult GetGitVersionHash()
        {
            return Ok(Domain.Domain.GetVersionHash());
        }

        [HttpGet("IsMaintenanceMode")]
        public IActionResult IsMaintenanceMode()
        {
            return Ok(JekyllHandler.Domain.IsMaintenanceMode);
        }
    }
}
