// <copyright file="Program.cs" company="Microsoft Corporation">
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license.
// </copyright>

using PsiBot.Service.Settings;
using Microsoft.AspNetCore;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;

namespace PsiBot.Services
{
    public class Program
    {
        public static void Main(string[] args)
        {
            CreateWebHostBuilder(args).Build().Run();
        }

        public static IWebHostBuilder CreateWebHostBuilder(string[] args) =>
            WebHost.CreateDefaultBuilder(args)
                .UseStartup<Startup>()
                .UseKestrel((ctx, opt) =>
                {
                         var config = new BotConfiguration();
                        ctx.Configuration.GetSection(nameof(BotConfiguration)).Bind(config);
                       config.Initialize();
                    opt.Configure();                     
                }).UseIISIntegration();
    }
}