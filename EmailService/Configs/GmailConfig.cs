namespace EmailService.Configs
{
    public class GmailConfig
    {
        public string SmtpServer { get; set; } = string.Empty;
        public int Port { get; set; }
        public string Username { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty;
        public int SmtpPort { get; set; }
        public string AppPassword { get; set; } = string.Empty;
    }
}
