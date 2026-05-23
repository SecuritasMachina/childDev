using System.Net;
using System.Net.Mail;

namespace ChildDev.Api.Services;

public class EmailService(IConfiguration config, ILogger<EmailService> logger)
{
    private readonly string? _host = config["CHILDDEV_SMTP_HOST"];
    private readonly int _port = int.TryParse(config["CHILDDEV_SMTP_PORT"], out var p) ? p : 587;
    private readonly string? _user = config["CHILDDEV_SMTP_USER"];
    private readonly string? _pass = config["CHILDDEV_SMTP_PASS"];
    private readonly string? _from = config["CHILDDEV_SMTP_FROM"];

    public async Task SendGoalCompleteAsync(string toEmail, string nickname, string goalText)
    {
        if (string.IsNullOrWhiteSpace(_host) || string.IsNullOrWhiteSpace(_user))
        {
            logger.LogDebug("SMTP not configured — skipping goal completion email.");
            return;
        }

        var fromAddress = _from ?? _user;
        var subject = $"You completed a goal, {nickname}!";
        var body = $"""
            Congratulations, {nickname}!

            You just completed your goal:

                "{goalText}"

            Amazing work! Keep setting goals and reaching them — you're building great habits.

            — LevelUp
            """;

        try
        {
#pragma warning disable SYSLIB0006
            using var client = new SmtpClient(_host, _port)
            {
                EnableSsl = true,
                Credentials = new NetworkCredential(_user, _pass)
            };
#pragma warning restore SYSLIB0006
            using var message = new MailMessage(fromAddress!, toEmail, subject, body);
            await client.SendMailAsync(message);
            // Log only first few chars to avoid leaking email addresses to logs
            logger.LogInformation("Goal completion email sent.");
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to send goal completion email.");
        }
    }
}
