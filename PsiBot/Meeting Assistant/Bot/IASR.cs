using Microsoft.AspNetCore.SignalR;
using Psibot.Service;
using PsiBot.Service.Settings;
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.UI.WebControls.WebParts;

namespace PsiBot.Services.Bot
{
    public abstract class IASR
    {
        protected byte[] currentBuffer;
        protected bool disposed = false;
        protected bool socketReady = false;
        protected bool reading = false;
        protected bool initializing = false;
        protected bool sending = false;
        protected IHubContext<CaptionHub> captionHub;
        public string displayName = "";
        public string thread;
        public string userId;
        public string langId;
        protected Transcript transcript;
        protected string theLock = ""; //Locking on this because currentBuffer can be null and must not be assigned to when locked
        protected string msgPrefix = Guid.NewGuid().ToString();
        protected int msgId;
        public string cartUrl = "";
        protected ASRConfiguration config;
        protected IDirectLineHandler directLine;
        protected MachineTranslation translation;
        protected List<string> translationProfiles;
        protected CaptionHubState hubState;
        protected ASRLangConfiguration asrconfig;
        public void addBuffer(byte[] parm)
        {
            silence = 0;            
            lock (theLock)
            {
                currentBuffer = Combine(currentBuffer, parm);
            }
        }

        public byte[] Combine(byte[] first, byte[] second)
        {
            if (first == null)
                return second;
            byte[] ret = new byte[first.Length + second.Length];
            Buffer.BlockCopy(first, 0, ret, 0, first.Length);
            Buffer.BlockCopy(second, 0, ret, first.Length, second.Length);
            return ret;
        }

        public void initSocket()
        {
            Task.Run(() => doInitSocket());
        }

        public virtual void dispose()
        {
            disposed = true;
        }

        protected abstract void doSend(byte[] data);

        protected abstract void doInitSocket();
        protected abstract Task readStuff();
        protected int silence = 0;

        public static int bytesPerMS = 16 * 2; //16khz rate, 2 bytes per sample;
        protected void sendStuff()
        {
            var time = DateTime.Now.Ticks;
            var writes = 0;
            sending = true;
            try
            {
                while (!disposed)
                {
                    var delta = (DateTime.Now.Ticks - time) / TimeSpan.TicksPerMillisecond;
                    time = DateTime.Now.Ticks;
                    writes++;                    
                    if (!socketReady && !initializing)
                        throw (new Exception("Socket dead"));
                    if (socketReady && asrconfig.SilenceTimeout > 0 && silence >= asrconfig.SilenceTimeout && (currentBuffer == null || currentBuffer?.Length == 0))
                    {                        
                        sendFinalize().Wait();
                        silence = -1;
                    }
                    if (socketReady)
                    {
                        if ((currentBuffer?.Length ?? 0) == 0)
                        {
                            if (asrconfig.SilenceTimeout > 0)
                            {                                
                                if (silence >= 0)
                                    silence += 100;
                            }
                        }
                        if (currentBuffer?.Length > 0)
                        { 
                            byte[] forSend = null;
                            lock (theLock)
                            {
                                forSend = currentBuffer;
                                currentBuffer = null;
                            }                            
                            doSend(forSend);
                        }
                        
                    }
                    Thread.Sleep(100);
                }
            }
            catch (Exception e)
            {
                Console.WriteLine($"{DateTime.Now.ToString("yyyyMMdd-hh:mm:ss")} {displayName} ERROR IN SEND: " + e.Message + e.StackTrace);
                Task.Run(() => doInitSocket());
            }
            finally
            {
                sending = false;
            }
        }

        protected virtual async Task sendFinalize()
        {
            byte[] b = new byte[bytesPerMS * 5000];
            Array.Clear(b, 0, b.Length);
            addBuffer(b);
        }

        protected async Task handleGuess(string text)
        {
            text = null; //showing progress instead
            Console.WriteLine($"{DateTime.Now.ToString("yyyyMMdd-hh:mm:ss")} GUESS: {text}");
            await captionHub.Clients.Group(thread).SendAsync("caption", new { user = displayName, message = text, id = msgPrefix + msgId, timestamp = DateTime.Now.ToUniversalTime().Ticks / TimeSpan.TicksPerMillisecond - 62135596800000, userid = userId });
        }

        protected async Task handleFinal(string text)
        {
            Console.WriteLine($"{DateTime.Now.ToString("yyyyMMdd-hh:mm:ss")} FINAL: {text}");
            var timestamp = DateTime.Now.ToUniversalTime().Ticks / TimeSpan.TicksPerMillisecond - 62135596800000;
            var transMsg = new TranscriptMessage
            {
                id = msgId,
                name = displayName,
                prefix = msgPrefix,
                text = text,
                timestamp = timestamp,
                lang = langId,
                translations = new Dictionary<string, string>(),
                userid = userId
            };
            transcript.appendMessage(transMsg);
            await captionHub.Clients.Group(thread).SendAsync("caption", new { user = displayName, message = text, id = msgPrefix + msgId, timestamp = timestamp });
            foreach (var tp in translationProfiles)
            {
                if (hubState.hasUsers(thread + tp))
                {
                    var trans = await translation.translate(translation.getSystemId(langId, tp), text);
                    transMsg.translations.Add(tp, trans);
                    await captionHub.Clients.Group(thread + tp).SendAsync("caption", new { user = displayName, message = trans, id = msgPrefix + msgId, timestamp = timestamp });
                }
            }
            msgId++;
            text = displayName + ": " + text;
            if (directLine != null)
            {
                Task.Run(() => { directLine.send(text); }).ConfigureAwait(false);
            }
            else
                Console.WriteLine($"{DateTime.Now.ToString("yyyyMMdd-hh:mm:ss")} Directline unavailable");
            if (!String.IsNullOrEmpty(cartUrl))
                sendToCC(text);
        }

        public void sendToCC(string text)
        {
            var httpClient = new HttpClient();
            httpClient.PostAsync(cartUrl, new StringContent(text, Encoding.UTF8, "text/plain")).GetAwaiter().GetResult();
        }
    }
}