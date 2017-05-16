// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

namespace System.Xml
{
    using System;
    using System.IO;
    using System.Security;
    using System.Collections;
    using System.Net;
    using System.Net.Cache;
    using System.Runtime.Versioning;
    using System.Threading.Tasks;
    using System.Net.Http;
    //
    // XmlDownloadManager
    //
    internal partial class XmlDownloadManager
    {
        internal Task<Stream> GetStreamAsync(Uri uri, ICredentials credentials, IWebProxy proxy,
            RequestCachePolicy cachePolicy)
        {
            if (uri.Scheme == "file")
            {
                return Task.Run<Stream>(() => { return new FileStream(uri.LocalPath, FileMode.Open, FileAccess.Read, FileShare.Read, 1, true); });
            }
            else
            {
                return GetNonFileStreamAsync(uri, credentials, proxy, cachePolicy);
            }
        }

        private async Task<Stream> GetNonFileStreamAsync(Uri uri, ICredentials credentials, IWebProxy proxy,
            RequestCachePolicy cachePolicy)
        {
            WebRequest req = WebRequest.Create(uri);
            if (credentials != null)
            {
                req.Credentials = credentials;
            }
            if (proxy != null)
            {
                req.Proxy = proxy;
            }
            if (cachePolicy != null)
            {
                req.CachePolicy = cachePolicy;
            }

            WebResponse resp = await Task<WebResponse>.Factory.FromAsync(req.BeginGetResponse, req.EndGetResponse, null).ConfigureAwait(false);
            HttpWebRequest webReq = req as HttpWebRequest;
            if (webReq != null)
            {
                lock (this)
                {
                    if (_connections == null)
                    {
                        _connections = new Hashtable();
                    }
                    OpenedHost openedHost = (OpenedHost)_connections[webReq.Address.Host];
                    if (openedHost == null)
                    {
                        openedHost = new OpenedHost();
                    }

                    if (openedHost.nonCachedConnectionsCount < webReq.ServicePoint.ConnectionLimit - 1)
                    {
                        // we are not close to connection limit -> don't cache the stream
                        if (openedHost.nonCachedConnectionsCount == 0)
                        {
                            _connections.Add(webReq.Address.Host, openedHost);
                        }
                        openedHost.nonCachedConnectionsCount++;
                        return new XmlRegisteredNonCachedStream(resp.GetResponseStream(), this, webReq.Address.Host);
                    }
                    else
                    {
                        // cache the stream and save the connection for the next request
                        return new XmlCachedStream(resp.ResponseUri, resp.GetResponseStream());
                    }
                }
            }
            else
            {
                return resp.GetResponseStream();
            }
        }
    }
}
