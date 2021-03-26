using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Claims;
using Hangfire;
using Hangfire.Common;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using SubmissionEvaluation.Contracts.Data;
using SubmissionEvaluation.Contracts.Data.Review;
using SubmissionEvaluation.Contracts.Exceptions;
using SubmissionEvaluation.Server.Classes;
using SubmissionEvaluation.Server.Classes.JekyllHandling;
using SubmissionEvaluation.Server.Models;
using SubmissionEvaluation.Shared.Classes;
using SubmissionEvaluation.Shared.Classes.Messages;
using SubmissionEvaluation.Shared.Models.Permissions;
using SubmissionEvaluation.Shared.Models.Shared;
using SubmissionEvaluation.Shared.Models.Submission;
using Member = SubmissionEvaluation.Contracts.ClientPocos.Member;

namespace SubmissionEvaluation.Server.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Authorize(Policy = "IsChallengePlattformUser")]
    public class SubmissionController : ControllerBase
    {
        private readonly ILogger logger;

        public SubmissionController(ILogger<SubmissionController> logger)
        {
            this.logger = logger;
        }

        [HttpGet("Download/{challenge?}/{id?}")]
        public IActionResult Download([FromRoute] string challenge, [FromRoute] string id)
        {
            if (challenge == null || id == null)
            {
                return Ok(new DownloadInfo(ErrorMessages.GenericError));
            }

            var member = JekyllHandler.GetMemberForUser(User);
            try
            {
                var submission = JekyllHandler.Domain.Query.GetSubmission(challenge, id);
                var userId = User.Claims.First(x => x.Type == ClaimTypes.Sid).Value;
                if (submission.MemberId != userId)
                {
                    var props = JekyllHandler.Domain.Query.GetChallenge(member, challenge);
                    if (props.AuthorID != userId && !User.IsInRole("admin"))
                    {
                        return Ok(new DownloadInfo(ErrorMessages.NoPermission));
                    }
                }

                var data = JekyllHandler.Domain.Query.GetSourceForSubmission(submission);
                return Ok(new DownloadInfo(data));
            }
            catch
            {
                return Ok(new DownloadInfo(ErrorMessages.GenericError));
            }
        }

        [HttpGet("getRatingForSubmission/{challengeid}")]
        public IActionResult GetRatingForSubmission([FromRoute] string challengeid)
        {
            if (challengeid.StartsWith("tn_"))
            {
                return Ok(RatingMethod.Fixed);
            }

            return Ok(JekyllHandler.Domain.Query.GetChallenge(JekyllHandler.GetMemberForUser(User), challengeid).RatingMethod);
        }

        //Return all submissions for given challenge -> Task in challenge overview.
        [HttpGet("returnAllSubmissions/{id}/{selectedSubmission?}")]
        public IActionResult Task(string id, string selectedSubmission, bool isSuccess, string message)
        {
            var model = new SubmissionHistoryModel<IMember> {Referer = "/Challenges"};
            var member = JekyllHandler.GetMemberForUser(User);
            try
            {
                var props = JekyllHandler.Domain.Query.GetChallenge(member, id);
                if (!JekyllHandler.CheckPermissions(Actions.VIEW, "Submissions", member, Restriction.CHALLENGES, id))
                {
                    return Ok(new GenericModel {HasError = true, Message = ErrorMessages.NoPermission});
                }

                var challengeSubmissions = JekyllHandler.Domain.Query.GetAllSubmissionsFor(props);
                if (!member.IsCreator && !member.IsAdmin)
                {
                    var permissions = JekyllHandler.GetPermissionsForMember(member);
                    challengeSubmissions = challengeSubmissions.Where(x => permissions.MembersAccessible.Contains(x.MemberId)).ToList();
                }

                FillSubmissionHistoryModel(props, challengeSubmissions, selectedSubmission, model);

                if (selectedSubmission != null && challengeSubmissions.Any(x => x.SubmissionId == selectedSubmission))
                {
                    var submission = JekyllHandler.Domain.Query.GetSubmission(id, selectedSubmission);
                    if (submission != null)
                    {
                        model.ErrorDetails = JekyllHandler.Domain.Query.GetFailedChallengeSubmissionReport(submission);
                    }
                }

                if (message != null)
                {
                    model.HasSuccess = isSuccess;
                    model.Message = message;
                }
            }
            catch (Exception e)
            {
                JekyllHandler.Log.Error(e, "Failed to build modell");
            }

            return Ok(model);
        }

        [HttpGet("Add/{id}/{selectedSubmission?}/{message?}/{isSuccess?}")]
        public IActionResult Add([FromRoute] string id, [FromRoute] string message, [FromRoute] bool isSuccess, [FromRoute] string selectedSubmission)
        {
            if (string.IsNullOrWhiteSpace(id))
            {
                return Ok(new GenericModel {HasError = true, Message = ErrorMessages.GenericError});
            }

            var member = JekyllHandler.GetMemberForUser(User);
            try
            {
                if (!id.StartsWith("tn_") && !HasUserPermissionToView(JekyllHandler.Domain.Query.GetChallenge(member, id)))
                {
                    return Ok(new GenericModel {HasError = true, Message = ErrorMessages.PreviousChallengesNotSolved});
                }
            }
            catch (ChallengeLockedForUserException)
            {
                return Ok(new GenericModel {HasError = true, Message = ErrorMessages.ChallengeLockedForUser});
            }

            try
            {
                var model = BuildUploadModel(id, message, isSuccess, selectedSubmission);
                return Ok(model);
            }
            catch (Exception ex)
            {
                JekyllHandler.Log.Error(ex, "Seitenaufruf für SubmissionController/Add fehlgeschlagen");
                return Ok(BuildUploadModel(id, null, false, null));
            }
        }

        private bool HasUserPermissionToView(IChallenge challenge)
        {
            if (User.IsInRole("admin"))
            {
                return true;
            }

            var member = JekyllHandler.GetMemberForUser(User);
            if (JekyllHandler.Domain.Query.TryGetBundleForChallenge(member, challenge.Id, out var bundle) && bundle.HasPreviousChallengesCheck &&
                !JekyllHandler.Domain.Query.HasMemberSolvedAllPreviousChallengesInBundle(member, challenge))
            {
                return false;
            }

            return true;
        }

        [HttpPost("UploadJSON/{id}")]
        //Size was found by testing. Is roughly a 100MB Zip-File.
        [RequestSizeLimit(160000000)] //If changed, change ErrorMessage.UploadError translation
        public IActionResult UploadJson([FromRoute] string id, List<DetailedInputFile> files)
        {
            IActionResult GenerateAjaxResult(string message, bool isSuccess)
            {
                return Ok(new GenericModel {Message = message, HasSuccess = isSuccess, HasError = !isSuccess});
            }

            if (files == null || files.Count == 0)
            {
                return GenerateAjaxResult(ErrorMessages.InvalidFile, false);
            }

            if (string.IsNullOrEmpty(id))
            {
                return GenerateAjaxResult(ErrorMessages.IdError, false);
            }

            var jekyllId = User.Claims.First(x => x.Type == ClaimTypes.Sid).Value;

            var archiveExtensions = new[] {".7z", ".zip", ".rar"};
            var containsArchives = files.Any(x => archiveExtensions.Contains(Path.GetExtension(x.OriginalName).ToLower()));
            var member = JekyllHandler.MemberProvider.GetMemberById(jekyllId);

            byte[] challengeArchive;
            string filename;
            if (containsArchives)
            {
                var file = files.First(x => archiveExtensions.Contains(Path.GetExtension(x.OriginalName)?.ToLower()));
                filename = file.OriginalName;
                using (var ms = new MemoryStream())
                {
                    challengeArchive = file.Content;
                }

                challengeArchive = ArchiveHelper.ConvertToZip(challengeArchive, filename);
                filename = $"{Path.GetFileNameWithoutExtension(filename)}.zip";
            }
            else
            {
                filename = $"{id}.zip";
                challengeArchive = GenerateZipFile(files);
            }

            try
            {
                JekyllHandler.Domain.Interactions.AddChallengeSubmission(member, id, challengeArchive);
            }
            catch (CompilerException ex)
            {
                JekyllHandler.Log.Warning(ex.Message, "Fehler beim Upload");
                return GenerateAjaxResult(ErrorMessages.NoCompilerFound, false);
            }
            catch (Exception ex)
            {
                JekyllHandler.Log.Error(ex.Message, "Fehler beim Upload");
                return GenerateAjaxResult(ErrorMessages.GenericError, false);
            }

            return GenerateAjaxResult(SuccessMessages.CreateSubmission, true);
        }

        [HttpGet]
        public IActionResult UploadJson()
        {
            return Ok(new GenericModel {Content = "/Submission/Member"});
        }

        private static byte[] GenerateZipFile(IEnumerable<DetailedInputFile> files)
        {
            using (var ms = new MemoryStream())
            {
                using (var archive = new ZipArchive(ms, ZipArchiveMode.Create, true))
                {
                    foreach (var file in files)
                    {
                        var challengeFile = archive.CreateEntry(file.OriginalName);
                        using (var stream = challengeFile.Open())
                        {
                            stream.Write(file.Content);
                        }
                    }
                }

                return ms.ToArray();
            }
        }

        private UploadModel<ISubmission, IMember> BuildUploadModel(string id, string message, bool isSuccess, string submissionId)
        {
            var userId = User.Claims.First(x => x.Type == ClaimTypes.Sid).Value;
            var member = new Member(JekyllHandler.MemberProvider.GetMemberById(userId));
            return BuildSubmissionModel(id, message, isSuccess, submissionId, member);
        }

        private UploadModel<ISubmission, IMember> BuildSubmissionModel(string id, string message, bool isSuccess, string submissionId, IMember member)
        {
            var challenge = JekyllHandler.Domain.Query.GetChallenge(member, id);
            var submissions = JekyllHandler.Domain.Query.GetSubmissionsForUser(member, challenge);

            var model = new UploadModel<ISubmission, IMember> {Id = id, ChallengeTitle = challenge.Title, SubmissionId = submissionId};
            FillSubmissionHistoryModel(challenge, submissions, submissionId, model);

            if (submissionId != null)
            {
                var submission = submissions.FirstOrDefault(x => x.SubmissionId.Contains(submissionId));
                if (submission != null)
                {
                    model.SelectedSubmission = submission;
                    if (!submission.IsPassed)
                    {
                        model.ErrorDetails = JekyllHandler.Domain.Query.GetFailedChallengeSubmissionReport(submission);
                    }
                }
            }

            if (message != null)
            {
                if (isSuccess)
                {
                    model.HasSuccess = true;
                    model.Message = message;
                }
                else
                {
                    model.HasError = true;
                    model.Message = message;
                }
            }

            model.Referer = $"/Challenge/View/{id}";
            return model;
        }

        private static void FillSubmissionHistoryModel(IChallenge challenge, IReadOnlyList<ISubmission> submissions, string selectedSubmission,
            SubmissionHistoryModel<IMember> model)
        {
            string BuildStateText(ISubmission x)
            {
                if (x.EvaluationState == EvaluationState.NotEvaluated || x.EvaluationState == EvaluationState.RerunRequested)
                {
                    return MapQueuedState(x);
                }

                switch (x.EvaluationResult)
                {
                    case EvaluationResult.Undefined: return MapQueuedState(x);
                    case EvaluationResult.CompilationError:
                    case EvaluationResult.NotAllowedLanguage:
                        return "Kompilerfehler";
                    case EvaluationResult.Timeout: return "Timeout";
                    case EvaluationResult.TestsFailed: return x.TestsFailed + "/" + (x.TestsFailed + x.TestsPassed + x.TestsSkipped) + " fehlgeschlagen";
                    case EvaluationResult.SucceededWithTimeout:
                    case EvaluationResult.Succeeded:
                        return "Bestanden";
                    default: return "Unbekannter Fehler";
                }
            }

            model.Id = challenge.Id;
            model.AreSubmissionsSelectable = challenge.RatingMethod == RatingMethod.Exec_Time;
            model.Submissions = submissions.OrderByDescending(x => x.LastSubmissionDate).Select(x => new SubmissionResult<IMember>
            {
                Id = x.SubmissionId,
                SubmissionDate = x.LastSubmissionDate,
                State = BuildStateText(x),
                Language = x.Language,
                IsPassed = x.EvaluationState == EvaluationState.Evaluated && x.IsPassed,
                IsReviewed = x.ReviewState == ReviewStateType.Reviewed,
                IsQueued = x.EvaluationState != EvaluationState.Evaluated,
                EnableReport = !x.IsPassed && (x.EvaluationState == EvaluationState.Evaluated || x.EvaluationState == EvaluationState.Dead),
                EnableRerun = x.EvaluationState == EvaluationState.Evaluated,
                ReviewResult = x.ReviewRating,
                ExecutionDuration = x.ExecutionDuration,
                HasReviewData = x.HasReviewData,
                Member = new Member(JekyllHandler.MemberProvider.GetMemberById(x.MemberId))
            }).ToList();
            if (selectedSubmission != null)
            {
                var submission = submissions.FirstOrDefault(x => x.SubmissionId.Contains(selectedSubmission));
                if (submission != null && !submission.IsPassed)
                {
                    model.ErrorDetails = JekyllHandler.Domain.Query.GetFailedChallengeSubmissionReport(submission);
                }
            }
        }

        private static string MapQueuedState(ISubmission x)
        {
            List<string> FilterJobs(IEnumerable<Job> jobs)
            {
                return jobs.Select(y => y.Args[1] + "_" + y.Args[0]).ToList();
            }

            var monitoringApi = JobStorage.Current.GetMonitoringApi();
            var runningJobs = monitoringApi.ProcessingJobs(0, 10).Select(y => y.Value.Job)
                .Where(y => y.Method.Name == nameof(SchedulesAndTasks.Task_ProcessSubmission));
            var queuedJobs = monitoringApi.EnqueuedJobs("default", 0, 20).Select(y => y.Value.Job).Reverse()
                .Where(y => y.Method.Name == nameof(SchedulesAndTasks.Task_ProcessSubmission));
            var running = FilterJobs(runningJobs);
            var queued = FilterJobs(queuedJobs);

            var id = x.Challenge + "_" + x.SubmissionId;
            if (running.Contains(id))
            {
                return "Läuft";
            }

            var pos = queued.IndexOf(id) + 1;
            if (pos > 0)
            {
                return $"Wartet ({pos})";
            }

            return "Wartet (>20)";
        }

        [HttpGet("ViewSubmission/{challenge}/{id}")]
        public IActionResult View(string challenge, string id)
        {
            ModelState.Clear();
            if (challenge == null || id == null)
            {
                return Ok(new GenericModel {HasError = true, Message = ErrorMessages.IdError});
            }

            SubmissionViewModel model = null;
            var currentUser = JekyllHandler.GetMemberForUser(User);
            var submission = JekyllHandler.Domain.Query.GetSubmission(challenge, id);
            var memberId = User.Claims.First(x => x.Type == ClaimTypes.Sid).Value;
            if (submission.MemberId != memberId &&
                (!JekyllHandler.CheckPermissions(Actions.VIEW, "Submissions", currentUser, Restriction.MEMBERS, memberId) && !currentUser.IsCreator ||
                 !JekyllHandler.CheckPermissions(Actions.VIEW, "Submissions", currentUser, Restriction.CHALLENGES, challenge)))
            {
                return Ok(new GenericModel {HasError = true, Message = ErrorMessages.NoPermission});
            }

            model = BuildSubmissionViewModel(submission, challenge);

            return Ok(model);
        }

        [HttpPost("GetCurrentFile/{challenge}/{id?}")]
        public IActionResult View(string challenge, string id, [FromBody] string filePath)
        {
            ModelState.Clear();
            if (challenge == null || id == null)
            {
                return Ok(new GenericModel {HasError = true, Message = ErrorMessages.IdError});
            }

            SubmissionViewModel model = null;
            var submission = JekyllHandler.Domain.Query.GetSubmission(challenge, id);
            var currentUser = JekyllHandler.GetMemberForUser(User);
            if (submission.MemberId != User.Claims.First(x => x.Type == ClaimTypes.Sid).Value &&
                (!JekyllHandler.CheckPermissions(Actions.VIEW, "Submissions", currentUser, Restriction.MEMBERS, submission.MemberId) &&
                    !currentUser.IsCreator || !JekyllHandler.CheckPermissions(Actions.VIEW, "Submissions", currentUser, Restriction.CHALLENGES, challenge)))
            {
                return Ok(new GenericModel {HasError = true, Message = ErrorMessages.NoPermission});
            }

            model = BuildSubmissionViewModel(submission, challenge, filePath);

            return Ok(model);
        }

        private SubmissionViewModel BuildSubmissionViewModel(ISubmission submission, string challenge, string fileToRead = null)
        {
            var submissionFiles = JekyllHandler.Domain.Query.GetSubmissionRelativeFilesPath(submission).ToArray();
            if (fileToRead == null)
            {
                fileToRead = submissionFiles.FirstOrDefault();
            }

            var model = new SubmissionViewModel
            {
                ChallengeId = challenge,
                SubmissionFilePaths = submissionFiles,
                CurrentFile = fileToRead != null ? ReadSourceCodeForFile(submission, fileToRead) : null,
                Referer = $"/Submission/Add/{challenge}"
            };
            return model;
        }

        private SourceCodeFile ReadSourceCodeForFile(ISubmission submission, string relativeFilePath)
        {
            return new SourceCodeFile
            {
                FilePath = relativeFilePath,
                FileName = Path.GetFileName(relativeFilePath),
                FileContent = JekyllHandler.Domain.Query.GetSubmissionSourceCode(submission, relativeFilePath)
            };
        }

        [HttpPost("RerunAllSubmissions")]
        public IActionResult RerunAllSubmissions([FromBody] string id)
        {
            var member = JekyllHandler.GetMemberForUser(User);
            var props = JekyllHandler.Domain.Query.GetChallenge(member, id);
            if (!JekyllHandler.CheckPermissions(Actions.EDIT, "Submissions", member, Restriction.CHALLENGES, id))
            {
                return Ok(new GenericModel {HasError = true, Message = ErrorMessages.NoPermission});
            }

            JekyllHandler.Domain.Interactions.RerunChallengeSubmissions(id);
            return Task(id, null, true, SuccessMessages.RerunSubmission);
        }

        [HttpPost("RerunSubmission/{id}/{selectedsubmission}")]
        public IActionResult RerunSubmission(string id, string selectedsubmission, [FromBody] bool isTask)
        {
            if (id == null || selectedsubmission == null)
            {
                return Ok(new GenericModel {HasError = true, Message = ErrorMessages.IdError});
            }

            var member = JekyllHandler.GetMemberForUser(User);
            var props = JekyllHandler.Domain.Query.GetChallenge(member, id);
            var submission = JekyllHandler.Domain.Query.GetSubmission(id, selectedsubmission);
            var userId = User.Claims.First(x => x.Type == ClaimTypes.Sid).Value;
            if (submission.MemberId != userId && !JekyllHandler.CheckPermissions(Actions.EDIT, "Submissions", member, Restriction.CHALLENGES, props.Id))
            {
                return Ok(new GenericModel {HasError = true, Message = ErrorMessages.NoPermission});
            }

            if (JekyllHandler.CheckPermissions(Actions.EDIT, "Submissions", member, Restriction.MEMBERS, submission.MemberId))
            {
                JekyllHandler.Domain.Interactions.RerunSubmission(id, selectedsubmission);
            }

            if (isTask)
            {
                return Task(id, selectedsubmission, true, SuccessMessages.RerunSubmission);
            }

            return Add(id, selectedsubmission, true, SuccessMessages.RerunSubmission);
        }

        [HttpPost("ReportError/{id}")]
        public IActionResult Report([FromRoute] string id, [FromBody] string selectedsubmission)
        {
            if (id != null && selectedsubmission != null)
            {
                JekyllHandler.Domain.Interactions.ReportErrornuousSubmission(id, selectedsubmission);
            }

            return Add(id, null, true, SuccessMessages.ReportedErrornuousSubmission);
        }

        [HttpPost("RemoveDeadSubmissions")]
        public IActionResult RemoveDeadSubmissions([FromBody] string id)
        {
            var member = JekyllHandler.GetMemberForUser(User);
            if (JekyllHandler.CheckPermissions(Actions.EDIT, "Submissions", member, Restriction.CHALLENGES, id))
            {
                var challenge = JekyllHandler.Domain.Query.GetChallenge(member, id);
                JekyllHandler.Domain.Interactions.RemoveDeadSubmissions(member, challenge);
                return Task(id, null, true, SuccessMessages.DeadSubmissionsDeleted);
            }
            else
            {
                return Ok(new GenericModel {HasError = true, Message = ErrorMessages.NoPermission});
            }
        }

        //TODO?:Implement error checker on download in Submissions/Add
        //Check html of "Einreichung wurde gelöscht."
        //Implement review download and review view, after reviews are implemented.
    }
}
