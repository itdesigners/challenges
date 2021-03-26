using System;
using System.Linq;
using System.Security.Claims;
using Hangfire;
using Hangfire.MemoryStorage;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Authorization;
using Microsoft.AspNetCore.ResponseCompression;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using SubmissionEvaluation.Server.Classes.JekyllHandling;

namespace SubmissionEvaluation.Server
{
    public class Startup
    {
        // This method gets called by the runtime. Use this method to add services to the container.
        // For more information on how to configure your application, visit https://go.microsoft.com/fwlink/?LinkID=398940
        public void ConfigureServices(IServiceCollection services)
        {
            services.AddAuthorization(options =>
            {
                options.AddPolicy("IsChallengePlattformUser", policyBuilder =>
                {
                    policyBuilder.RequireAuthenticatedUser().RequireClaim(ClaimTypes.Name).RequireClaim(ClaimTypes.GivenName)
                        .RequireClaim(ClaimTypes.NameIdentifier).Build();
                });
            });

            services.AddAuthentication(o =>
            {
                o.DefaultAuthenticateScheme = CookieAuthenticationDefaults.AuthenticationScheme;
                o.DefaultChallengeScheme = CookieAuthenticationDefaults.AuthenticationScheme;
                o.DefaultSignInScheme = CookieAuthenticationDefaults.AuthenticationScheme;
            }).AddCookie(o =>
            {
                o.Cookie.Name = "ITDChPlUserTicket";
                o.AccessDeniedPath = new PathString("/Home/Error");
                o.LoginPath = new PathString("/Account/Login");
                o.LogoutPath = new PathString("/Account/Logout");
            });

            services.AddMvc(config =>
            {
                var policy = new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build();
                config.Filters.Add(new AuthorizeFilter(policy));
            }).AddNewtonsoftJson();

            // TODO> required?
            services.AddResponseCompression(opts => { opts.MimeTypes = ResponseCompressionDefaults.MimeTypes.Concat(new[] {"application/octet-stream"}); });

            services.AddHangfire(x => { });
            services.AddHangfireServer(x => { x.WorkerCount = Environment.ProcessorCount; });
            services.AddDirectoryBrowser();
            services.AddControllersWithViews();
            services.AddRazorPages();
        }

        // This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
        public void Configure(IApplicationBuilder app, IWebHostEnvironment env)
        {
            GlobalConfiguration.Configuration.UseMemoryStorage(new MemoryStorageOptions {JobExpirationCheckInterval = TimeSpan.FromHours(1)});
            GlobalJobFilters.Filters.Add(new AutomaticRetryAttribute {Attempts = 0});
            RecurringJob.AddOrUpdate("Schedule_CheckForUnprocessedSubmissions", () => SchedulesAndTasks.Schedule_CheckForUnprocessedSubmissions(),
                Cron.Daily(21), TimeZoneInfo.Local);
            RecurringJob.AddOrUpdate("Schedule_PendingReviews", () => SchedulesAndTasks.Schedule_PendingReviews(), "00 05 * * 1-5", TimeZoneInfo.Local);
            RecurringJob.AddOrUpdate("Schedule_PromotionsAndMails", () => SchedulesAndTasks.Schedule_PromotionsAndMails(), "00 08 * * 1-5", TimeZoneInfo.Local);
            RecurringJob.AddOrUpdate("Schedule_ChallengeStatsUpdate", () => SchedulesAndTasks.Schedule_ChallengeStatsUpdate(), "00 06,12 * * *",
                TimeZoneInfo.Local);
            RecurringJob.AddOrUpdate("Schedule_DuplicateCheck", () => SchedulesAndTasks.Schedule_DuplicateCheck(), "00 03 * * *", TimeZoneInfo.Local);
            RecurringJob.AddOrUpdate("Schedule_RankingsUpdate", () => SchedulesAndTasks.Schedule_RankingsUpdate(), "0 */2 * * *", TimeZoneInfo.Local);
            RecurringJob.AddOrUpdate("Schedule_UnlockChallenges", () => SchedulesAndTasks.Schedule_UnlockChallenges(), "0 */2 * * *", TimeZoneInfo.Local);
            RecurringJob.AddOrUpdate("Schedule_AchievementsUpdate", () => SchedulesAndTasks.Schedule_AchievementsUpdate(), "0 */6 * * *", TimeZoneInfo.Local);

            app.UseResponseCompression();

            if (env.IsDevelopment())
            {
                app.UseDeveloperExceptionPage();
            }
            else
            {
                app.UseExceptionHandler("/Error");
                app.UseHsts();
                app.UseHttpsRedirection();
            }

            app.UseStaticFiles(new StaticFileOptions {ServeUnknownFileTypes = true});
            app.Use(async (context, next) =>
            {
                if (!context.Request.Path.StartsWithSegments(new PathString("/Account")) && !context.Request.Path.StartsWithSegments(new PathString("/Help")))
                {
                    var id = context.User.Claims.FirstOrDefault(x => x.Type == ClaimTypes.Sid)?.Value;
                    if (id != null)
                    {
                        var member = JekyllHandler.MemberProvider.GetMemberById(id);
                        if (member.Groups.Length == 0 && JekyllHandler.Domain.Query.GetAllGroups().Any())
                        {
                            context.Response.Redirect("/Account/Groups");
                        }
                    }
                }

                await next.Invoke();
            });

            app.UseRouting();
            app.UseAuthentication();
            app.UseAuthorization();
            app.UseHangfireDashboard("/hangfire", new DashboardOptions {Authorization = new[] {new MyAuthorizationFilter()}});
            app.UseBlazorFrameworkFiles();
            app.UseEndpoints(endpoints =>
            {
                endpoints.MapRazorPages();
                endpoints.MapDefaultControllerRoute();
                endpoints.MapControllerRoute("imageView", "Challenge/Edit/{challengeName}/{action=Show}/{FileName}", new {controller = "Challenge"});
                endpoints.MapControllerRoute("files", "files/{action}/{id}/{*path}", new {controller = "Download"});
                endpoints.MapControllerRoute("help", "Help/{*path}", new {controller = "Help", action = "ViewPage"});
                endpoints.MapHangfireDashboard();
                endpoints.MapFallbackToFile("index.html");
            });
        }
    }
}
