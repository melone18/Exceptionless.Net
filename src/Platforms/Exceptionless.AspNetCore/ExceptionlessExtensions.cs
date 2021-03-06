﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Exceptionless.AspNetCore;
using Exceptionless.Models;
using Exceptionless.Models.Data;
using Exceptionless.Plugins.Default;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Exceptionless {
    public static class ExceptionlessExtensions {
        public static IApplicationBuilder UseExceptionless(this IApplicationBuilder app, ExceptionlessClient client = null) {
            if (client == null)
                client = ExceptionlessClient.Default;

            client.Startup();
            client.Configuration.AddPlugin<ExceptionlessAspNetCorePlugin>();
            client.Configuration.AddPlugin<IgnoreUserAgentPlugin>();
            //client.Configuration.Resolver.Register<ILastReferenceIdManager, WebLastReferenceIdManager>();
            
            var diagnosticListener = app.ApplicationServices.GetRequiredService<DiagnosticListener>();
            diagnosticListener?.SubscribeWithAdapter(new ExceptionlessDiagnosticListener(client));

            var lifetime = app.ApplicationServices.GetRequiredService<IApplicationLifetime>();
            lifetime.ApplicationStopping.Register(() => client.ProcessQueue());

            return app.UseMiddleware<ExceptionlessMiddleware>(client);
        }

        public static IApplicationBuilder UseExceptionless(this IApplicationBuilder app, IConfiguration configuration) {
            ExceptionlessClient.Default.Configuration.ReadFromConfiguration(configuration);
            return app.UseExceptionless(ExceptionlessClient.Default);
        }

        public static IApplicationBuilder UseExceptionless(this IApplicationBuilder app, string apiKey) {
            ExceptionlessClient.Default.Configuration.ApiKey = apiKey;
            return app.UseExceptionless(ExceptionlessClient.Default);
        }

        /// <summary>
        /// Sets the configuration from .net configuration settings.
        /// </summary>
        /// <param name="config">The configuration object you want to apply the settings to.</param>
        /// <param name="settings">The configuration settings</param>
        public static void ReadFromConfiguration(this ExceptionlessConfiguration config, IConfiguration settings) {
            if (config == null)
                throw new ArgumentNullException(nameof(config));

            if (settings == null)
                throw new ArgumentNullException(nameof(settings));

            var section = settings.GetSection("Exceptionless");

            string apiKey = section["ApiKey"];
            if (!String.IsNullOrEmpty(apiKey) && apiKey != "API_KEY_HERE")
                config.ApiKey = apiKey;

            foreach (var data in section.GetSection("DefaultData").GetChildren())
                if (data.Value != null)
                    config.DefaultData[data.Key] = data.Value;

            foreach (var tag in section.GetSection("DefaultTags").GetChildren())
                config.DefaultTags.Add(tag.Value);

            bool enabled;
            if (Boolean.TryParse(section["Enabled"], out enabled) && !enabled)
                config.Enabled = false;

            bool includePrivateInformation;
            if (Boolean.TryParse(section["IncludePrivateInformation"], out includePrivateInformation) && !includePrivateInformation)
                config.IncludePrivateInformation = false;

            string serverUrl = section["ServerUrl"];
            if (!String.IsNullOrEmpty(serverUrl))
                config.ServerUrl = serverUrl;

            foreach (var setting in section.GetSection("Settings").GetChildren())
                if (setting.Value != null)
                    config.Settings[setting.Key] = setting.Value;
        }

        /// <summary>
        /// Adds the current request info.
        /// </summary>
        /// <param name="context">The http context to gather information from.</param>
        /// <param name="config">The config.</param>
        public static RequestInfo GetRequestInfo(this HttpContext context, ExceptionlessConfiguration config) {
            return RequestInfoCollector.Collect(context, config.DataExclusions);
        }

        /// <summary>
        /// Adds the current request info as extended data to the event.
        /// </summary>
        /// <param name="ev">The event model.</param>
        /// <param name="context">The http context to gather information from.</param>
        /// <param name="config">The config.</param>
        public static Event AddRequestInfo(this Event ev, HttpContext context, ExceptionlessConfiguration config = null) {
            if (context == null)
                return ev;

            if (config == null)
                config = ExceptionlessClient.Default.Configuration;

            ev.AddRequestInfo(context.GetRequestInfo(config));

            return ev;
        }

        /// <summary>
        /// Adds the current request info as extended data to the event.
        /// </summary>
        /// <param name="builder">The event builder.</param>
        /// <param name="context">The http context to gather information from.</param>
        public static EventBuilder AddRequestInfo(this EventBuilder builder, HttpContext context) {
            builder.Target.AddRequestInfo(context, builder.Client.Configuration);
            return builder;
        }

        internal static HttpContext GetHttpContext(this IDictionary<string, object> data) {
            if (!data.ContainsKey(nameof(HttpContext)))
                return null;

            return data[nameof(HttpContext)] as HttpContext;
        }
    }
}