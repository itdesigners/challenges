using System.IO;
using System.Net;
using System.Net.Mail;
using SubmissionEvaluation.Contracts.Data;
using SubmissionEvaluation.Contracts.Providers;

namespace SubmissionEvaluation.Providers.MailProvider
{
    public class SmtpProvider : ISmtpProvider
    {
        private readonly bool enableSendMail;
        private readonly ILog log;
        private readonly string mailAddress;
        private readonly string password;
        private readonly string serverAddress;
        private readonly string username;

        public SmtpProvider(ILog log, bool enableSendMail, string username, string password, string serverAddress, string mailAddress)
        {
            this.log = log;
            this.enableSendMail = enableSendMail;
            this.username = username;
            this.password = password;
            this.serverAddress = serverAddress;
            this.mailAddress = mailAddress;

            ServicePointManager.ServerCertificateValidationCallback = (sender, certificate, chain, sslPolicyErrors) => true;
        }

        public void SendMail(string address, string subject, string content, AttachmentProperties[] attachmentProperties = null, string cc = null,
            bool htmlBody = false)
        {
            log.Warning("Sende Mail an {to} mit {subject}", address, subject, content);
            if (!enableSendMail)
            {
                log.Warning("Versenden von Antwortmails ausgeschaltet. Inhalt:");
                log.Information(content);
                return;
            }

            using var client = new SmtpClient(serverAddress, 25) {Credentials = new NetworkCredential(username, password), EnableSsl = true};
            using var message = new MailMessage(mailAddress, address, subject, content) {IsBodyHtml = htmlBody};
            if (cc != null)
            {
                message.CC.Add(cc);
            }

            if (attachmentProperties != null)
            {
                foreach (var attachment in attachmentProperties)
                {
                    var ms = new MemoryStream(attachment.Data);
                    message.Attachments.Add(new Attachment(ms, attachment.Name));
                }
            }

            client.Send(message);
        }
    }
}
