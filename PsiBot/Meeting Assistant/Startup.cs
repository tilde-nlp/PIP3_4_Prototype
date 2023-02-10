// <copyright file="Startup.cs" company="Microsoft Corporation">
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license.
// </copyright>

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Localization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Razor;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.Graph.Communications.Common.Telemetry;
using Psibot.Service;
using PsiBot.Service.Settings;
using PsiBot.Services.Authentication;
using PsiBot.Services.Bot;
using PsiBot.Services.Logging;
using System.Globalization;

namespace PsiBot.Services
{
    public class Startup
    {
        public Startup(IConfiguration configuration)
        {
            Configuration = configuration;
        }

        public IConfiguration Configuration { get; }

        // This method gets called by the runtime. Use this method to add services to the container.
        public void ConfigureServices(IServiceCollection services)
        {
            services.AddLocalization(options => options.ResourcesPath = "Resources");                
            services.AddMvc().SetCompatibilityVersion(CompatibilityVersion.Version_2_1).AddViewLocalization(LanguageViewLocationExpanderFormat.Suffix);
            
            services.AddSingleton<IGraphLogger, GraphLogger>(_ => new GraphLogger("PsiBot", redirectToTrace: false));
            services.AddSingleton<InMemoryObserver, InMemoryObserver>();
            services.Configure<BotConfiguration>(Configuration.GetSection(nameof(BotConfiguration)));
            services.Configure<ASRConfiguration>(Configuration.GetSection(nameof(ASRConfiguration)));
            var section = Configuration.GetSection("Authentication");
            services.Configure<AuthenticationConfiguration>(section);
            services.PostConfigure<BotConfiguration>(config => config.Initialize());
            services.AddSignalR();
            services.AddSingleton<CaptionHubState>();
            services.AddTransient<AuthenticationKeyService>();
            services.AddSingleton<MachineTranslation>(provider =>
            {
                var mt = new MachineTranslation(provider.GetRequiredService<IOptions<BotConfiguration>>());
                mt.initialize().ConfigureAwait(false);
                return mt;
            });
            services.AddSingleton(provider =>
            {
                return new MeetingLogger(provider.GetRequiredService<MachineTranslation>(), provider.GetRequiredService<IServiceScopeFactory>());
            });
            services.AddSingleton<IBotService, BotService>(provider =>
            {
                var bot = new BotService(
                    provider.GetRequiredService<IGraphLogger>(),
                    provider.GetRequiredService<IOptions<BotConfiguration>>(),
                    provider.GetRequiredService<IOptions<ASRConfiguration>>(),
                    provider.GetRequiredService<IHubContext<CaptionHub>>(),
                    provider.GetRequiredService<MeetingLogger>(),
                    provider.GetRequiredService<MachineTranslation>(),
                    provider.GetRequiredService<CaptionHubState>());
                bot.Initialize();
                return bot;
            });
        }

        // This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
        public void Configure(IApplicationBuilder app, IHostingEnvironment env)
        {
            var supportedCultures = new[]
            {
                new CultureInfo("en"),
                new CultureInfo("lv"),
                new CultureInfo("lt"),
                new CultureInfo("et"),
                new CultureInfo("ru")
            };

            app.UseRequestLocalization(new RequestLocalizationOptions
            {
                DefaultRequestCulture = new RequestCulture("en", "en"),
                // Formatting numbers, dates, etc.
                SupportedCultures = supportedCultures,
                // UI strings that we have localized.
                SupportedUICultures = supportedCultures
            });

            if (env.IsDevelopment())
            {
                app.UseDeveloperExceptionPage();
            }

            app.UseCors(builder =>
            {
                builder.SetIsOriginAllowed(origin => true)
                    .AllowAnyHeader()
                    .AllowAnyMethod()
                    .AllowCredentials();
            });

            app.UseSignalR(routes =>
            {
                routes.MapHub<CaptionHub>("/captionHub");
            });
            app.UseMvc();

            //warm ups
            app.ApplicationServices.GetService<MachineTranslation>();
            app.ApplicationServices.GetService<IGraphLogger>().DiagnosticLevel = System.Diagnostics.TraceLevel.Warning;
        }
    }
}
