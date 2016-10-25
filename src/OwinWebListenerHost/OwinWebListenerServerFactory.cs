// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved. See License.txt in the project root for license information.

namespace OwinWebListenerHost
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics.CodeAnalysis;
    using Microsoft.Net.Http.Server;
    using AddressList = System.Collections.Generic.IList<System.Collections.Generic.IDictionary<string, object>>;
    using AppFunc = System.Func<System.Collections.Generic.IDictionary<string, object>, System.Threading.Tasks.Task>;
    using CapabilitiesDictionary = System.Collections.Generic.IDictionary<string, object>;
    using LoggerFactoryFunc = System.Func<string, System.Func<System.Diagnostics.TraceEventType, int, object, System.Exception, System.Func<object, System.Exception, string>, bool>>;
    using LoggerFunc = System.Func<System.Diagnostics.TraceEventType, int, object, System.Exception, System.Func<object, System.Exception, string>, bool>;

    /// <summary>
    /// Implements the Katana setup pattern for the OwinWebListener server.
    /// </summary>
    public static class OwinServerFactory
    {
        /// <summary>
        /// Advertise the capabilities of the server.
        /// </summary>
        /// <param name="properties"></param>
        [SuppressMessage("Microsoft.Reliability", "CA2000:Dispose objects before losing scope", Justification = "Disposed by server later.")]
        public static void Initialize(CapabilitiesDictionary properties)
        {
            if (properties == null)
            {
                throw new ArgumentNullException(nameof(properties));
            }

            properties[Constants.VersionKey] = Constants.OwinVersion;

            CapabilitiesDictionary capabilities =
                properties.Get<CapabilitiesDictionary>(Constants.ServerCapabilitiesKey)
                    ?? new Dictionary<string, object>();
            properties[Constants.ServerCapabilitiesKey] = capabilities;

            DetectWebSocketSupport(properties);

            // Let users set advanced configurations directly.
            var wrapper = new OwinWebListener();
            properties[typeof(OwinWebListener).FullName] = wrapper;
            properties[typeof(System.Net.HttpListener).FullName] = wrapper.Listener;
        }

        private static void DetectWebSocketSupport(IDictionary<string, object> properties)
        {
            // There is no explicit API to detect server side websockets, just check for v4.5 / Win8.
            // Per request we can provide actual verification.
            if (Environment.OSVersion.Version >= new Version(6, 2))
            {
                var capabilities = properties.Get<CapabilitiesDictionary>(Constants.ServerCapabilitiesKey);
                capabilities[Constants.WebSocketVersionKey] = Constants.WebSocketVersion;
            }
            else
            {
                var loggerFactory = properties.Get<LoggerFactoryFunc>(Constants.ServerLoggerFactoryKey);
                LoggerFunc logger = LogHelper.CreateLogger(loggerFactory, typeof(OwinServerFactory));
                LogHelper.LogInfo(logger, "No WebSocket support detected, Windows 8 or later is required.");
            }
        }

        /// <summary>
        /// Creates an OwinWebListener and starts listening on the given URL.
        /// </summary>
        /// <param name="app">The application entry point.</param>
        /// <param name="properties">The addresses to listen on.</param>
        /// <returns>The OwinWebListener.  Invoke Dispose to shut down.</returns>
        [SuppressMessage("Microsoft.Reliability", "CA2000:Dispose objects before losing scope", Justification = "Disposed by caller")]
        public static IDisposable Create(AppFunc app, IDictionary<string, object> properties)
        {
            if (app == null)
            {
                throw new ArgumentNullException(nameof(app));
            }
            if (properties == null)
            {
                throw new ArgumentNullException(nameof(properties));
            }

            // Retrieve the instances created in Initialize
            OwinWebListener wrapper = properties.Get<OwinWebListener>(typeof(OwinWebListener).FullName)
                ?? new OwinWebListener();
            WebListener listener = properties.Get<WebListener>(typeof(WebListener).FullName)
                ?? new WebListener();

            AddressList addresses = properties.Get<AddressList>(Constants.HostAddressesKey)
                ?? new List<IDictionary<string, object>>();

            CapabilitiesDictionary capabilities =
                properties.Get<CapabilitiesDictionary>(Constants.ServerCapabilitiesKey)
                    ?? new Dictionary<string, object>();

            var loggerFactory = properties.Get<LoggerFactoryFunc>(Constants.ServerLoggerFactoryKey);

            wrapper.Start(listener, app, addresses, capabilities, loggerFactory);
            return wrapper;
        }
    }
}
