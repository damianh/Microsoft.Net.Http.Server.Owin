// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved. See License.txt in the project root for license information.

namespace OwinWebListenerHost
{
    using System;
    using System.Diagnostics;
    using System.Globalization;
    using LoggerFactoryFunc = System.Func<string, System.Func<System.Diagnostics.TraceEventType, int, object, System.Exception, System.Func<object, System.Exception, string>, bool>>;
    using LoggerFunc = System.Func<System.Diagnostics.TraceEventType, int, object, System.Exception, System.Func<object, System.Exception, string>, bool>;

    internal static class LogHelper
    {
        private static readonly Func<object, Exception, string> LogState =
            (state, error) => Convert.ToString(state, CultureInfo.CurrentCulture);

        private static readonly Func<object, Exception, string> LogStateAndError =
            (state, error) => string.Format(CultureInfo.CurrentCulture, "{0}\r\n{1}", state, error);

        internal static LoggerFunc CreateLogger(LoggerFactoryFunc factory, Type type)
        {
            if (factory == null)
            {
                return null;
            }

            return factory(type.FullName);
        }

        internal static void LogInfo(LoggerFunc logger, string data)
        {
            if (logger == null)
            {
                Debug.WriteLine(data);
            }
            else
            {
                logger(TraceEventType.Information, 0, data, null, LogState);
            }
        }

        internal static void LogException(LoggerFunc logger, string location, Exception exception)
        {
            if (logger == null)
            {
                Debug.WriteLine(exception);
            }
            else
            {
                logger(TraceEventType.Error, 0, location, exception, LogStateAndError);
            }
        }
    }
}
