// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved. See License.txt in the project root for license information.

namespace OwinWebListenerHost
{
    using System;
    using System.Runtime.InteropServices;
    using System.Security;
    using System.Threading;
    using Microsoft.Net.Http.Server;

    [SuppressUnmanagedCodeSecurity]
    internal static class NativeMethods
    {
        internal enum HTTP_SERVER_PROPERTY
        {
            HttpServerAuthenticationProperty,
            HttpServerLoggingProperty,
            HttpServerQosProperty,
            HttpServerTimeoutsProperty,
            HttpServerQueueLengthProperty,
            HttpServerStateProperty,
            HttpServer503VerbosityProperty,
            HttpServerBindingProperty,
            HttpServerExtendedAuthenticationProperty,
            HttpServerListenEndpointProperty,
            HttpServerChannelBindProperty,
            HttpServerProtectionLevelProperty,
        }

        [DllImport("httpapi.dll", CallingConvention = CallingConvention.StdCall, SetLastError = true, ExactSpelling = true)]
        internal static extern uint HttpSetRequestQueueProperty(
            CriticalHandle requestQueueHandle,
            HTTP_SERVER_PROPERTY serverProperty,
            IntPtr pPropertyInfo,
            uint propertyInfoLength,
            uint reserved,
            IntPtr pReserved);

        internal static void SetRequestQueueLength(WebListener listener, long length)
        {
            listener.Settings.RequestQueueLimit = length;
        }

        [DllImport("httpapi.dll", CallingConvention = CallingConvention.StdCall, SetLastError = true, ExactSpelling = true)]
        internal static extern unsafe uint HttpWaitForDisconnectEx(CriticalHandle requestQueueHandle, ulong connectionId, uint reserved, NativeOverlapped* pOverlapped);

        internal static class HttpErrors
        {
            // ReSharper disable InconsistentNaming
            public const int NO_ERROR = 0x0;
            public const int ERROR_IO_PENDING = 0x3E5;
            // ReSharper restore InconsistentNaming
        }
    }
}
