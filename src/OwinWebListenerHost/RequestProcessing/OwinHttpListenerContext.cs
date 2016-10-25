// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved. See License.txt in the project root for license information.

namespace OwinWebListenerHost.RequestProcessing
{
    using System;
    using System.IO;
    using System.Net;
    using System.Security.Cryptography.X509Certificates;
    using System.Security.Principal;
    using System.Threading;
    using Microsoft.Net.Http.Server;
    using WebSocketAccept = System.Action<System.Collections.Generic.IDictionary<string, object>, // WebSocket Accept parameters
            System.Func<System.Collections.Generic.IDictionary<string, object>, System.Threading.Tasks.Task>>;

    internal class OwinHttpListenerContext : IDisposable, CallEnvironment.IPropertySource
    {
        private readonly RequestContext _requestContext;
        private readonly OwinHttpListenerRequest _owinRequest;
        private readonly OwinHttpListenerResponse _owinResponse;
        private readonly CallEnvironment _environment;
        private readonly DisconnectHandler _disconnectHandler;

        private CancellationTokenSource _cts;
        private CancellationTokenRegistration _disconnectRegistration;
        private IPrincipal _user;

        internal OwinHttpListenerContext(RequestContext requestContext,
            string basePath, string path, string query, DisconnectHandler disconnectHandler)
        {
            _requestContext = requestContext;
            _environment = new CallEnvironment(this);
            _owinRequest = new OwinHttpListenerRequest(_requestContext.Request, basePath, path, query, _environment);
            _owinResponse = new OwinHttpListenerResponse(_requestContext, _environment);
            _disconnectHandler = disconnectHandler;

            _environment.OwinVersion = Constants.OwinVersion;

            SetServerUser(_requestContext.User);
            _environment.RequestContext = _requestContext;
        }

        internal CallEnvironment Environment => _environment;

        internal OwinHttpListenerRequest Request => _owinRequest;

        internal OwinHttpListenerResponse Response => _owinResponse;

        internal void End(Exception ex)
        {
            if (ex != null)
            {
                // TODO: LOG
                // Lazy initialized
                if (_cts != null)
                {
                    try
                    {
                        _cts.Cancel();
                    }
                    catch (ObjectDisposedException)
                    {
                    }
                    catch (AggregateException)
                    {
                        // TODO: LOG
                    }
                }
            }

            End();
        }

        internal void End()
        {
            try
            {
                _disconnectRegistration.Dispose();
            }
            catch (ObjectDisposedException)
            {
                // CTR.Dispose() may throw an ODE on 4.0 if the CTS has previously been disposed.  Removed in 4.5.
            }
            _owinResponse.End();
        }

        private static void SetDisconnected(object state)
        {
            var context = (OwinHttpListenerContext)state;
            context.End(new HttpListenerException(Constants.ErrorConnectionNoLongerValid));
        }

        public void Dispose()
        {
            Dispose(true);
        }

        public virtual void Dispose(bool disposing)
        {
            if (disposing)
            {
                try
                {
                    _disconnectRegistration.Dispose();
                }
                catch (ObjectDisposedException)
                {
                    // CTR.Dispose() may throw an ODE on 4.0 if the CTS has previously been disposed.  Removed in 4.5.
                }
                if (_cts != null)
                {
                    _cts.Dispose();
                }
            }
        }

        // Lazy environment initialization

        public CancellationToken GetCallCancelled()
        {
            _cts = new CancellationTokenSource();
            CancellationToken ct = _disconnectHandler.GetDisconnectToken(_requestContext);
            _disconnectRegistration = ct.Register(SetDisconnected, this);
            return _cts.Token;
        }

        public Stream GetRequestBody()
        {
            return _owinRequest.GetRequestBody();
        }

        public string GetServerRemoteIpAddress()
        {
            return _owinRequest.GetRemoteIpAddress();
        }

        public string GetServerRemotePort()
        {
            return _owinRequest.GetRemotePort();
        }

        public string GetServerLocalIpAddress()
        {
            return _owinRequest.GetLocalIpAddress();
        }

        public string GetServerLocalPort()
        {
            return _owinRequest.GetLocalPort();
        }

        public bool GetServerIsLocal()
        {
            return _owinRequest.GetIsLocal();
        }

        public IPrincipal GetServerUser()
        {
            return _user;
        }

        public void SetServerUser(IPrincipal user)
        {
            _user = user;
            Thread.CurrentPrincipal = _user;
        }

        public bool TryGetClientCert(ref X509Certificate value)
        {
            Exception clientCertErrors = null;
            bool result = _owinRequest.TryGetClientCert(ref value, ref clientCertErrors);
            Environment.ClientCertErrors = clientCertErrors;
            return result;
        }

        public bool TryGetClientCertErrors(ref Exception value)
        {
            X509Certificate clientCert = null;
            bool result = _owinRequest.TryGetClientCert(ref clientCert, ref value);
            Environment.ClientCert = clientCert;
            return result;
        }

        public bool TryGetWebSocketAccept(ref WebSocketAccept websocketAccept)
        {
            return _owinResponse.TryGetWebSocketAccept(ref websocketAccept);
        }
    }
}
