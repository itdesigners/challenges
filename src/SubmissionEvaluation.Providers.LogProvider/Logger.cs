using System;
using System.Collections.Generic;
using Serilog;
using Serilog.Events;
using Serilog.Sinks.RollingFileAlternate;
using Serilog.Sinks.SystemConsole.Themes;
using SubmissionEvaluation.Contracts.Data;
using SubmissionEvaluation.Contracts.Providers;

namespace SubmissionEvaluation.Providers.LogProvider
{
    public class Logger : ILog
    {
        private readonly MailLogger mailLogger;

        public Logger(string pathToLog)
        {
            mailLogger = new MailLogger();
            var logConfig = new LoggerConfiguration().MinimumLevel.Override("Microsoft", LogEventLevel.Warning).WriteTo.Console(
                outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss} [{Level}] {Message}{NewLine}{Exception}", theme: SystemConsoleTheme.Literate);
            if (pathToLog != null)
            {
                logConfig = logConfig.WriteTo.RollingFileAlternate(pathToLog, fileSizeLimitBytes: 1024 * 1024 * 100); // 100MB
            }

            logConfig = logConfig.WriteTo.Observers(x => x.Subscribe(mailLogger));

            Log.Logger = logConfig.CreateLogger();
        }

        public ISmtpProvider SmtpProvider { set => mailLogger.SmtpProvider = value; }

        public List<string> ReportMails { set => mailLogger.ReportMails = value; }

        public void Information(string msg, params object[] args)
        {
            Log.Information(msg, args);
        }

        public void Warning(string msg, params object[] args)
        {
            Log.Warning(msg, args);
        }

        public void Warning(Exception exception, string msg, params object[] args)
        {
            Log.Warning(exception, msg, args);
        }

        public void Error(string msg, params object[] args)
        {
            Log.Error(msg, args);
        }

        public void Error(Exception exception, string msg, params object[] args)
        {
            Log.Error(exception, msg, args);
        }

        public void Fatal(string msg, params object[] args)
        {
            Log.Fatal(msg, args);
        }

        public void Fatal(Exception exception, string msg, params object[] args)
        {
            Log.Fatal(exception, msg, args);
        }

        public void Activity(string userId, ActivityType type, string reference = null)
        {
            ActivityAdded?.Invoke(new Activity {Date = DateTime.Now, Type = type, UserId = userId, Reference = reference});
        }

        public event Action<Activity> ActivityAdded;

        public void SendDelayedErrorReports()
        {
            mailLogger.SendDelayedErrorReport();
        }
    }
}
