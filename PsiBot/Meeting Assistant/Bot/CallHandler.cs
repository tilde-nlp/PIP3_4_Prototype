// <copyright file="CallHandler.cs" company="Microsoft Corporation">
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license.
// </copyright>

using Microsoft.Graph;
using Microsoft.Graph.Communications.Calls;
using Microsoft.Graph.Communications.Calls.Media;
using Microsoft.Graph.Communications.Common.Telemetry;
using Microsoft.Graph.Communications.Resources;
using PsiBot.Model.Constants;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Timers;
using PsiBot.Service.Settings;
using System.Linq;
using System.Collections.Concurrent;
using Microsoft.Skype.Bots.Media;
using Microsoft.Psi;
using Microsoft.Psi.Data;
using Microsoft.Psi.TeamsBot;
using System.Text.Json;
using Microsoft.AspNetCore.SignalR;
using Psibot.Service;
using PsiBot.Model.Models;
using System.Web;

namespace PsiBot.Services.Bot
{
    /// <summary>
    /// Call Handler Logic.
    /// </summary>
    public class CallHandler : HeartbeatHandler
    {
        /// <summary>
        /// Gets the call.
        /// </summary>
        /// <value>The call.</value>
        public ICall Call { get; }

        /// <summary>
        /// Gets the bot media stream.
        /// </summary>
        /// <value>The bot media stream.</value>
        public BotMediaStream BotMediaStream { get; private set; }

        /// <summary>
        /// MSI when there is no dominant speaker.
        /// </summary>
        public const uint DominantSpeakerNone = DominantSpeakerChangedEventArgs.None;

        /// <summary>
        /// The bot configuration
        /// </summary>
        private readonly BotConfiguration botConfiguration;
        private readonly ASRConfiguration asrConfiguration;

        // hashSet of the available sockets
        private readonly HashSet<uint> availableSocketIds = new HashSet<uint>();

        // this is an LRU cache with the MSI values, we update this Cache with the dominant speaker events
        // this way we can make sure that the muliview sockets are subscribed to the active (speaking) participants
        private readonly LRUCache currentVideoSubscriptions = new LRUCache(BotConstants.NumberOfMultiviewSockets + 1);

        private readonly object subscriptionLock = new object();

        // This dictionary helps maintain a mapping of the sockets subscriptions
        private readonly ConcurrentDictionary<uint, uint> msiToSocketIdMapping = new ConcurrentDictionary<uint, uint>();

        private readonly Pipeline pipeline;
        private readonly TeamsBot teamsBot;
        private string cartUrl;
        private GraphServiceClient graphClient;
        public string thread;
        string asrLang;        
        private Transcript transcript;

        /// <summary>
        /// Initializes a new instance of the <see cref="CallHandler"/> class.
        /// </summary>
        /// <param name="statefulCall">The stateful call.</param>
        /// <param name="botConfiguration">The bot configuration.</param>
        public CallHandler(ICall statefulCall, BotConfiguration botConfiguration, ASRConfiguration asrConfiguration, string _cartUrl, GraphServiceClient _graphClient, string _thread, IHubContext<CaptionHub> _captionHub, Transcript _transcript, MachineTranslation _translation, CaptionHubState _hubState, string _asrLang)
            : base(TimeSpan.FromMinutes(10), statefulCall?.GraphLogger)
        {
            cartUrl = _cartUrl;
            thread = _thread;
            this.botConfiguration = botConfiguration;
            this.asrConfiguration = asrConfiguration;
            var asrConfig = _asrLang != null ? this.asrConfiguration.Langs.FirstOrDefault(x => x.Id == _asrLang) : this.asrConfiguration.Langs.First();
            asrLang = asrConfig.Id;
            transcript = _transcript;
            var culture = string.IsNullOrEmpty(asrConfig.BaseLang) ? asrLang : asrConfig.BaseLang;
            transcript.culture = culture;

            this.pipeline = Pipeline.Create(enableDiagnostics: true);
            this.teamsBot = CreateTeamsBot(this.pipeline);
            PsiExporter exporter = null;

            this.Call = statefulCall;
            this.Call.OnUpdated += this.CallOnUpdated;

            // susbscribe to the participants updates, this will inform the bot if a particpant left/joined the meeting
            this.Call.Participants.OnUpdated += this.ParticipantsOnUpdated;            

            foreach (var participant in this.Call.Participants)
            {
                transcript.addParticipant(getNameOfParticipant(participant));
            }

            // attach the botMediaStream
            this.BotMediaStream = new BotMediaStream(this.Call.GetLocalMediaSession(), this, pipeline, teamsBot, this.GraphLogger, this.botConfiguration, this.asrConfiguration, cartUrl, _thread, _captionHub, _transcript, _translation, _hubState, _graphClient, culture);

            this.pipeline.PipelineExceptionNotHandled += (_, ex) =>
            {
                this.GraphLogger.Error($"PSI PIPELINE ERROR: {ex.Exception.Message}");
            };
            this.pipeline.RunAsync();
        }

