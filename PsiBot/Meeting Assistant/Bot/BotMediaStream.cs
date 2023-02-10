// <copyright file="BotMediaStream.cs" company="Microsoft Corporation">
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license.
// </copyright>

using Microsoft.Graph.Communications.Calls;
using Microsoft.Graph.Communications.Calls.Media;
using Microsoft.Graph.Communications.Common;
using Microsoft.Graph.Communications.Common.Telemetry;
using Microsoft.Graph;
using Microsoft.Skype.Bots.Media;
using Microsoft.Skype.Internal.Media.Services.Common;
using System;
using System.Collections.Generic;
using PsiBot.Service.Settings;
using System.Linq;
using System.Threading;
using Microsoft.Psi;
using Microsoft.Psi.TeamsBot;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Microsoft.AspNetCore.SignalR;
using Psibot.Service;

namespace PsiBot.Services.Bot
{
    /// <summary>
    /// Class responsible for streaming audio.
    /// </summary>
    public class BotMediaStream : ObjectRootDisposable
    {
        /// <summary>
        /// The participants
        /// </summary>
        internal List<IParticipant> participants;

        private readonly IAudioSocket audioSocket;
        private readonly ILocalMediaSession mediaSession;
        private readonly IGraphLogger logger;
        private readonly MediaFrameSourceComponent mediaFrameSourceComponent;
        private int shutdown;
        private MediaSendStatus audioSendStatus = MediaSendStatus.Inactive;

        private List<byte[]> finalDataBytes = new List<byte[]>();
        private Dictionary<uint, IASR> asrs = new Dictionary<uint, IASR>();
        private CallHandler call;
        private string cartUrl;
        private ASRConfiguration asrConfiguration;
        public IDirectLineHandler directLine;
        private string thread;
        private string asrLang;
        private MachineTranslation translation;
        IHubContext<CaptionHub> captionHub;
        Transcript transcript;
        CaptionHubState hubState;
        GraphServiceClient graphClient;

        /// <summary>
        /// Initializes a new instance of the <see cref="BotMediaStream"/> class.
        /// </summary>
        /// <param name="mediaSession">The media session.</param>
        /// <param name="callHandler">Call handler.</param>
        /// <param name="pipeline">Psi Pipeline.</param>
        /// <param name="teamsBot">Teams bot instance.</param>
        /// <param name="exporter">Psi Exporter.</param>
        /// <param name="logger">Graph logger.</param>
        /// <param name="botConfiguration">Bot configuration</param>
        /// <exception cref="InvalidOperationException">A mediaSession needs to have at least an audioSocket</exception>
        public BotMediaStream(
            ILocalMediaSession mediaSession,
            CallHandler callHandler,
            Pipeline pipeline,
            TeamsBot teamsBot,
            IGraphLogger logger,
            BotConfiguration botConfiguration,
            ASRConfiguration _asrConfiguration,
            string _cartUrl,
            string _thread,
            IHubContext<CaptionHub> _captionHub,
            Transcript _transcript,
            MachineTranslation _translation,
            CaptionHubState _hubState,
            GraphServiceClient _graphClient,
            string culture
        )
            : base(logger)
        {
            ArgumentVerifier.ThrowOnNullArgument(mediaSession, nameof(mediaSession));
            ArgumentVerifier.ThrowOnNullArgument(logger, nameof(logger));
            ArgumentVerifier.ThrowOnNullArgument(botConfiguration, nameof(botConfiguration));

            this.mediaSession = mediaSession;
            this.logger = logger;
            call = callHandler;
            asrConfiguration = _asrConfiguration;
            cartUrl = _cartUrl;
            thread = _thread;
            captionHub = _captionHub;
            transcript = _transcript;
            translation = _translation;
            hubState = _hubState;
            graphClient = _graphClient;

            this.mediaFrameSourceComponent = new MediaFrameSourceComponent(pipeline, callHandler, this.logger);

            this.mediaFrameSourceComponent.Audio.PipeTo(teamsBot.AudioIn);

            teamsBot.AudioOut?.Do(buffer =>
            {
                if (this.audioSendStatus == MediaSendStatus.Active && teamsBot.EnableAudioOutput)
                {
                    IntPtr unmanagedPointer = Marshal.AllocHGlobal(buffer.Length);
                    Marshal.Copy(buffer.Data, 0, unmanagedPointer, buffer.Length);
                    this.SendAudio(new AudioSendBuffer(unmanagedPointer, buffer.Length, AudioFormat.Pcm16K));
                    Marshal.FreeHGlobal(unmanagedPointer);
                }
            });

            // Subscribe to the audio media.
            this.audioSocket = this.mediaSession.AudioSocket;
            if (this.audioSocket == null)
            {
                throw new InvalidOperationException("A mediaSession needs to have at least an audioSocket");
            }

            this.audioSocket.AudioSendStatusChanged += this.OnAudioSendStatusChanged;
            this.audioSocket.AudioMediaReceived += this.OnAudioMediaReceived;

            if (!string.IsNullOrEmpty(botConfiguration.OffDirectLineUrl))
            {
                directLine = new OffDirectLineHandler(botConfiguration.OffDirectLineUrl);
                Task.Run(() => directLine.connect(thread, culture)).Wait();
            }
            else
            if (!string.IsNullOrEmpty(botConfiguration.DirectLineSecret))
            {
                directLine = new DirectLineHandler(botConfiguration.DirectLineSecret);                                
                Task.Run(() => directLine.connect(thread, culture)).Wait();
            }
        }

