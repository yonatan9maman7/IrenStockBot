using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Mail;
using System.Net;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace IrenNotifier
{
    class Program
    {
        private static readonly string TelegramBotToken = Environment.GetEnvironmentVariable("TELEGRAM_TOKEN") ?? "";
        private static readonly string TelegramChatId = Environment.GetEnvironmentVariable("TELEGRAM_CHAT_ID") ?? "";
        private static readonly string EmailAddress = Environment.GetEnvironmentVariable("EMAIL_ADDRESS") ?? "";
        private static readonly string EmailAppPassword = Environment.GetEnvironmentVariable("EMAIL_APP_PASSWORD") ?? "";

        private static readonly string YahooRssUrl = "https://finance.yahoo.com/rss/headline?s=IREN";
        private static readonly string LastIdFile = Path.Combine(Directory.GetCurrentDirectory(), "last_id.txt");

        private static readonly string[] CriticalKeywords = { "offering", "8-k", "earnings", "guidance", "dilution" };

        static async Task Main(string[] args)
        {
            Console.WriteLine("Checking for new IREN updates...");

            try
            {
                using var client = new HttpClient();
                client.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36");
                var rssContent = await client.GetStringAsync(YahooRssUrl);
                var doc = XDocument.Parse(rssContent);

                var latestItem = doc.Descendants("item").FirstOrDefault();

                if (latestItem == null)
                {
                    Console.WriteLine("No items found in RSS feed.");
                    return;
                }

                string title = latestItem.Element("title")?.Value ?? "No Title";
                string link = latestItem.Element("link")?.Value ?? "";
                string pubDate = latestItem.Element("pubDate")?.Value ?? "";

                string lastLink = File.Exists(LastIdFile) ? File.ReadAllText(LastIdFile).Trim() : "";

                if (link == lastLink)
                {
                    Console.WriteLine("No new updates.");
                    return;
                }

                bool isCritical = CriticalKeywords.Any(k => title.ToLower().Contains(k));

                string message = $"{(isCritical ? "🚨 מדווח קריטי 🚨" : "📰 עדכון חדש")} מ-IREN!\n\n" +
                                 $"כותרת: {title}\n" +
                                 $"תאריך: {pubDate}\n" +
                                 $"קישור: {link}";

                await SendTelegramMessage(message);

                if (isCritical)
                {
                    SendEmail("IREN Critical Alert: " + title, message);
                }

                File.WriteAllText(LastIdFile, link);
                Console.WriteLine("Update sent and last_id.txt updated.");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error: {ex.Message}");
            }
        }

        static async Task SendTelegramMessage(string message)
        {
            string url = $"https://api.telegram.org/bot{TelegramBotToken}/sendMessage";

            using var client = new HttpClient();
            var content = new FormUrlEncodedContent(new[]
            {
                new KeyValuePair<string, string>("chat_id", TelegramChatId),
                new KeyValuePair<string, string>("text", message)
            });

            var response = await client.PostAsync(url, content);
            if (response.IsSuccessStatusCode)
                Console.WriteLine("Telegram message sent successfully.");
            else
                Console.WriteLine("Failed to send Telegram message.");
        }

        static void SendEmail(string subject, string body)
        {
            try
            {
                var smtpClient = new SmtpClient("smtp.gmail.com")
                {
                    Port = 587,
                    Credentials = new NetworkCredential(EmailAddress, EmailAppPassword),
                    EnableSsl = true,
                };

                var mailMessage = new MailMessage
                {
                    From = new MailAddress(EmailAddress),
                    Subject = subject,
                    Body = body,
                    IsBodyHtml = false,
                };
                mailMessage.To.Add(EmailAddress);

                smtpClient.Send(mailMessage);
                Console.WriteLine("Critical Email sent successfully.");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to send email: {ex.Message}");
            }
        }
    }
}