        public string getAsrLang()
        {
            return asrLang;
        }

        public void setAsrLang(string lang)
        {
            asrLang = lang;
            BotMediaStream.setAsrLang(asrLang);
            var asrConfig = lang != null ? this.asrConfiguration.Langs.First(x => x.Id == lang) : this.asrConfiguration.Langs.First();
            transcript.culture = string.IsNullOrEmpty(asrConfig.BaseLang) ? asrLang : asrConfig.BaseLang;
        }

        public void setTimeZone(string timeZone)
        {
            transcript.timeZone = TimeZoneConverter.TZConvert.IanaToWindows(timeZone);
        }

        public void setMeetingInfo(GraphChat meetingInfo)
        {            
            transcript.meetingInfo = meetingInfo;
        }

        /// <summary>
        /// Create your ITeamsBot component.
        /// </summary>
        /// <param name="pipeline">The pipeline.</param>
        /// <returns>ITeamsBot instance.</returns>
        private static TeamsBot CreateTeamsBot(Pipeline pipeline)
        {
            return new TeamsBot(pipeline);
        }

        /// <inheritdoc/>
        protected override Task HeartbeatAsync(ElapsedEventArgs args)
        {
            return this.Call.KeepAliveAsync();
        }

        /// <inheritdoc/>
        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);

            this.pipeline.Dispose();
            this.Call.OnUpdated -= this.CallOnUpdated;
            this.Call.Participants.OnUpdated -= this.ParticipantsOnUpdated;

            foreach (var participant in this.Call.Participants)
            {
                participant.OnUpdated -= this.OnParticipantUpdated;
            }

