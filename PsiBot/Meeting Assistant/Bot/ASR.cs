using Microsoft.AspNetCore.SignalR;
using Psibot.Service;
using PsiBot.Service.Settings;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using System.Web;

namespace PsiBot.Services.Bot
{
    public class ASR : IASR
    {
        private ClientWebSocket asrSocket;                                                                  

        private class asrMessage {
            [JsonPropertyName("request_id")]
            public string requestId { get; set; }
            public int status { get; set; }
            public asrResult result { get; set; }
            [JsonPropertyName("segment-start")]
            public double segmentStart { get; set; }
            [JsonPropertyName("total-length")]
            public double totalLength { get; set; }
        }

        private class asrHypothesis
        {
            public string transcript { get; set; }
            public double likelihood { get; set; }
            [JsonPropertyName("raw_transcript")]
            public string rawTranscript { get; set; }            
        }

        private class asrResult
        {
            public bool final {get; set;}
            public List<asrHypothesis> hypotheses { get; set; }
        }

        public ASR(string _cartURL, ASRConfiguration _config, string _displayName, string _userId, IDirectLineHandler _directLine, string _thread, IHubContext<CaptionHub> _captionHub, Transcript _transcript, string _langId,
            MachineTranslation _translation, CaptionHubState _hubState)
        {
            cartUrl = _cartURL;
            config = _config;
            displayName = _displayName;
            directLine = _directLine;
            thread = _thread;
            captionHub = _captionHub;
            transcript = _transcript;
            langId = _langId;
            translation = _translation;
            translationProfiles = translation.mtLangs(langId);
            Console.WriteLine($"{DateTime.Now.ToString("yyyyMMdd-hh:mm:ss")}ASR initialized with translation profiles: " + String.Join(" ", translationProfiles));
            hubState = _hubState;
            userId = _userId;
        }        
        
        protected override void doInitSocket()
        {            
            if (initializing)
                return;
            try
            {
                asrconfig = config.Langs.FirstOrDefault(x => x.Id == langId) ?? config.Langs.First(); 
                initializing = true;
                Console.WriteLine($"{DateTime.Now.ToString("yyyyMMdd-hh:mm:ss")}Initializing socket");
                asrSocket = new ClientWebSocket();
                asrSocket.Options.SetRequestHeader("X-API-KEY", asrconfig.AppSecret);
                asrSocket.ConnectAsync(new Uri(asrconfig.Url), CancellationToken.None).Wait();
                var timeStamp = DateTimeOffset.Now.ToUnixTimeSeconds();
                string hash = "";
                using (SHA1 sha1Hash = SHA1.Create())
                {
                    byte[] sourceBytes = Encoding.UTF8.GetBytes(timeStamp.ToString() + asrconfig.AppId + asrconfig.AppSecret);
                    byte[] hashBytes = sha1Hash.ComputeHash(sourceBytes);
                    hash = BitConverter.ToString(hashBytes).Replace("-", String.Empty);
                }
                var authMessage = "{ \"enable-postprocess\": [\"numbers\",\"punc\"] }";
                asrSocket.SendAsync(new ArraySegment<byte>(Encoding.UTF8.GetBytes(authMessage)), WebSocketMessageType.Text, true, CancellationToken.None).Wait();
                
                socketReady = true; 
                if (!reading)
                    Task.Run(() => readStuff());
                if (!sending)
                    Task.Run(() => sendStuff());                
            }
            catch (Exception e)
            {
                Console.WriteLine($"{DateTime.Now.ToString("yyyyMMdd-hh:mm:ss")} Error initializing socket: " + e.Message + e.InnerException?.Message + e.InnerException?.InnerException?.Message);
            }
            finally
            {
                initializing = false;
            }
        }

        protected override void doSend(byte[] data)
        {            
            asrSocket.SendAsync(new ArraySegment<byte>(data), WebSocketMessageType.Binary, true, CancellationToken.None).Wait();                        
        }      

        protected override async Task readStuff()
        {
            int reads = 0;
            try
            {
                reading = true;
                while (!disposed)
                {
                    reads++;
                    if (reads % 1000 == 0)
                    {
                        Console.WriteLine($"{DateTime.Now.ToString("yyyyMMdd-hh:mm:ss")} {displayName} reads {reads} in {thread}");
                    }
                    if (!socketReady && !initializing)
                        throw (new Exception("Socket dead"));
                    if (asrSocket == null || asrSocket.State != WebSocketState.Open)
                    {
                        Thread.Sleep(100);
                        continue;
                    }
                    
                    //8192 value has no specific reason that I know
                    ArraySegment<Byte> buffer = new ArraySegment<byte>(new Byte[8192]);
                    WebSocketReceiveResult result = null;
                    using (var ms = new MemoryStream())
                    {
                        do
                        {                            
                            result = asrSocket.ReceiveAsync(buffer, CancellationToken.None).GetAwaiter().GetResult();                                                      
                            ms.Write(buffer.Array, buffer.Offset, result.Count);
                        }
                        while (!result.EndOfMessage);

                        ms.Seek(0, SeekOrigin.Begin);

                        if (result.MessageType == WebSocketMessageType.Text)
                        {
                            using (var reader = new System.IO.StreamReader(ms, Encoding.UTF8))
                            {                                
                                var tres = reader.ReadToEnd();                                
                                handleMessage(tres);                                
                            }                            
                        }                        
                    }
                    Thread.Sleep(100); 
                }
            }
            catch (Exception e)
            {
                Console.WriteLine($"{DateTime.Now.ToString("yyyyMMdd-hh:mm:ss")} {displayName} ERROR IN READ: " + e.Message);
            }
            finally
            {
                Console.WriteLine($"{DateTime.Now.ToString("yyyyMMdd-hh:mm:ss")} {displayName} Ending read: ");
                reading = false;
            }
        }

        protected override async Task sendFinalize()
        {
            Console.WriteLine($"{DateTime.Now.ToString("yyyyMMdd-hh:mm:ss")} {displayName} SENDING FINALIZER");          
            await asrSocket.SendAsync(new ArraySegment<byte>(Encoding.ASCII.GetBytes("EOS")), WebSocketMessageType.Text, true, CancellationToken.None);
        }
   
        public async Task handleMessage(string message)
        {
            try
            {                
                var msg = JsonSerializer.Deserialize<asrMessage>(message);
                if (msg?.result?.hypotheses?.Any() ?? false)
                {
                    string text = msg.result.hypotheses.First().transcript;
                    await handleGuess(text);
                }
                if (msg?.result?.final ?? false)
                {
                    string text = msg.result.hypotheses.First().transcript;
                    await handleFinal(msg.result.hypotheses.First().transcript);
                }                                
            } catch (Exception e)
            {
                throw e;
            }
        }        
    }
}