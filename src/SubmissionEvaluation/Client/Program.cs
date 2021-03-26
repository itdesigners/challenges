using System;
using System.Net.Http;
using System.Threading.Tasks;
using Blazored.LocalStorage;
using Blazored.Toast;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Microsoft.Extensions.DependencyInjection;
using SubmissionEvaluation.Client.Services;

namespace SubmissionEvaluation.Client
{
    public static class Program
    {
        public static async Task Main(string[] args)
        {
            var builder = WebAssemblyHostBuilder.CreateDefault(args);
            builder.RootComponents.Add<App>("#app");

            builder.Services.AddAuthorizationCore();
            builder.Services.AddBlazoredLocalStorage();
            builder.Services.AddBlazoredToast();
            builder.Services.AddSingleton<LoginService>();
            builder.Services.AddSingleton<MaintenanceService>();
            builder.Services.AddSingleton<FileExplorerDomain>();
            builder.Services.AddSingleton<ReviewSynchronizer>();
            builder.Services.AddSingleton<HelperService>();
            builder.Services.AddSingleton<PasswordRequirementsService>();
            builder.Services.AddScoped(sp => new HttpClient {BaseAddress = new Uri(builder.HostEnvironment.BaseAddress)});

            await builder.Build().RunAsync();
        }
    }
}
