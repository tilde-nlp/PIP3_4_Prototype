
using Microsoft.Bot.Connector.DirectLine;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace PsiBot.Services.Bot
{
    public class DirectLineHandler : IDirectLineHandler
    {
        public class DirectLineToken
        {
            public string conversationId { get; set; }
            public string token { get; set; }
            public int expires_in { get; set; }
        }

        private string secret { get; set; }
        private string userName { get; set; }
        private string userId { get; set; }
        private DirectLineClient directLineClient { get; set; }
        private Conversation conversation { get; set; }
        public delegate void MessageCallback(string message);
        public MessageCallback messageCallback;
        private string thread { get; set; }
        private string locale { get; set; }
        public DirectLineHandler(string _secret)
        {
            secret = _secret;
            userName = "MA";
            userId = Guid.NewGuid().ToString();
        }
        public override async Task connect(string thread, string locale)
        {
            this.thread = thread;
            this.locale = locale;
            HttpClient client = new HttpClient();

            HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Post, "https://directline.botframework.com/v3/directline/tokens/generate");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", secret);
            request.Content = new StringContent(
                System.Text.Json.JsonSerializer.Serialize(
                    new { User = new { Id = userId } }),
                    Encoding.UTF8,
                    "application/json");

            var response = await client.SendAsync(request);

            string token = String.Empty;
            if (response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync();
                token = System.Text.Json.JsonSerializer.Deserialize<DirectLineToken>(body).token;
                Console.WriteLine($"{DateTime.Now.ToString("yyyyMMdd-hh:mm:ss")} Acquired Directline token {thread}");
            }
            else
            {
                Console.WriteLine($"{DateTime.Now.ToString("yyyyMMdd-hh:mm:ss")} Failed to acquire Directline token {thread} {response.StatusCode}");
                Console.WriteLine($"{DateTime.Now.ToString("yyyyMMdd-hh:mm:ss")} {await response.Content.ReadAsStringAsync()}");
            }


            // Use token to create conversation
            directLineClient = new DirectLineClient(token);         
            conversation = directLineClient.Conversations.StartConversation();
            

            var From = new ChannelAccount(userId, userName);
            Activity userMessage = new Activity
            {
                From = From,
                Text = locale,
                Name = "locale",
                Type = ActivityTypes.Event
            };
            directLineClient.Conversations.PostActivity(conversation.ConversationId, userMessage);
            userMessage = new Activity
            {
                From = From,
                Text = thread,
                Name = "teams/thread",
                Type = ActivityTypes.Event
            };
            directLineClient.Conversations.PostActivity(conversation.ConversationId, userMessage);           
        }

        string watermark = null;
        public override async Task send(string text)
        {
            Console.WriteLine($"{DateTime.Now.ToString("yyyyMMdd-hh:mm:ss")} {thread} Sending to directline {text}");
            try
            {
                var From = new ChannelAccount(userId, userName);
                var props = new JObject();
                props.Add("thread", thread);
                Activity userMessage = new Activity
                {
                    From = From,
                    Text = text,
                    Type = ActivityTypes.Message,
                    Properties = props
                };

                directLineClient.Conversations.PostActivity(conversation.ConversationId, userMessage);

                         ActivitySet resp = directLineClient.Conversations.GetActivities(conversation.ConversationId, watermark);
                         if (resp != null)
                         {
                             watermark = resp.Watermark;
                             var activities = from x in resp.Activities
                                              where x.Type == "message" && x.From.Name != userName
                                              select x;

                             foreach (var activity in activities)
                             {
                                 Console.WriteLine($"{DateTime.Now.ToString("yyyyMMdd-hh:mm:ss")} DirectLine response: " + activity.Text);
                                 if (messageCallback != null)
                                     messageCallback(activity.Text);
                             }
                         }
                Console.WriteLine($"{DateTime.Now.ToString("yyyyMMdd-hh:mm:ss")} FINISHED Sending to directline {text}");
            } catch (Exception e)
            {
                Console.WriteLine($"{DateTime.Now.ToString("yyyyMMdd-hh:mm:ss")} Failed sending to directline {e.Message} {e.StackTrace}");
            }
        }

        public override async Task sendToChat(string text, string url)
        {
            try
            {
                Console.WriteLine($"{DateTime.Now.ToString("yyyyMMdd-hh:mm:ss")} {thread} Sending to chat via directline {text}");
                var From = new ChannelAccount(userId, userName);
                var props = new JObject();
                props.Add("thread", thread);
                Activity a = new Activity
                {
                    From = From,
                    Text = text,
                    Type = ActivityTypes.Event,
                    Name = "forward",
                    Attachments = new List<Attachment>(),
                    Properties = props
                };
                if (!string.IsNullOrEmpty(url))
                {
                    Thread.CurrentThread.CurrentCulture = new System.Globalization.CultureInfo(locale);
                    Thread.CurrentThread.CurrentUICulture = new System.Globalization.CultureInfo(locale);
                    ResourceManagerStringLocalizerFactory locfac = new ResourceManagerStringLocalizerFactory(Options.Create(new LocalizationOptions()), NullLoggerFactory.Instance);
                    var loc = locfac.Create("Resources.Bot.DirectLineHandler", System.Reflection.Assembly.GetExecutingAssembly().GetName().Name);


                    a.Attachments.Add(
                        new Attachment
                        {
                            ContentType = "application/vnd.microsoft.card.hero",
                            Content = new HeroCard
                            {
                                Text = loc["DOWNLOAD_DESC"].Value,
                                Title = loc["MEETING_NOTES"].Value,
                                Buttons = new List<CardAction> {
                            new CardAction
                            {
                                Title = loc["DOWNLOAD"].Value,
                                Type = ActionTypes.OpenUrl,
                                Value = url
                            }
                                }

                            }
                        });
                }
                directLineClient.Conversations.PostActivity(conversation.ConversationId, a);
                Console.WriteLine($"{DateTime.Now.ToString("yyyyMMdd-hh:mm:ss")} FINISHED Sending to chat via directline {text}");
            } catch (Exception e)
            {
                Console.WriteLine($"{DateTime.Now.ToString("yyyyMMdd-hh:mm:ss")} {e.Message} {e.StackTrace} {e.InnerException?.Message}");
            }
        }
    }
}
