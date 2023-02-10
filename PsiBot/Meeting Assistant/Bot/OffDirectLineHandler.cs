
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
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace PsiBot.Services.Bot
{
    public class OffDirectLineHandler : IDirectLineHandler
    {
    
        public class ChannelData
        {
            string clientActivityId { get; set; }
            public string clientTimestamp { get; set; }

            public ChannelData()
            {
                clientActivityId = Guid.NewGuid().ToString();                
            }
        }

        //These values are grabbed from webchat, no idea if any of these matter
        public class MessageEntities
        {
            bool requiresBotState { get; set; }
            bool supportsListening { get; set; }
            bool supportsTts { get; set; }
            string type { get; set; }

            public MessageEntities()
            {
                requiresBotState = true;
                supportsListening = true;
                supportsTts = true;
                type = "ClientCapabilities";
            }
        }

        public class MessageFrom
        {
            public string id { get; set; }
            public string name { get; set; }
            public string role { get; set; }
        }

        public class EventMessage
        {
            public ChannelData channelData { get; set; }
            public string channelId { get; set; }
            public string locale { get; set; }
            public string name { get; set; }
            public List<MessageEntities> entities { get; set; }
            public MessageFrom from { get; set; }
            public string type { get; set; }
            public string timeStamp { get; set; }
            public object value { get; set; }
            public string text { get; set; }
            public object properties { get; set; }
            public List<Attachment> attachments { get; set; }

            public EventMessage()
            {
                channelId = "directline";
                entities = new List<MessageEntities> { new MessageEntities() };
                channelData = new ChannelData();
                type = "event";
                timeStamp = DateTime.Now.ToString("yyyy-MM-ddThh:mm:ss.ss0Z");
                channelData.clientTimestamp = DateTime.Now.ToString("yyyy-MM-ddThh:mm:ss.ss0Z");
                //set from on usage
                //set locale on usage
                //set name on usage (this is event type)
                //set value on usage (this is advanced payload)
                //set text on usage (this is basic payload)
            }
        }

        public class TextMessage
        {
            public ChannelData channelData { get; set; }
            public string channelId { get; set; }
            public string locale { get; set; }            
            public List<MessageEntities> entities { get; set; }
            public MessageFrom from { get; set; }            
            public string type { get; set; }
            public string timeStamp { get; set; }            
            public string text { get; set; }
            public string textFormat { get; set; }
            public object properties { get; set; }

            public TextMessage()
            {
                channelId = "directline";
                entities = new List<MessageEntities> { new MessageEntities() };
                channelData = new ChannelData();
                type = "message";
                timeStamp = DateTime.Now.ToString("yyyy-MM-ddThh:mm:ss.ss0Z");
                channelData.clientTimestamp = DateTime.Now.ToString("yyyy-MM-ddThh:mm:ss.ss0Z");
                textFormat = "plain";
                //set from on usage
                //set locale on usage                
                //set text on usage (this is basic payload)
            }
        }

        public class DirectLineResponse
        {
            public string conversationId { get; set; }
            public string expiresIn { get; set; }
        }
        
        private string userName { get; set; }
        private string userId { get; set; }
        private DirectLineClient directLineClient { get; set; }
        private Conversation conversation { get; set; }
        public delegate void MessageCallback(string message);
        public MessageCallback messageCallback;
        private string thread { get; set; }
        private string locale { get; set; }
        private string url { get; set; }
        private string conversationId { get; set; }
        HttpClient client { get; set; }
        public OffDirectLineHandler(string _url)
        {
            url = _url;
            userName = "Tilde MA";
            userId = Guid.NewGuid().ToString();
        }
        public override async Task connect(string thread, string locale)
        {
            this.thread = thread;
            this.locale = locale;
            client = new HttpClient();
            HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Post, url);
            
            request.Content = new StringContent(
                "{}",
                    Encoding.UTF8,
                    "application/json");

            var response = await client.SendAsync(request);

        
            if (response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsAsync<DirectLineResponse>();
                conversationId = body.conversationId;
                url = $"{url}/{conversationId}/activities";
                Console.WriteLine($"{DateTime.Now.ToString("yyyyMMdd-hh:mm:ss")} Acquired Offline Directline connection {thread}");
            }
            else
            {
                Console.WriteLine($"{DateTime.Now.ToString("yyyyMMdd-hh:mm:ss")} Failed to acquire Offline Directline connection {thread} {response.StatusCode}");
                Console.WriteLine($"{DateTime.Now.ToString("yyyyMMdd-hh:mm:ss")} {await response.Content.ReadAsStringAsync()}");
                return;
            }

            var from = new MessageFrom
            {
                id = userId,
                name = userName,
                role = "user"
            };

            var eventmsg = new EventMessage();
            eventmsg.from = from;
            eventmsg.locale = locale;
            eventmsg.name = "locale";
            eventmsg.text = locale;

            request = new HttpRequestMessage(HttpMethod.Post, url);
            request.Content = new StringContent(
                System.Text.Json.JsonSerializer.Serialize(eventmsg),
                    Encoding.UTF8,
                    "application/json");

            response = await client.SendAsync(request);
            if (response.IsSuccessStatusCode)
            {                
                Console.WriteLine($"{DateTime.Now.ToString("yyyyMMdd-hh:mm:ss")} Set Offline Directline locale {thread}");
            }
            else
            {
                Console.WriteLine($"{DateTime.Now.ToString("yyyyMMdd-hh:mm:ss")} Failed to set Offline Directline locale {thread} {response.StatusCode}");
                Console.WriteLine($"{DateTime.Now.ToString("yyyyMMdd-hh:mm:ss")} {await response.Content.ReadAsStringAsync()}");
                return;
            }

            eventmsg = new EventMessage();
            eventmsg.from = from;
            eventmsg.locale = locale;
            eventmsg.name = "teams/thread";
            eventmsg.text = thread;

            request = new HttpRequestMessage(HttpMethod.Post, url);
            request.Content = new StringContent(
                System.Text.Json.JsonSerializer.Serialize(eventmsg),
                    Encoding.UTF8,
                    "application/json");

            response = await client.SendAsync(request);
            if (response.IsSuccessStatusCode)
            {
                Console.WriteLine($"{DateTime.Now.ToString("yyyyMMdd-hh:mm:ss")} Set Offline Directline thread {thread}");
            }
            else
            {
                Console.WriteLine($"{DateTime.Now.ToString("yyyyMMdd-hh:mm:ss")} Failed to set Offline Directline thread {thread} {response.StatusCode}");
                Console.WriteLine($"{DateTime.Now.ToString("yyyyMMdd-hh:mm:ss")} {await response.Content.ReadAsStringAsync()}");
                return;
            }
        }
        
        public override async Task send(string text)
        {
            Console.WriteLine($"{DateTime.Now.ToString("yyyyMMdd-hh:mm:ss")} {thread} Sending to offline directline {text}");
            try
            {
                var from = new MessageFrom
                {
                    id = userId,
                    name = userName,
                    role = "user"
                };

                var msg = new TextMessage();
                msg.from = from;
                msg.locale = locale;                
                msg.text = text;
                msg.properties = new { thread = thread };

                var request = new HttpRequestMessage(HttpMethod.Post, url);
                request.Content = new StringContent(
                    System.Text.Json.JsonSerializer.Serialize(msg),
                        Encoding.UTF8,
                        "application/json");

                var response = await client.SendAsync(request);
                if (response.IsSuccessStatusCode)
                {
                    Console.WriteLine($"{DateTime.Now.ToString("yyyyMMdd-hh:mm:ss")} Sent to Offline Directline {thread}");
                }
                else
                {
                    Console.WriteLine($"{DateTime.Now.ToString("yyyyMMdd-hh:mm:ss")} Failed to send to Offline Directline {thread} {response.StatusCode}");
                    Console.WriteLine($"{DateTime.Now.ToString("yyyyMMdd-hh:mm:ss")} {await response.Content.ReadAsStringAsync()}");
                    return;
                }                
            } catch (Exception e)
            {
                Console.WriteLine($"{DateTime.Now.ToString("yyyyMMdd-hh:mm:ss")} Failed sending to offline directline {e.Message} {e.StackTrace}");
            }
        }

        public override async Task sendToChat(string text, string url)
        {
            try
            {
                Console.WriteLine($"{DateTime.Now.ToString("yyyyMMdd-hh:mm:ss")} {thread} Sending to chat via offline directline {text} {url}");

                var from = new MessageFrom
                {
                    id = userId,
                    name = userName,
                    role = "user"
                };

                Thread.CurrentThread.CurrentCulture = new System.Globalization.CultureInfo(locale);
                Thread.CurrentThread.CurrentUICulture = new System.Globalization.CultureInfo(locale);
                ResourceManagerStringLocalizerFactory locfac = new ResourceManagerStringLocalizerFactory(Options.Create(new LocalizationOptions()), NullLoggerFactory.Instance);
                var loc = locfac.Create("Resources.Bot.DirectLineHandler", System.Reflection.Assembly.GetExecutingAssembly().GetName().Name);
                var eventmsg = new EventMessage();
                eventmsg.from = from;
                eventmsg.locale = locale;
                eventmsg.name = "forward";
                eventmsg.text = text;
                eventmsg.properties = new { thread = thread };
                eventmsg.attachments = new List<Attachment> { new Attachment
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
                        } };

                var request = new HttpRequestMessage(HttpMethod.Post, this.url);
                request.Content = new StringContent(
                    System.Text.Json.JsonSerializer.Serialize(eventmsg),
                        Encoding.UTF8,
                        "application/json");

                var response = await client.SendAsync(request);
                if (response.IsSuccessStatusCode)
                {
                    Console.WriteLine($"{DateTime.Now.ToString("yyyyMMdd-hh:mm:ss")} Sent to chat via Offline Directline {thread} {url}");
                }
                else
                {
                    Console.WriteLine($"{DateTime.Now.ToString("yyyyMMdd-hh:mm:ss")} Failed to sent to chat via Offline Directline {thread} {response.StatusCode}");
                    Console.WriteLine($"{DateTime.Now.ToString("yyyyMMdd-hh:mm:ss")} {await response.Content.ReadAsStringAsync()}");
                    return;
                }                              
            } catch (Exception e)
            {
                Console.WriteLine($"{DateTime.Now.ToString("yyyyMMdd-hh:mm:ss")} {e.Message} {e.StackTrace} {e.InnerException?.Message}");
            }
        }
    }
}
