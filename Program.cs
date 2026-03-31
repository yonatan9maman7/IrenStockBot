using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Mail;
using System.Net;
using System.Text;
using System.Text.Json;
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
        private static readonly string GeminiApiKey = Environment.GetEnvironmentVariable("GEMINI_API_KEY") ?? "";

        private static readonly string YahooRssUrl = "https://finance.yahoo.com/rss/headline?s=IREN";
        private static readonly string LastIdFile = Path.Combine(Directory.GetCurrentDirectory(), "last_id.txt");

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
                string description = latestItem.Element("description")?.Value ?? "";

                string lastLink = File.Exists(LastIdFile) ? File.ReadAllText(LastIdFile).Trim() : "";

                if (link == lastLink)
                {
                    Console.WriteLine("No new updates.");
                    return;
                }

                string analysis = await AnalyzeWithGemini(title, description);
                Console.WriteLine($"Gemini analysis:\n{analysis}");

                string message = $"📰 IREN Update\n\n" +
                                 $"כותרת: {title}\n" +
                                 $"תאריך: {pubDate}\n\n" +
                                 $"ניתוח AI:\n{analysis}\n\n" +
                                 $"קישור: {link}";

                await SendTelegramMessage(message);

                bool shouldEmail = analysis.Contains("🚨")
                                || analysis.Contains("קנה")
                                || analysis.Contains("מכור");

                if (shouldEmail)
                {
                    SendEmail("IREN Alert: " + title, message);
                }

                File.WriteAllText(LastIdFile, link);
                Console.WriteLine("Update sent and last_id.txt updated.");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error: {ex.Message}");
            }
        }

        static async Task<string> AnalyzeWithGemini(string title, string description)
        {
            string prompt = "Analyze this news article about Iris Energy (IREN). " +
                "Provide a 2-3 sentence summary STRICTLY IN HEBREW. " +
                "State if the sentiment is positive, negative, or neutral. " +
                "Finally, give a clear short recommendation in Hebrew: Hold (החזק), Buy (קנה), or Sell (מכור). " +
                "If the news is extremely critical (e.g., dilution, earnings report, major contract), include a 🚨 emoji at the beginning.\n\n" +
                $"Title: {title}\nDescription: {description}";

            var attempts = new[]
            {
                (model: "gemini-2.5-flash", delay: 0),
                (model: "gemini-2.0-flash", delay: 0),
                (model: "gemini-2.0-flash", delay: 5000),
            };

            using var client = new HttpClient();

            foreach (var (model, delay) in attempts)
            {
                try
                {
                    if (delay > 0)
                        await Task.Delay(delay);

                    Console.WriteLine($"Trying model: {model}...");

                    string url = $"https://generativelanguage.googleapis.com/v1beta/models/{model}:generateContent?key={GeminiApiKey}";

                    var requestBody = new
                    {
                        contents = new[]
                        {
                            new
                            {
                                parts = new[]
                                {
                                    new { text = prompt }
                                }
                            }
                        }
                    };

                    var json = JsonSerializer.Serialize(requestBody);
                    var content = new StringContent(json, Encoding.UTF8, "application/json");

                    var response = await client.PostAsync(url, content);
                    var responseBody = await response.Content.ReadAsStringAsync();

                    if (!response.IsSuccessStatusCode)
                    {
                        Console.WriteLine($"Gemini API error ({model}): {(int)response.StatusCode} {response.StatusCode} - {responseBody}");
                        continue;
                    }

                    using var doc = JsonDocument.Parse(responseBody);
                    var text = doc.RootElement
                        .GetProperty("candidates")[0]
                        .GetProperty("content")
                        .GetProperty("parts")[0]
                        .GetProperty("text")
                        .GetString();

                    return text?.Trim() ?? "לא התקבלה תשובה מהמודל.";
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Exception with model {model}: {ex.Message}");
                }
            }

            return "ניתוח לא זמין כרגע עקב עומס בשרתים.";
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
