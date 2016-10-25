// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved. See License.txt in the project root for license information.

namespace OwinWebListenerHost
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Diagnostics.CodeAnalysis;
    using System.Diagnostics.Contracts;
    using System.Net;
    using System.Reflection;
    using System.Threading;
    using System.Threading.Tasks;
    using Microsoft.Net.Http.Server;
    using Microsoft.Owin.BuilderProperties;
    using OwinWebListenerHost.RequestProcessing;
    using AppFunc = System.Func<System.Collections.Generic.IDictionary<string, object>, System.Threading.Tasks.Task>;
    using LoggerFactoryFunc = System.Func<string, System.Func<System.Diagnostics.TraceEventType, int, object, System.Exception, System.Func<object, System.Exception, string>, bool>>;
    using LoggerFunc = System.Func<System.Diagnostics.TraceEventType, int, object, System.Exception, System.Func<object, System.Exception, string>, bool>;

    /// <summary>
    /// This wraps HttpListener and exposes it as an OWIN compatible server.
    /// </summary>
    public sealed class OwinWebListener : IDisposable
    {
        private const int DefaultMaxRequests = Int32.MaxValue;
        private const long DefaultRequestQueueLength = 1000;  // Http.sys default.
        private static readonly int DefaultMaxAccepts = 5 * Environment.ProcessorCount;
        private static readonly bool IsMono = Type.GetType("Mono.Runtime") != null;
        private static readonly FieldInfo CookedPathField = typeof(HttpListenerRequest).GetField("m_CookedUrlPath", BindingFlags.NonPublic | BindingFlags.Instance);
        private static readonly FieldInfo CookedQueryField = typeof(HttpListenerRequest).GetField("m_CookedUrlQuery", BindingFlags.NonPublic | BindingFlags.Instance);

        private readonly Action _startNextRequestAsync;
        private readonly Action<Task> _startNextRequestError;
        private WebListener _listener;
        private IList<string> _basePaths;
        private AppFunc _appFunc;
        private DisconnectHandler _disconnectHandler;
        private IDictionary<string, object> _capabilities;
        private PumpLimits _pumpLimits;
        private int _currentOutstandingAccepts;
        private int _currentOutstandingRequests;
        private LoggerFunc _logger;
        private long? _requestQueueLength;

        /// <summary>
        /// Creates a listener wrapper that can be configured by the user before starting.
        /// </summary>
        public OwinWebListener()
        {
            _listener = new WebListener();
            _startNextRequestAsync = new Action(ProcessRequestsAsync);
            _startNextRequestError = new Action<Task>(StartNextRequestError);
            SetRequestProcessingLimits(DefaultMaxAccepts, DefaultMaxRequests);
        }

        /// <summary>
        /// The HttpListener instance wrapped by this wrapper.
        /// </summary>
        public WebListener Listener => _listener;

        private bool CanAcceptMoreRequests
        {
            get
            {
                PumpLimits limits = _pumpLimits;
                return (_currentOutstandingAccepts < limits.MaxOutstandingAccepts
                    && _currentOutstandingRequests < limits.MaxOutstandingRequests - _currentOutstandingAccepts);
            }
        }

        /// <summary>
        /// These are merged as one call because they should be swapped out atomically.
        /// This controls how many requests the server attempts to process concurrently.
        /// </summary>
        /// <param name="maxAccepts">The maximum number of pending request receives.</param>
        /// <param name="maxRequests">The maximum number of active requests being processed.</param>
        public void SetRequestProcessingLimits(int maxAccepts, int maxRequests)
        {
            _pumpLimits = new PumpLimits(maxAccepts, maxRequests);

            if (_listener.IsListening)
            {
                // Kick the pump in case we went from zero to non-zero limits.
                OffloadStartNextRequest();
            }
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="maxAccepts"></param>
        /// <param name="maxRequests"></param>
        [SuppressMessage("Microsoft.Design", "CA1021:AvoidOutParameters", MessageId = "0#", Justification = "By design")]
        [SuppressMessage("Microsoft.Design", "CA1021:AvoidOutParameters", MessageId = "1#", Justification = "By design")]
        public void GetRequestProcessingLimits(out int maxAccepts, out int maxRequests)
        {
            PumpLimits limits = _pumpLimits;
            maxAccepts = limits.MaxOutstandingAccepts;
            maxRequests = limits.MaxOutstandingRequests;
        }

        /// <summary>
        /// Sets the maximum number of requests that will be queued up in Http.Sys.
        /// </summary>
        /// <param name="limit"></param>
        public void SetRequestQueueLimit(long limit)
        {
            if (limit <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(limit), limit, string.Empty);
            }
            if ((!_requestQueueLength.HasValue && limit == DefaultRequestQueueLength)
                || (_requestQueueLength.HasValue && limit == _requestQueueLength.Value))
            {
                return;
            }

            _requestQueueLength = limit;

            SetRequestQueueLimit();
        }

        private void SetRequestQueueLimit()
        {
            // The listener must be active for this to work.  Call from Start after activating.
            // Platform check. This isn't supported on XP / Http.Sys v1.0, or Mono.
            if (IsMono || !_listener.IsListening || !_requestQueueLength.HasValue || Environment.OSVersion.Version.Major < 6)
            {
                return;
            }

            NativeMethods.SetRequestQueueLength(_listener, _requestQueueLength.Value);
        }

        public void Start(AppFunc appFunc, string url)
        {
            var addressCollection = new AddressCollection(new List<IDictionary<string, object>>())
            {
                new Address("http", "localhost", "1083", "/")
            };
            Start(appFunc, addressCollection.List);
        }

        public void Start(AppFunc appFunc, IList<IDictionary<string, object>> addresses)
        {
            Start(Listener, appFunc, addresses, new Dictionary<string, object>(), NoopLoggerFactory);
        }

        private static Func<TraceEventType, int, object, Exception, Func<object, Exception, string>, bool> NoopLoggerFactory(string s)
        {
            return (type, i, arg3, arg4, arg5) => false;
        }

        /// <summary>
        /// Starts the listener and request processing threads.
        /// </summary>
        public void Start(WebListener listener, AppFunc appFunc, IList<IDictionary<string, object>> addresses,
            IDictionary<string, object> capabilities, LoggerFactoryFunc loggerFactory)
        {
            Contract.Assert(_appFunc == null); // Start should only be called once
            Contract.Assert(appFunc != null);
            Contract.Assert(addresses != null);

            _listener = listener;
            _appFunc = appFunc;
            _logger = LogHelper.CreateLogger(loggerFactory, typeof(OwinWebListener));

            _basePaths = new List<string>();

            foreach (var address in addresses)
            {
                // build url from parts
                string scheme = address.Get<string>("scheme") ?? Uri.UriSchemeHttp;
                string host = address.Get<string>("host") ?? "localhost";
                string port = address.Get<string>("port") ?? "5000";
                string path = address.Get<string>("path") ?? string.Empty;

                // if port is present, add delimiter to value before concatenation
                if (!string.IsNullOrWhiteSpace(port))
                {
                    port = ":" + port;
                }

                // Assume http(s)://+:9090/BasePath/, including the first path slash.  May be empty. Must end with a slash.
                if (!path.EndsWith("/", StringComparison.Ordinal))
                {
                    // Http.Sys requires that the URL end in a slash
                    path += "/";
                }
                _basePaths.Add(path);

                // add a server for each url
                string url = scheme + "://" + host + port + path;
                _listener.Settings.UrlPrefixes.Add(url);
            }

            _capabilities = capabilities;

            if (!_listener.IsListening)
            {
                _listener.Start();
            }

            SetRequestQueueLimit();

            _disconnectHandler = new DisconnectHandler(_listener, _logger);

            OffloadStartNextRequest();
        }

        private void OffloadStartNextRequest()
        {
            if (_listener.IsListening && CanAcceptMoreRequests)
            {
                Task.Factory.StartNew(_startNextRequestAsync)
                    .ContinueWith(_startNextRequestError, TaskContinuationOptions.OnlyOnFaulted);
            }
        }

        private async void ProcessRequestsAsync()
        {
            while (_listener.IsListening && CanAcceptMoreRequests)
            {
                Interlocked.Increment(ref _currentOutstandingAccepts);

                RequestContext context;
                try
                {
                    context = await _listener.AcceptAsync();
                }
                catch (ApplicationException ae)
                {
                    // These come from the thread pool if HttpListener tries to call BindHandle after the listener has been disposed.
                    Interlocked.Decrement(ref _currentOutstandingAccepts);
                    LogHelper.LogException(_logger, "Accept", ae);
                    return;
                }
                catch (WebListenerException wle)
                {
                    // These happen if HttpListener has been disposed
                    Interlocked.Decrement(ref _currentOutstandingAccepts);
                    LogHelper.LogException(_logger, "Accept", wle);
                    return;
                }
                catch (ObjectDisposedException ode)
                {
                    // These happen if HttpListener has been disposed
                    Interlocked.Decrement(ref _currentOutstandingAccepts);
                    LogHelper.LogException(_logger, "Accept", ode);
                    return;
                }

                Interlocked.Decrement(ref _currentOutstandingAccepts);
                Interlocked.Increment(ref _currentOutstandingRequests);
                OffloadStartNextRequest();

                // This needs to be separate from ProcessRequestsAsync so that async/await will clean up the execution context.
                // This prevents changes to Thread.CurrentPrincipal from leaking across requests.
                await ProcessRequestAsync(context);
            }
        }

        // This needs to be separate from ProcessRequestsAsync so that async/await will clean up the execution context.
        // This prevents changes to Thread.CurrentPrincipal from leaking across requests.
        private async Task ProcessRequestAsync(RequestContext context)
        {
            OwinHttpListenerContext owinContext = null;
            try
            {
                string pathBase, path, query;
                GetPathAndQuery(context.Request, out pathBase, out path, out query);
                owinContext = new OwinHttpListenerContext(context, pathBase, path, query, _disconnectHandler);
                PopulateServerKeys(owinContext.Environment);
                Contract.Assert(!owinContext.Environment.IsExtraDictionaryCreated,
                    "All keys set by the server should have reserved slots.");

                await _appFunc(owinContext.Environment);
                await owinContext.Response.CompleteResponseAsync();
                owinContext.Response.Close();

                owinContext.End();
                owinContext.Dispose();

                Interlocked.Decrement(ref _currentOutstandingRequests);
            }
            catch (Exception ex)
            {
                Interlocked.Decrement(ref _currentOutstandingRequests);
                LogHelper.LogException(_logger, "Exception during request processing.", ex);

                if (owinContext != null)
                {
                    owinContext.End(ex);
                    owinContext.Dispose();
                }
            }
        }

        private void StartNextRequestError(Task faultedTask)
        {
            // StartNextRequestAsync should handle it's own exceptions.
            LogHelper.LogException(_logger, "Unexpected exception.", faultedTask.Exception);
            Contract.Assert(false, "Un-expected exception path: " + faultedTask.Exception.ToString());
#if DEBUG
            // Break into the debugger in case the message pump fails.
            System.Diagnostics.Debugger.Break();
#endif
        }

        // When the server is listening on multiple urls, we need to decide which one is the correct base path for this request.
        private void GetPathAndQuery(Request request, out string pathBase, out string path, out string query)
        {
            pathBase = request.PathBase;
            path = request.Path;
            query = request.QueryString;
        }

        private void PopulateServerKeys(CallEnvironment env)
        {
            env.ServerCapabilities = _capabilities;
            env.Listener = _listener;
            env.OwinWebListener = this;
        }

        internal void Stop()
        {
            try
            {
                _listener.Dispose();
            }
            catch (ObjectDisposedException)
            {
            }
        }

        /// <summary>
        /// Shuts down the listener and disposes it.
        /// </summary>
        public void Dispose()
        {
            if (_listener.IsListening)
            {
                _listener.Dispose();
            }

            ((IDisposable)_listener).Dispose();
        }
    }
}
