using SubmissionEvaluation.Contracts.Data;

namespace SubmissionEvaluation.Contracts.Providers
{
    public interface ISmtpProvider
    {
        void SendMail(string address, string subject, string content, AttachmentProperties[] attachmentProperties = null, string cc = null,
            bool htmlBody = false);
    }
}