        public void setAsrLang(string lang)
        {
            if (asrLang != lang)
            {
                asrs.ForEach(x => x.Value.dispose());
                asrs = new Dictionary<uint, IASR>();
            }
            asrLang = lang;
        }

        IASR getAsr(uint id)
        {
            if (asrs.ContainsKey(id))
            {
                return asrs[id];
            }
            else
            {
                
                var participant = call.Call.Participants.FirstOrDefault(x => x.Resource.MediaStreams.Any(x => x.SourceId == id.ToString()));
                if (participant == null)
                    return null;
                string displayName = null;
                displayName = participant?.Resource?.Info?.Identity?.User?.DisplayName ?? displayName;
                var userId = participant?.Resource?.Info?.Identity?.User?.Id;
                if (displayName == null)
                    displayName = participant?.Resource?.Info?.Identity?.Application?.DisplayName ?? displayName;
                if (userId == null)
                    userId = participant?.Resource?.Info?.Identity?.Application?.Id;
                if (displayName == null)
                {
                    var identity = participant?.Resource?.Info?.Identity?.AdditionalData?.FirstOrDefault();
                    if (identity != null && identity.Value.Value is Microsoft.Graph.Identity)
                    {
                        displayName = (identity.Value.Value as Microsoft.Graph.Identity).DisplayName;
                        userId = (identity.Value.Value as Microsoft.Graph.Identity).Id;
                    }
                }
                //ProfilePhoto photo;
                if (displayName == null)
                    displayName = "Unknown";
                Console.WriteLine($"{DateTime.Now.ToString("yyyyMMdd-hh:mm:ss")} Adding new ASR to {thread} for {id} {displayName}");

                IASR asr = null;
                var asrconfig = asrConfiguration.Langs.FirstOrDefault(x => x.Id == asrLang) ?? asrConfiguration.Langs.First();                
                asr = new ASR(cartUrl, asrConfiguration, displayName, userId, directLine, thread, captionHub, transcript, asrLang, translation, hubState);                                    
                asr.initSocket();
                asrs.Add(id, asr);
                return asr;
            }
        }

        /// <inheritdoc/>
        protected override void Dispose(bool disposing)
        {

            foreach (var v in asrs)
                v.Value.dispose();
            // Event Dispose of the bot media stream object
            base.Dispose(disposing);

            if (Interlocked.CompareExchange(ref this.shutdown, 1, 1) == 1)
            {
                return;
            }

            if (this.audioSocket != null)
            {
                this.audioSocket.AudioSendStatusChanged -= this.OnAudioSendStatusChanged;
                this.audioSocket.AudioMediaReceived -= this.OnAudioMediaReceived;
            }

        }

        #region Audio
        /// <summary>
        /// Callback for informational updates from the media plaform about audio status changes.
        /// Once the status becomes active, audio can be loopbacked.
        /// </summary>
        /// <param name="sender">The audio socket.</param>
        /// <param name="e">Event arguments.</param>
        private void OnAudioSendStatusChanged(object sender, AudioSendStatusChangedEventArgs e)
        {
            this.logger.Info($"[AudioSendStatusChangedEventArgs(MediaSendStatus={e.MediaSendStatus})]");
            this.audioSendStatus = e.MediaSendStatus;
        }

        /// <summary>
        /// Receive audio from subscribed participant.
        /// </summary>
        /// <param name="sender">The sender.</param>
        /// <param name="e">The audio media received arguments.</param>
        private void OnAudioMediaReceived(object sender, AudioMediaReceivedEventArgs e)
        {
            try
            {
                if (e.Buffer.UnmixedAudioBuffers != null)
                    foreach (var buffer in e.Buffer.UnmixedAudioBuffers)
                    {
                        byte[] managedArray = new byte[buffer.Length];
                        var handler = buffer.Data;
                        int start = 0;
                        int length = (int)buffer.Length;
                        Marshal.Copy(handler, managedArray, start, length);
                        var asr = getAsr(buffer.ActiveSpeakerId);
                        if (asr == null) //Participants haven't been read yet, just drop the data; should only happen while bot is initially joining the call
                            return;                        
                        asr.addBuffer(managedArray);
                    }

                e.Buffer.Dispose();
            }
            catch (Exception ex)
            {
                this.logger.Error(ex);
            }
            finally
            {
                e.Buffer.Dispose();
            }

        }

        /// <summary>
        /// Sends an <see cref="AudioMediaBuffer"/> to the call from the Bot's audio feed.
        /// </summary>
        /// <param name="buffer">The audio buffer to send.</param>
        private void SendAudio(AudioMediaBuffer buffer)
        {
            // Send the audio to our outgoing video stream
            try
            {
                this.audioSocket.Send(buffer);
            }
            catch (Exception ex)
            {
                this.logger.Error(ex, $"[OnAudioReceived] Exception while calling audioSocket.Send()");
            }
        }
        #endregion   
    }
}
