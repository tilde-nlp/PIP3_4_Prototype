// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license.

namespace Microsoft.Psi.TeamsBot
{
    using System;
    using System.Collections.Generic;
    using System.Drawing;
    using System.Drawing.Drawing2D;
    using System.Linq;
    using Microsoft.Psi.Audio;
    using Microsoft.Psi.Components;
    using Microsoft.Psi.Imaging;
    using PsiImage = Microsoft.Psi.Imaging.Image;

    /// <summary>
    /// Represents a participant engagement component base class.
    /// </summary>
    public class TeamsBot : Subpipeline
    {
        /// <summary>
        /// Acoustic log energy threshold used for voice activity detection.
        /// </summary>
        protected const float EnergyThreshold = 6.0f;

        /// <summary>
        /// Video thumbnail scale relative to window size.
        /// </summary>
        protected const double ThumbnailWindowScale = 0.25;

        /// <summary>
        /// Video frame margin in pixels.
        /// </summary>
        protected const double FrameMarginWindowScale = 0.03;

        /// <summary>
        /// Video image border thickness in pixels.
        /// </summary>
        protected const int ImageBorderThickness = 4;

        private readonly Connector<Dictionary<string, (AudioBuffer, DateTime)>> audioInConnector;
        private readonly Connector<Dictionary<string, (Shared<PsiImage>, DateTime)>> videoInConnector;
        private readonly Connector<Shared<PsiImage>> screenShareOutConnector;

        private readonly TimeSpan speechWindow = TimeSpan.FromSeconds(5);
        private readonly Bitmap icon;
        private readonly Color backgroundColor = Color.FromArgb(71, 71, 71);
        private readonly Brush textBrush = Brushes.Black;
        private readonly Brush emptyThumbnailBrush = new SolidBrush(Color.FromArgb(30, 30, 30));
        private readonly Brush labelBrush = Brushes.Gray;
        private readonly Font statusFont = new(FontFamily.GenericSansSerif, 12);
        private readonly Font labelFont = new(FontFamily.GenericSansSerif, 36);

        /// <summary>
        /// Initializes a new instance of the <see cref="ParticipantEngagementBotBase"/> class.
        /// </summary>
        /// <param name="pipeline">The pipeline to add the component to.</param>
        /// <param name="interval">Interval at which to render and emit frames of the rendered visual.</param>
        /// <param name="screenWidth">Width at which to render the shared screen.</param>
        /// <param name="screenHeight">Height at which to render the shared screen.</param>
        public TeamsBot(Pipeline pipeline)
            : base(pipeline, "Bot")
        {
            if (pipeline == null)
            {
                throw new ArgumentNullException(nameof(pipeline));
            }

            this.icon = new Bitmap("./icon.png");        
            this.audioInConnector = this.CreateInputConnectorFrom<Dictionary<string, (AudioBuffer, DateTime)>>(pipeline, nameof(this.audioInConnector));                           
        }
        
        public Receiver<Dictionary<string, (AudioBuffer, DateTime)>> AudioIn => this.audioInConnector.In;
                
        /// <inheritdoc />
        public bool EnableAudioOutput => false;

        /// <inheritdoc />
        public Emitter<AudioBuffer> AudioOut { get; } = null;
         
        /// <summary>
        /// Represents a meeting participant.
        /// </summary>
        protected class Participant
        {
            /// <summary>
            /// Initializes a new instance of the <see cref="Participant"/> class.
            /// </summary>
            /// <param name="thumbnail">Video thumbnail.</param>
            /// <param name="x">Horizontal position of video thumbnail as vector from center.</param>
            /// <param name="y">Vertical position of video thumbnail as vector from center.</param>
            /// <param name="width">Width of video thumbnail as unit screen width.</param>
            /// <param name="height">Height of video thumbnail as unit screen height.</param>
            /// <param name="label">Label text.</param>
            public Participant(Shared<PsiImage> thumbnail, double x, double y, double width, double height, string label = default)
            {
                this.Thumbnail = thumbnail;
                this.X = x;
                this.Y = y;
                this.Width = width;
                this.Height = height;
                this.Label = label ?? string.Empty;
            }

            /// <summary>
            /// Gets horizontal position of video thumbnail as vector from center.
            /// </summary>
            public double X { get; }

            /// <summary>
            /// Gets vertical position of video thumbnail as vector from center.
            /// </summary>
            public double Y { get; }

            /// <summary>
            /// Gets label text.
            /// </summary>
            public string Label { get; }

            /// <summary>
            /// Gets latest video thumbnail.
            /// </summary>
            public Shared<PsiImage> Thumbnail { get; }

            /// <summary>
            /// Gets or sets width of video thumbnail as unit screen width.
            /// </summary>
            public double Width { get; set; }

            /// <summary>
            /// Gets or sets height of video thumbnail as unit screen height.
            /// </summary>
            public double Height { get; set; }

            /// <summary>
            /// Gets or sets recent (voice) activity level.
            /// </summary>
            public double Activity { get; set; }
        }
    }
}