            this.BotMediaStream?.Dispose();
        }

        /// <summary>
        /// Event fired when the call has been updated.
        /// </summary>
        /// <param name="sender">The call.</param>
        /// <param name="e">The event args containing call changes.</param>
        private void CallOnUpdated(ICall sender, ResourceEventArgs<Call> e)
        {
            this.GraphLogger.Info($"Call status updated to {e.NewResource.State} - {e.NewResource.ResultInfo?.Message}");
            // Event - Recording update e.g established/updated/start/ended

            if (e.OldResource.State != e.NewResource.State && e.NewResource.State == CallState.Established)
            {
            }
        }

        string getNameOfParticipant(IParticipant participant)
        {
            if (participant == null) 
                return null;
            string displayName = null;
            displayName = participant?.Resource?.Info?.Identity?.User?.DisplayName ?? displayName;
            if (displayName == null)
                displayName = participant?.Resource?.Info?.Identity?.Application?.DisplayName ?? displayName;
            if (displayName == null)
            {
                var identity = participant?.Resource?.Info?.Identity?.AdditionalData?.FirstOrDefault();
                if (identity != null && identity.Value.Value is Microsoft.Graph.Identity)
                {
                    displayName = (identity.Value.Value as Microsoft.Graph.Identity).DisplayName;
                }
            }
            return displayName;
        }
        /// <summary>
        /// Event fired when the participants collection has been updated.
        /// </summary>
        /// <param name="sender">Participants collection.</param>
        /// <param name="args">Event args containing added and removed participants.</param>
        public void ParticipantsOnUpdated(IParticipantCollection sender, CollectionEventArgs<IParticipant> args)
        {
            foreach (var participant in args.AddedResources)
            {
                Console.WriteLine($"{DateTime.Now.ToString("yyyyMMdd-hh:mm:ss")} Participant added to {thread} : {getNameOfParticipant(participant)}");
            }
            foreach (var participant in args.RemovedResources)
            {
                Console.WriteLine($"{DateTime.Now.ToString("yyyyMMdd-hh:mm:ss")} Participant removed from {thread} : {getNameOfParticipant(participant)}");
            }

            if (args.RemovedResources.Any() && Call.Participants.Count == 1 && getNameOfParticipant(Call.Participants.First()) == "Meeting Assistant")
            {
                var filename = transcript.save().GetAwaiter().GetResult();
                Console.WriteLine($"{DateTime.Now.ToString("yyyyMMdd-hh:mm:ss")} Leaving thread {thread} because only 1 participant");
                BotMediaStream.directLine.sendToChat("", $"https://{botConfiguration.ServiceCname}/TeamsApp/MeetingNotes?thread={HttpUtility.UrlEncode(filename)}").ConfigureAwait(false); 
                BotMediaStream.directLine.send("Meeting-Ended " + Call.Participants.Count.ToString()).ConfigureAwait(false);
                Call.DeleteAsync().ConfigureAwait(false);
            }
            else
                foreach (var participant in args.AddedResources)
                {
                    var name = getNameOfParticipant(participant);
                    //Duplicates are filtered out by addParticipant()
                    if (name != "Meeting Assistant" && transcript != null)                        
                        transcript.addParticipant(getNameOfParticipant(participant));
                }
        }

        /// <summary>
        /// Event fired when a participant is updated.
        /// </summary>
        /// <param name="sender">Participant object.</param>
        /// <param name="args">Event args containing the old values and the new values.</param>
        private void OnParticipantUpdated(IParticipant sender, ResourceEventArgs<Participant> args)
        {
        }

        /// <summary>
        /// Gets the participant with the corresponding MSI.
        /// </summary>
        /// <param name="msi">media stream id.</param>
        /// <returns>
        /// The <see cref="IParticipant"/>.
        /// </returns>
        public static IParticipant GetParticipantFromMSI(ICall call, uint msi)
        {
            return call.Participants.SingleOrDefault(x => x.Resource.IsInLobby == false && x.Resource.MediaStreams.Any(y => y.SourceId == msi.ToString()));
        }

        /// <summary>
        /// Tries to get the identity information of the given participant.
        /// </summary>
        /// <param name="participant">The participant we wish to get an identity from.</param>
        /// <returns>The participant's identity info (or null if not found).</returns>
        public static Identity TryGetParticipantIdentity(IParticipant participant)
        {
            var identitySet = participant?.Resource?.Info?.Identity;
            var identity = identitySet?.User;

            if (identity == null &&
                identitySet != null &&
                identitySet.AdditionalData.Any(kvp => kvp.Value is Microsoft.Graph.Identity))
            {
                identity = identitySet.AdditionalData.Values.First(v => v is Microsoft.Graph.Identity) as Microsoft.Graph.Identity;
            }

            return identity;
        }



        private bool CheckParticipantIsUsable(IParticipant p)
        {
            foreach (var i in p.Resource.Info.Identity.AdditionalData)
                if (i.Key != "applicationInstance" && i.Value is Identity)
                    return true;

            return false;
        }

    }
}