using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using SubmissionEvaluation.Contracts.Data;
using SubmissionEvaluation.Server.Classes;
using SubmissionEvaluation.Server.Classes.JekyllHandling;
using SubmissionEvaluation.Shared.Classes.Messages;
using SubmissionEvaluation.Shared.Models;
using SubmissionEvaluation.Shared.Models.Challenge;
using SubmissionEvaluation.Shared.Models.Permissions;
using SubmissionEvaluation.Shared.Models.Test;
using File = SubmissionEvaluation.Shared.Models.Shared.File;
using Member = SubmissionEvaluation.Contracts.ClientPocos.Member;

namespace SubmissionEvaluation.Server.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Authorize(Policy = "IsChallengePlattformUser")]
    [Authorize(Roles = "admin,creator")]
    public class TestController : ControllerBase
    {
        private readonly ILogger _logger;

        public TestController(ILogger<TestController> logger)
        {
            _logger = logger;
        }

        [Authorize(Roles = "admin,creator")]
        [HttpGet("GetTestGeneratorModel/{challengeid}")]
        public IActionResult GetTestGeneratorModel([FromRoute] string challengeid)
        {
            var member = JekyllHandler.GetMemberForUser(User);
            if (JekyllHandler.CheckPermissions(Actions.CREATE, "Test", member, Restriction.CHALLENGES, challengeid))
            {
                var challenge = JekyllHandler.Domain.Query.GetChallenge(member, challengeid, true);
                var submissions = JekyllHandler.Domain.Query.GetAllSubmissionsFor(challenge);
                var submissionsWithMember = submissions.Select(x => new SubmissionModel<ISubmission, IMember>
                {
                    Member = JekyllHandler.MemberProvider.GetMemberById(x.MemberId), Submission = x
                }).ToList();
                var model = new TestGeneratorModel<ISubmission, IMember>
                {
                    AvailableSubmissions = submissionsWithMember,
                    ChallengeId = challengeid,
                    ChallengeName = challenge.Id,
                    Test = new ChallengeTest {Parameters = new List<string>()},
                    Referer = $"/Challenge/Edit/{challengeid}"
                };
                return Ok(model);
            }
            else
            {
                return Ok(new GenericModel {HasError = true, Message = ErrorMessages.NoPermission});
            }
        }

        [Authorize(Roles = "admin,creator")]
        [HttpPost("CreateTestWithTestGeneratorResult")]
        public IActionResult CreateTestWithTestGenerator(TestGeneratorModel<Result, Member> model)
        {
            var member = JekyllHandler.GetMemberForUser(User);
            if (JekyllHandler.CheckPermissions(Actions.CREATE, "Test", member, Restriction.CHALLENGES, model.ChallengeId))
            {
                var output = JekyllHandler.Domain.Interactions.RunTestGenerator(model.ChallengeId, model.SubmissionId, model.Test.Input,
                    model.Test.Parameters.ToArray());
                model.Test.Output = new Output {Content = output};
                model.Referer = $"/Test/CreateTestWithTestGenerator/{model.ChallengeId}?={model.SubmissionId}";
                return Ok(model);
            }
            else
            {
                return Ok(new GenericModel {HasError = true, Message = ErrorMessages.NoPermission});
            }
        }

        [Authorize(Roles = "admin,creator")]
        [HttpPost("createtest/{command}")]
        public IActionResult CreateTest(ChallengeTestCreateModel model, [FromRoute] string command)
        {
            var member = JekyllHandler.GetMemberForUser(User);
            if (JekyllHandler.CheckPermissions(Actions.CREATE, "Test", member))
            {
                var challengeProps = JekyllHandler.Domain.Query.GetChallenge(member, model.ChallengeId, true);
                model.IsUserAdmin = HttpContext.User.IsInRole("admin");
                model.InputFileEntries = challengeProps.AdditionalFiles;

                if (!ModelState.IsValid)
                {
                    model.HasError = true;
                    model.HasSuccess = false;
                    model.Message = "Model not valid";
                    return Ok(model);
                }

                var tests = JekyllHandler.Domain.Query.GetTests(challengeProps).ToList();
                var editTest = new TestParameters();
                MapValues(editTest, model);
                tests.Add(editTest);
                JekyllHandler.Domain.Interactions.UpdateTests(challengeProps, tests);
                model.HasError = false;
                model.HasSuccess = true;
                model.Message = "Test created successfully";
                model.Referer = $"/Challenges/Edit/{model.ChallengeId}";
                return Ok(model);
            }
            else
            {
                return Ok(new GenericModel {HasError = true, Message = ErrorMessages.NoPermission});
            }
        }

        [Authorize(Roles = "admin,creator")]
        [HttpGet("gettest/{id}/{testId}")]
        public IActionResult GetTest(string id, int testId)
        {
            var model = new ChallengeTestUpdateModel();
            if (id == null)
            {
                return Ok(new GenericModel {Message = ErrorMessages.IdError, HasError = true});
            }

            var member = JekyllHandler.GetMemberForUser(User);
            if (JekyllHandler.CheckPermissions(Actions.EDIT, "Test", member, Restriction.CHALLENGES, id))
            {
                IChallenge challenge;
                try
                {
                    challenge = JekyllHandler.Domain.Query.GetChallenge(member, id, true);
                }
                catch (IOException)
                {
                    model.HasSuccess = false;
                    model.HasError = true;
                    model.Message = ErrorMessages.IdError;
                    return Ok(model);
                }

                if (challenge.AuthorID != User.Claims.First(x => x.Type == ClaimTypes.Sid).Value && !User.IsInRole("admin"))
                {
                    model.HasSuccess = false;
                    model.HasError = true;
                    model.Message = ErrorMessages.NoPermission;
                    return Ok(model);
                }

                try
                {
                    var test = TestChallengeHelper.GetTests(challenge, _logger).ElementAt(testId);
                    var isUserAdmin = HttpContext.User.IsInRole("admin");
                    model = new ChallengeTestUpdateModel
                    {
                        ChallengeId = challenge.Id,
                        TestId = testId,
                        Test = test,
                        InputFileEntries = challenge.AdditionalFiles,
                        IsUserAdmin = isUserAdmin,
                        Referer = $"/Challenge/Edit/{id}",
                        HasError = false,
                        HasSuccess = true
                    };
                    return Ok(model);
                }
                catch (ArgumentOutOfRangeException)
                {
                    model.HasError = true;
                    model.Message = ErrorMessages.IdError;
                    return Ok(model);
                }
            }
            else
            {
                return Ok(new GenericModel {HasError = true, Message = ErrorMessages.NoPermission});
            }
        }

        [Authorize(Roles = "admin,creator")]
        [HttpPost("EditTest/{command}")]
        public IActionResult EditTest([FromBody] ChallengeTestUpdateModel model, [FromRoute] string command)
        {
            var member = JekyllHandler.GetMemberForUser(User);
            if (JekyllHandler.CheckPermissions(Actions.EDIT, "Test", member, Restriction.CHALLENGES, model.ChallengeId))
            {
                var challengeProps = JekyllHandler.Domain.Query.GetChallenge(member, model.ChallengeId, true);
                model.InputFileEntries = challengeProps.AdditionalFiles;
                model.IsUserAdmin = HttpContext.User.IsInRole("admin");

                if (!ModelState.IsValid)
                {
                    return Ok(model);
                }

                var tests = JekyllHandler.Domain.Query.GetTests(challengeProps);
                var editTest = tests.ElementAt(model.TestId);
                MapValues(editTest as TestParameters, model);
                JekyllHandler.Domain.Interactions.UpdateTests(challengeProps, tests);
                return Ok(model);
            }
            else
            {
                return Ok(new GenericModel {HasError = true, Message = ErrorMessages.NoPermission});
            }
        }

        [Authorize(Roles = "admin,creator")]
        [HttpGet("copy/{challengeid}/{testid}")]
        public IActionResult CopyTest([FromRoute] string challengeid, [FromRoute] int testid)
        {
            var member = JekyllHandler.GetMemberForUser(User);
            JekyllHandler.Domain.Interactions.DuplicateTest(member, challengeid, testid);
            return GetTest(challengeid, testid);
        }


        [HttpGet("delete/{challengeid}/{testindex}")]
        public IActionResult DeleteTest([FromRoute] string challengeid, [FromRoute] int testindex)
        {
            var member = JekyllHandler.GetMemberForUser(User);
            IChallenge challenge;
            var resultmodel = new GenericModel();
            try
            {
                challenge = JekyllHandler.Domain.Query.GetChallenge(member, challengeid, true);
            }
            catch (IOException)
            {
                resultmodel.HasError = true;
                resultmodel.HasSuccess = false;
                resultmodel.Message = ErrorMessages.IdError;
                return Ok(resultmodel);
            }

            if (challenge.AuthorID != User.Claims.First(x => x.Type == ClaimTypes.Sid).Value && !User.IsInRole("admin"))
            {
                resultmodel.Message = ErrorMessages.NoPermission;
                resultmodel.HasError = true;
                resultmodel.HasSuccess = false;
                return Ok(resultmodel);
            }

            var tests = JekyllHandler.Domain.Query.GetTests(challenge).ToList();
            tests.RemoveAt(testindex);
            JekyllHandler.Domain.Interactions.UpdateTests(challenge, tests);
            resultmodel.HasSuccess = true;
            resultmodel.HasError = false;
            resultmodel.Message = "Successfully deleted test";
            return Ok(resultmodel);
        }

        private void MapValues(TestParameters editTest, ChallengeTestCreateModel model)
        {
            editTest.Hint = model.Test.Hint;
            editTest.Id = model.Test.Id;
            if (!string.IsNullOrEmpty(model.Test.Input))
            {
                editTest.Input = new InputDefinition {Content = model.Test.Input};
            }
            else
            {
                editTest.Input = null;
            }

            editTest.Parameters = model.Test.Parameters?.ToArray();
            editTest.Timeout = model.Test.Timeout;
            if (!string.IsNullOrEmpty(model.Test.Output.Content))
            {
                editTest.ExpectedOutput = new OutputDefinition {Content = model.Test.Output.Content};
                if (model.Test.Output.Alternatives != null)
                {
                    editTest.ExpectedOutput.Alternatives = model.Test.Output.Alternatives.Where(x => !string.IsNullOrEmpty(x)).ToList();
                }
                else
                {
                    editTest.ExpectedOutput.Alternatives = null;
                }

                editTest.ExpectedOutput.Settings = ConvertToDomainSettings(model.Test.Output.CompareSettings);
            }
            else
            {
                editTest.ExpectedOutput = null;
            }

            if (!string.IsNullOrWhiteSpace(model.Test.OutputFile?.Name))
            {
                editTest.ExpectedOutputFile = new OutputFileDefinition
                {
                    Name = model.Test.OutputFile.Name,
                    Content = model.Test.OutputFile.Content,
                    Settings = ConvertToDomainSettings(model.Test.OutputFile.CompareSettings)
                };
            }
            else
            {
                editTest.ExpectedOutputFile = null;
            }

            if (model.Test.InputFiles?.Count > 0)
            {
                editTest.InputFiles = model.Test.InputFiles.Where(x => !x.IsDelete).Select(x => ConvertToFileDefinition(x)).ToList();
            }
            else
            {
                editTest.InputFiles = null;
            }

            if (!string.IsNullOrWhiteSpace(model.Test.CustomTestRunnerName))
            {
                editTest.CustomTestRunner = new CustomTestRunnerDefinition {Command = model.Test.CustomTestRunnerName};
            }
            else
            {
                editTest.CustomTestRunner = null;
            }
        }

        private static CompareSettings ConvertToDomainSettings(CompareSettingsModel settings)
        {
            return new CompareSettings
            {
                CompareMode = settings.CompareMode,
                Trim = settings.Trim,
                Whitespaces = settings.Whitespaces,
                UnifyFloatingNumbers = settings.IsUnifyFloatingNumbers,
                IncludeCase = settings.IsIncludeCase,
                KeepUmlauts = settings.IsKeepUmlauts,
                Threshold = settings.Threshold
            };
        }

        private static FileDefinition ConvertToFileDefinition(File file)
        {
            IFormatProvider culture = new CultureInfo("de-DE", true);
            DateTime? lastModified;
            try
            {
                lastModified = DateTime.Parse(file.LastModified, culture);
            }
            catch (Exception)
            {
                lastModified = null;
            }

            return new FileDefinition {Name = file.Name, ContentFile = file.OriginalName, LastModified = lastModified};
        }
    }
}
