using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SubmissionEvaluation.Contracts.Data;
using SubmissionEvaluation.Contracts.Data.Review;
using SubmissionEvaluation.Server.Classes.JekyllHandling;
using SubmissionEvaluation.Server.Classes.Messages;
using SubmissionEvaluation.Shared.Classes;
using SubmissionEvaluation.Shared.Models;
using SubmissionEvaluation.Shared.Models.Permissions;
using SubmissionEvaluation.Shared.Models.Review;

namespace SubmissionEvaluation.Server.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Authorize(Policy = "IsChallengePlattformUser")]
    public class ReviewController : Controller
    {
        [HttpGet("GetReviewView/{challenge}/{id}")]
        public IActionResult View(string challenge, string id)
        {
            var submission = JekyllHandler.Domain.Query.GetSubmission(challenge, id);
            if (submission.HasReviewData == false)
            {
                return Ok(new GenericModel {HasError = true, Message = ErrorMessages.NoReview});
            }

            var userId = User.Claims.First(x => x.Type == ClaimTypes.Sid).Value;
            var member = JekyllHandler.GetMemberForUser(User);
            if (submission.MemberId != userId && !JekyllHandler.CheckPermissions(Actions.VIEW, "Member", member, Restriction.MEMBERS, submission.MemberId))
            {
                return Ok(new GenericModel {HasError = true, Message = ErrorMessages.NoPermission});
            }

            var data = JekyllHandler.Domain.Query.GetReviewData(submission);
            var reviewRating = JekyllHandler.Domain.Query.GetReviewRating(data);
            foreach (var rating in reviewRating.Childs)
            {
                rating.Description = MarkdownToHtml.Convert(rating.Description);
            }

            var pathToFiles = JekyllHandler.Domain.Query.GetSubmissionRelativeFilesPath(submission).ToList();
            var pathAndContent = pathToFiles.Select(p => new ReviewFile
            {
                Name = p, Content = JekyllHandler.Domain.Query.GetSubmissionSourceCode(submission, p)
            });
            var fileModel = ConvertToFileModel(pathToFiles);
            var sourceFiles = pathAndContent;
            var comments = ConvertComments(data);
            var template = JekyllHandler.Domain.Query.GetReviewTemplate(challenge);
            var model = new ReviewViewModel
            {
                ChallengeId = challenge,
                SubmissionId = id,
                ReviewData = data,
                ReviewRating = reviewRating,
                Stars = submission.ReviewRating,
                FileModel = fileModel,
                SourceFiles = sourceFiles,
                Comments = comments,
                Categories = template.Categories.Select(p => (p.Id, p.Title)).ToList()
            };
            return Ok(model);
        }

        private Dictionary<string, CommentModel[]> ConvertComments(ReviewData data)
        {
            var convertedComments = new Dictionary<string, CommentModel[]>();
            foreach (var comments in data.ResultComments)
            {
                var c = comments.Comments.Select(p => new CommentModel
                {
                    File = comments.FileName,
                    Id = p.Id,
                    Issue = "",
                    Length = p.Length,
                    Offset = p.Offset,
                    Text = p.Text
                }).ToArray();
                convertedComments.Add(comments.FileName, c);
            }

            return convertedComments;
        }

        [Authorize(Roles = "admin,groupAdmin,reviewer")]
        [HttpGet("GetReviewToolModel/{challenge}/{id}")]
        public IActionResult Tool(string challenge, string id)
        {
            var member = JekyllHandler.GetMemberForUser(User);
            if (JekyllHandler.CheckPermissions(Actions.EDIT, "Review", member, Restriction.CHALLENGES, challenge))
            {
                try
                {
                    if (challenge == null)
                    {
                        return Ok(new GenericModel {HasError = true, Message = ErrorMessages.IdError});
                    }

                    if (id == null)
                    {
                        return Ok(new GenericModel {HasError = true, Message = ErrorMessages.IdError});
                    }

                    var submission = JekyllHandler.Domain.Query.GetSubmission(challenge, id);
                    if (JekyllHandler.CheckPermissions(Actions.EDIT, "Review", member, Restriction.MEMBERS, submission.MemberId))
                    {
                        JekyllHandler.Domain.Query.StartReview(submission, member);
                        var model = new ReviewToolModel {Challenge = challenge, SubmissionId = id, IsAdmin = member.IsAdmin};
                        model = BuildReviewToolModel(model);
                        return Ok(model);
                    }
                    else
                    {
                        return Ok(new GenericModel {HasError = true, Message = ErrorMessages.NoPermission});
                    }
                }
                catch (Exception e)
                {
                    JekyllHandler.Log.Error(e, $"Fehler beim Starten eines Reviews. ChallengeId:{challenge}; SubmissionId:{id}");
                    return Ok(new GenericModel {HasError = true, Message = ErrorMessages.GenericError});
                }
            }
            else
            {
                return Ok(new GenericModel {HasError = true, Message = ErrorMessages.NoPermission});
            }
        }

        [Authorize(Roles = "admin,groupAdmin,reviewer")]
        [HttpPost("SubmitReview")]
        public IActionResult SubmitReview([FromBody] ReviewData model)
        {
            var member = JekyllHandler.GetMemberForUser(User);
            if (JekyllHandler.CheckPermissions(Actions.EDIT, "Review", member, Restriction.CHALLENGES, model.Challenge))
            {
                try
                {
                    var submission = JekyllHandler.Domain.Query.GetSubmission(model.Challenge, model.Id);
                    if (JekyllHandler.CheckPermissions(Actions.EDIT, "Review", member, Restriction.MEMBERS, submission.MemberId))
                    {
                        JekyllHandler.Domain.Interactions.AddReview(model);
                        return Ok(new GenericModel {HasSuccess = true, Message = SuccessMessages.SubmitReview});
                    }
                }
                catch (Exception)
                {
                    return Ok(new GenericModel {HasSuccess = false, HasError = true, Message = ErrorMessages.GenericError});
                }
            }

            return Ok(new GenericModel {HasError = true, Message = ErrorMessages.NoPermission});
        }

        private ReviewToolModel BuildReviewToolModel(ReviewToolModel model)
        {
            var submission = JekyllHandler.Domain.Query.GetSubmission(model.Challenge, model.SubmissionId);

            var pathToFiles = JekyllHandler.Domain.Query.GetSubmissionRelativeFilesPath(submission).ToList();
            var pathAndContent = pathToFiles.Select(p => new ReviewFile
            {
                Name = p, Content = JekyllHandler.Domain.Query.GetSubmissionSourceCode(submission, p)
            });
            var template = JekyllHandler.Domain.Query.GetReviewTemplate(model.Challenge);
            model.GuidedQuestions = template.GuidedQuestions;
            model.FileModel = ConvertToFileModel(pathToFiles);
            model.SourceFiles = pathAndContent.ToList();
            model.Categories = template.Categories.Select(p => (p.Id, p.Title)).ToList();
            return model;
        }

        private dynamic[] ConvertToFileModel(List<string> files)
        {
            //TODO CleanUp somehow
            var tree = new List<dynamic>();
            foreach (var file in files)
            {
                var splittedArray = file.Split("\\").Where(p => !string.IsNullOrEmpty(p)).ToArray();
                var currentDic = tree;
                for (var i = 0; i < splittedArray.Length - 1; i++)
                {
                    var entry = splittedArray[i];
                    var directoy = currentDic.ToList().Find(p => p.text == entry);
                    if (directoy == null)
                    {
                        directoy = new {text = entry, icon = "oi oi-folder", children = new List<dynamic>()};
                        currentDic.Add(directoy);
                    }

                    currentDic = directoy.children;
                }

                var fileObject = new {text = splittedArray.Last(), data = file, icon = "oi oi-file"};
                currentDic.Add(fileObject);
            }

            return tree.ToArray();
        }

        [Authorize(Roles = "reviewer,admin,groupAdmin")]
        [HttpPost("CancelReview")]
        public IActionResult Cancel(ReviewToolModel model)
        {
            var member = JekyllHandler.GetMemberForUser(User);
            if (JekyllHandler.CheckPermissions(Actions.EDIT, "Review", member, Restriction.CHALLENGES, model.Challenge))
            {
                var submission = JekyllHandler.Domain.Query.GetSubmission(model.Challenge, model.SubmissionId);
                if (JekyllHandler.CheckPermissions(Actions.EDIT, "Review", member, Restriction.MEMBERS, submission.MemberId))
                {
                    JekyllHandler.Domain.Interactions.CancelReview(submission);
                    return Ok(new GenericModel());
                }
            }

            return Ok(new GenericModel {HasError = true, Message = ErrorMessages.NoPermission});
        }

        [Authorize(Roles = "admin,reviewer,groupAdmin")]
        [HttpGet("GetSubmissionOverview")]
        public IActionResult Overview(bool? isSuccess, string message)
        {
            var member = JekyllHandler.GetMemberForUser(User);
            if (JekyllHandler.CheckPermissions(Actions.VIEW, "Review", member))
            {
                var model = new ReviewOverviewModel {ReviewableSubmissions = JekyllHandler.Domain.Query.GetAllReviewableSubmissions(member)};
                if (isSuccess.HasValue)
                {
                    if (isSuccess.Value)
                    {
                        model.HasSuccess = true;
                    }
                    else
                    {
                        model.HasError = true;
                    }
                }

                if (message != null)
                {
                    model.Message = message;
                }

                if (User.IsInRole("admin"))
                {
                    model.RunningReviews = JekyllHandler.Domain.Query.GetRunningReviews().Select(ConvertToRunningReviewModel);
                }
                else
                {
                    model.RunningReviews = JekyllHandler.Domain.Query.GetRunningReviews().Select(ConvertToRunningReviewModel)
                        .Where(x => x.ReviewerName == member.Name);
                }

                return Ok(model);
            }
            else
            {
                return Ok(new GenericModel() {HasError = true, Message = ErrorMessages.NoPermission});
            }
        }

        private RunningReviewModel ConvertToRunningReviewModel(Result submission)
        {
            var runTill = submission.ReviewDate.HasValue ? "Läuft ab am:" + submission.ReviewDate.Value.ToShortDateString() : "-";

            return new RunningReviewModel
            {
                Challenge = submission.Challenge,
                ReviewerName = JekyllHandler.MemberProvider.GetMemberById(submission.Reviewer).Name,
                Language = submission.Language,
                Status = runTill,
                Submission = submission.SubmissionId
            };
        }
    }
}
